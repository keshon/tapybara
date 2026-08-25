using System.Diagnostics;
using Tapybara.Core.Audio;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Settings;
using Tapybara.Core.Speech;

namespace Tapybara.Core.Dictation;

/// <summary>Состояние диктовки.</summary>
public enum DictationState
{
    Idle,
    Recording,
    Transcribing,
}

/// <summary>
/// О чём контроллер сообщает наружу.
/// </summary>
/// <remarks>
/// Перечисление, а не готовая строка. <c>Core</c> не знает языка интерфейса и
/// знать не должен: пока он раздавал русский текст, английский пользователь
/// видел в трее «Диктовка не удалась» — при полностью английском интерфейсе.
/// Формулировки принадлежат слою, у которого есть словарь.
/// </remarks>
public enum DictationNotice
{
    /// <summary>Диктовка сорвалась. В <c>Detail</c> — техническая причина.</summary>
    Failed,

    /// <summary>Модель не загрузилась. В <c>Detail</c> — причина.</summary>
    ModelLoadFailed,

    /// <summary>Записи меньше полусекунды — распознавать нечего.</summary>
    TooShort,

    /// <summary>Пользователь отменил.</summary>
    Cancelled,

    /// <summary>Речи не нашлось.</summary>
    Empty,

    /// <summary>Сработал предохранитель по длине: запись остановлена сама.</summary>
    LengthLimitReached,

    /// <summary>Устройство записи отвалилось посреди диктовки.</summary>
    DeviceLost,
}

/// <summary>Сообщение наружу: что случилось и, если нужно, подробность.</summary>
public sealed record DictationStatus(DictationNotice Notice, string? Detail = null);

/// <summary>
/// Конечный автомат диктовки: хоткей → запись → распознавание → готовый текст.
/// </summary>
/// <remarks>
/// <para>
/// Вставкой текста контроллер НЕ занимается: он поднимает <see cref="TextReady"/>,
/// а решение «вставить в окно / положить в буфер / просто показать» принимает
/// вызывающий код. Благодаря этому один и тот же контроллер работает и под
/// интерфейсом, и в консольном харнессе.
/// </para>
/// <para>
/// События поднимаются из фоновых потоков. Подписчик из UI обязан
/// переправлять их в диспетчер сам.
/// </para>
/// </remarks>
public sealed class DictationController : IAsyncDisposable
{
    /// <summary>Сколько последних результатов держим для повторной вставки.</summary>
    private const int HistoryLimit = 10;

    private readonly SpeechTranscriber _transcriber;
    private readonly Func<AppSettings> _settings;
    private readonly List<string> _history = [];

    // Ноль-таймаут при захвате: нажатие хоткея во время распознавания просто
    // игнорируется, а не встаёт в очередь второй записью.
    private readonly SemaphoreSlim _busy = new(1, 1);

    private MicrophoneCapture? _capture;
    private Stopwatch? _recordingTimer;
    private CancellationTokenSource? _transcribeCancellation;
    private Timer? _unloadTimer;
    private Timer? _lengthFuse;
    private bool _disposed;

    public DictationController(SpeechTranscriber transcriber, Func<AppSettings> settings)
    {
        _transcriber = transcriber;
        _settings = settings;
    }

    /// <summary>Текущее состояние.</summary>
    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>Сколько идёт текущая запись.</summary>
    public TimeSpan Elapsed => _recordingTimer?.Elapsed ?? TimeSpan.Zero;

    /// <summary>Последний распознанный текст — страховка «скопировать ещё раз».</summary>
    public string? LastText => _history.Count > 0 ? _history[0] : null;

    /// <summary>
    /// Последние результаты, свежий первым.
    /// </summary>
    /// <remarks>
    /// Одной ячейки было мало: две диктовки подряд — и первая невосстановима,
    /// хотя переспросить её у человека уже нельзя.
    /// </remarks>
    public IReadOnlyList<string> History
    {
        get
        {
            lock (_history)
            {
                return [.. _history];
            }
        }
    }

    public event Action<DictationState>? StateChanged;

    /// <summary>Уровень микрофона 0..1 для индикатора.</summary>
    public event Action<float>? LevelChanged;

    /// <summary>Прогресс распознавания 0–100.</summary>
    public event Action<int>? ProgressChanged;

    /// <summary>Готовый текст, уже очищенный от галлюцинаций и с применённым словарём.</summary>
    public event Action<string>? TextReady;

    /// <summary>Что сообщить пользователю. Формулировку выбирает слой интерфейса.</summary>
    public event Action<DictationStatus>? Status;

