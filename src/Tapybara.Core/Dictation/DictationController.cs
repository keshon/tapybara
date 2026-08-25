using System.Diagnostics;
using Tapybara.Core.Audio;
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
    private readonly WhisperEngine _engine;
    private readonly Func<AppSettings> _settings;

    // Ноль-таймаут при захвате: нажатие хоткея во время распознавания просто
    // игнорируется, а не встаёт в очередь второй записью.
    private readonly SemaphoreSlim _busy = new(1, 1);

    private MicrophoneCapture? _capture;
    private Stopwatch? _recordingTimer;
    private CancellationTokenSource? _transcribeCancellation;
    private Timer? _unloadTimer;
    private bool _disposed;

    public DictationController(WhisperEngine engine, Func<AppSettings> settings)
    {
        _engine = engine;
        _settings = settings;
    }

    /// <summary>Текущее состояние.</summary>
    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>Сколько идёт текущая запись.</summary>
    public TimeSpan Elapsed => _recordingTimer?.Elapsed ?? TimeSpan.Zero;

    /// <summary>Последний распознанный текст — страховка «скопировать ещё раз».</summary>
    public string? LastText { get; private set; }

    public event Action<DictationState>? StateChanged;

    /// <summary>Уровень микрофона 0..1 для индикатора.</summary>
    public event Action<float>? LevelChanged;

    /// <summary>Прогресс распознавания 0–100.</summary>
    public event Action<int>? ProgressChanged;

    /// <summary>Готовый текст, уже очищенный от галлюцинаций и с применённым словарём.</summary>
    public event Action<string>? TextReady;

    /// <summary>Человекочитаемое сообщение для строки статуса.</summary>
    public event Action<string>? Status;

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
            await ResetToIdleAsync().ConfigureAwait(false);
            Status?.Invoke($"Диктовка не удалась: {ex.Message}");
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
            _transcribeCancellation?.Cancel();
            return;
        }

        if (State != DictationState.Recording || !await _busy.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await ResetToIdleAsync().ConfigureAwait(false);
            Status?.Invoke("Диктовка отменена");
        }
        finally
        {
            _busy.Release();
        }
    }

    private void StartRecording()
    {
        var capture = new MicrophoneCapture();
        capture.LevelChanged += OnLevel;
        capture.Start();

        _capture = capture;
        _recordingTimer = Stopwatch.StartNew();
        SetState(DictationState.Recording);

        // Модель грузим ПАРАЛЛЕЛЬНО записи: пока пользователь говорит, она уже
        // прогревается — на распознавании экономится холодный старт целиком.
        _ = Task.Run(async () =>
        {
            try
            {
                await _engine.LoadAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Status?.Invoke($"Модель не загрузилась: {ex.Message}");
            }
        });
    }

    private async Task StopAndTranscribeAsync()
    {
        MicrophoneCapture capture = _capture!;
        _capture = null;
        _recordingTimer?.Stop();

        capture.LevelChanged -= OnLevel;
        float[] samples = await capture.StopAsync().ConfigureAwait(false);
        capture.Dispose();

        var duration = TimeSpan.FromSeconds(samples.Length / (double)MicrophoneCapture.TargetSampleRate);
        if (duration < TimeSpan.FromSeconds(0.5))
        {
            SetState(DictationState.Idle);
            Status?.Invoke("Слишком коротко — нечего распознавать");
            ScheduleUnload();
            return;
        }

        SetState(DictationState.Transcribing);

        using var cancellation = new CancellationTokenSource();
        _transcribeCancellation = cancellation;
        try
        {
            var progress = new Progress<int>(p => ProgressChanged?.Invoke(p));
            IReadOnlyList<TranscriptSegment> segments = await _engine
                .TranscribeAsync(samples, progress, cancellationToken: cancellation.Token)
                .ConfigureAwait(false);

            Publish(segments);
        }
        catch (OperationCanceledException)
        {
            Status?.Invoke("Распознавание отменено");
        }
        finally
        {
            _transcribeCancellation = null;
            SetState(DictationState.Idle);
            ScheduleUnload();
        }
    }

    private void Publish(IReadOnlyList<TranscriptSegment> segments)
    {
        AppSettings settings = _settings();
        string joined = TextPostProcessor.JoinSegments(segments, settings.EffectiveParagraphPause);
        string text = TextPostProcessor.ApplyReplacements(joined, settings.Replacements);

        if (text.Length == 0 || TextPostProcessor.IsHallucination(text))
        {
            Status?.Invoke("Пусто — речь не распознана");
            return;
        }

        // Запоминаем ДО вставки: что бы ни случилось дальше, текст можно
        // забрать из меню, а не диктовать заново.
        LastText = text;
        TextReady?.Invoke(text);
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
        if (_capture is { } capture)
        {
            _capture = null;
            capture.LevelChanged -= OnLevel;
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
        var delay = TimeSpan.FromMinutes(_settings().IdleUnloadMinutes);

        _unloadTimer?.Dispose();
        _unloadTimer = new Timer(
            _ => _ = _engine.UnloadIfIdleAsync(),
            state: null,
            delay,
            Timeout.InfiniteTimeSpan);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transcribeCancellation?.Cancel();

        if (_unloadTimer is { } timer)
        {
            await timer.DisposeAsync().ConfigureAwait(false);
        }

        await ResetToIdleAsync().ConfigureAwait(false);
        _busy.Dispose();
    }
}