    /// <summary>Начать диктовку, если стоим; закончить, если пишем.</summary>
    public async Task ToggleAsync()
    {
        if (!await _busy.WaitAsync(0).ConfigureAwait(false))
        {
            return; // распознавание ещё идёт — нажатие игнорируем
        }

        try
        {
            if (_capture is null)
            {
                StartRecording();
            }
            else
            {
                await StopAndTranscribeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Диктовка сорвалась.", ex);
            await ResetToIdleAsync().ConfigureAwait(false);
            Status?.Invoke(new DictationStatus(DictationNotice.Failed, ex.Message));
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>
    /// Отменить диктовку и выбросить записанное.
    /// </summary>
    /// <remarks>
    /// Отмена — не то же самое, что остановка. Остановка распознаёт записанное,
    /// отмена выбрасывает его: пользователь передумал, и вставлять ему в текст
    /// ничего не надо. Работает и во время записи, и во время распознавания.
    /// </remarks>
    public async Task CancelAsync()
    {
        if (State == DictationState.Transcribing)
        {
            // Читаем в локальную переменную: поле обнуляется из другого потока
            // ровно в тот момент, когда распознавание заканчивается само.
            CancellationTokenSource? cancellation = Volatile.Read(ref _transcribeCancellation);
            try
            {
                cancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Успело закончиться само — отменять уже нечего.
            }

            return;
        }

        if (State != DictationState.Recording || !await _busy.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await ResetToIdleAsync().ConfigureAwait(false);
            Status?.Invoke(new DictationStatus(DictationNotice.Cancelled));
        }
        finally
        {
            _busy.Release();
        }
    }

    private void StartRecording()
    {
        AppSettings settings = _settings();

        var capture = new MicrophoneCapture { DeviceId = settings.MicrophoneDeviceId };
        capture.LevelChanged += OnLevel;
        capture.Failed += OnCaptureFailed;
        capture.Start();

        _capture = capture;
        _recordingTimer = Stopwatch.StartNew();
        StartLengthFuse(settings);
        SetState(DictationState.Recording);

        // Модель грузим ПАРАЛЛЕЛЬНО записи: пока пользователь говорит, она уже
        // прогревается — на распознавании экономится холодный старт целиком.
        _ = Task.Run(async () =>
        {
            try
            {
                await _transcriber.PrepareAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error("Модель не загрузилась.", ex);
                Status?.Invoke(new DictationStatus(DictationNotice.ModelLoadFailed, ex.Message));
            }
        });
    }

    /// <summary>
    /// Предохранитель от забытой диктовки.
    /// </summary>
    /// <remarks>
    /// Настройка на это была с самого начала, а таймера под ней не было вовсе.
    /// Забытая запись росла без предела: около 3,8 МБ в минуту в куче больших
    /// объектов, а потом час звука одним куском уезжал в модель.
    /// </remarks>
    private void StartLengthFuse(AppSettings settings)
    {
        StopLengthFuse();

        int minutes = Math.Clamp(settings.MaxDictationMinutes, 1, 24 * 60);
        _lengthFuse = new Timer(
            _ => _ = OnLengthLimitAsync(),
            state: null,
            TimeSpan.FromMinutes(minutes),
            Timeout.InfiniteTimeSpan);
    }

    private void StopLengthFuse()
    {
        _lengthFuse?.Dispose();
        _lengthFuse = null;
    }

    /// <summary>Время вышло — останавливаем и распознаём то, что записано.</summary>
    private async Task OnLengthLimitAsync()
    {
        if (State != DictationState.Recording)
        {
            return;
        }

        AppLog.Warn("Сработал предохранитель по длине диктовки.");
        Status?.Invoke(new DictationStatus(DictationNotice.LengthLimitReached));
        await ToggleAsync().ConfigureAwait(false);
    }

    private void OnCaptureFailed(Exception error)
    {
        Status?.Invoke(new DictationStatus(DictationNotice.DeviceLost, error.Message));
    }

    private async Task StopAndTranscribeAsync()
    {
        MicrophoneCapture capture = _capture!;
        _capture = null;
        _recordingTimer?.Stop();
        StopLengthFuse();

        capture.LevelChanged -= OnLevel;
        capture.Failed -= OnCaptureFailed;
        float[] samples = await capture.StopAsync().ConfigureAwait(false);
        capture.Dispose();

        var duration = TimeSpan.FromSeconds(samples.Length / (double)AudioCapture.TargetSampleRate);
        if (duration < TimeSpan.FromSeconds(0.5))
        {
            SetState(DictationState.Idle);
            Status?.Invoke(new DictationStatus(DictationNotice.TooShort));
            ScheduleUnload();
            return;
        }

        SetState(DictationState.Transcribing);

        var cancellation = new CancellationTokenSource();
        Volatile.Write(ref _transcribeCancellation, cancellation);
        try
        {
            var progress = new Progress<int>(p => ProgressChanged?.Invoke(p));
            IReadOnlyList<TranscriptSegment> segments = await _transcriber
                .TranscribeAsync(samples, progress, cancellationToken: cancellation.Token)
                .ConfigureAwait(false);

            Publish(segments);
        }
        catch (OperationCanceledException)
        {
            Status?.Invoke(new DictationStatus(DictationNotice.Cancelled));
        }
        finally
        {
            // Сначала снимаем ссылку, потом освобождаем: наоборот получалась
            // щель, в которую отмена успевала позвать Cancel() у уже
            // освобождённого источника.
            Volatile.Write(ref _transcribeCancellation, null);
            cancellation.Dispose();

            SetState(DictationState.Idle);
            ScheduleUnload();
        }
    }

    private void Publish(IReadOnlyList<TranscriptSegment> segments)
    {
        AppSettings settings = _settings();

        // Галлюцинации отсеиваем ПОСЕГМЕНТНО. Проверка целого текста искала бы
        // маркер в любом месте, и одна фраза про подписку на канал выбрасывала
        // бы всю диктовку — вместе со звуком, которого уже нет.
        IReadOnlyList<TranscriptSegment> clean = TextPostProcessor.RemoveHallucinations(segments);

        string joined = TextPostProcessor.JoinSegments(clean, settings.EffectiveParagraphPause);
        string text = TextPostProcessor.ApplyReplacements(joined, settings.Replacements).Trim();

        if (text.Length == 0)
        {
            Status?.Invoke(new DictationStatus(DictationNotice.Empty));
            return;
        }

        // Запоминаем ДО вставки: что бы ни случилось дальше, текст можно
        // забрать из меню, а не диктовать заново.
        Remember(text);
        TextReady?.Invoke(text);
    }

    private void Remember(string text)
    {
        lock (_history)
        {
            _history.Insert(0, text);
            if (_history.Count > HistoryLimit)
            {
                _history.RemoveRange(HistoryLimit, _history.Count - HistoryLimit);
            }
        }
    }

    private void OnLevel(float rms)
    {
        // RMS → 0..1 через децибелы: тихая речь видна на индикаторе,
        // а шум комнаты — нет. Линейная шкала показывала бы почти ноль всегда.
        double db = 20 * Math.Log10(Math.Max(rms, 1e-7));
        LevelChanged?.Invoke((float)Math.Clamp((db + 50) / 40, 0, 1));
    }

    private async Task ResetToIdleAsync()
    {
        StopLengthFuse();

        if (_capture is { } capture)
        {
            _capture = null;
            capture.LevelChanged -= OnLevel;
            capture.Failed -= OnCaptureFailed;
            await capture.StopAsync().ConfigureAwait(false);
            capture.Dispose();
        }

        SetState(DictationState.Idle);
    }

    private void SetState(DictationState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(state);
    }

    /// <summary>Отложенная выгрузка модели.</summary>
    /// <remarks>
    /// Сам таймер решения не принимает: он лишь будит движок, а тот проверяет
    /// время последнего использования. Иначе «осиротевший» таймер, заведённый
    /// до предыдущей диктовки, выгрузил бы модель под свежим распознаванием.
    /// </remarks>
    private void ScheduleUnload()
    {
        var delay = TimeSpan.FromMinutes(Math.Clamp(_settings().IdleUnloadMinutes, 1, 24 * 60));

        _unloadTimer?.Dispose();
        _unloadTimer = new Timer(
            _ => _ = UnloadAsync(),
            state: null,
            delay,
            Timeout.InfiniteTimeSpan);
    }

    private async Task UnloadAsync()
    {
        try
        {
            await _transcriber.Engine.UnloadIfIdleAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
        {
            // Приложение закрывается — выгружать уже нечего.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            Volatile.Read(ref _transcribeCancellation)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Уже завершилось само.
        }

        StopLengthFuse();

        if (_unloadTimer is { } timer)
        {
            await timer.DisposeAsync().ConfigureAwait(false);
            _unloadTimer = null;
        }

        await ResetToIdleAsync().ConfigureAwait(false);
        _busy.Dispose();
    }
}
