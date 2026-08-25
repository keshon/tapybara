using Whisper.net;
using Whisper.net.LibraryLoader;

namespace TapRecorder.Core.Speech;

/// <summary>
/// Обёртка над whisper.cpp. Модель — «тёплая»: грузится при первом обращении
/// и выгружается после простоя, чтобы в бездействии приложение не держало
/// гигабайт памяти.
/// </summary>
/// <remarks>
/// <para>
/// Потокобезопасна. Загрузка и распознавание сериализуются одним семафором:
/// <see cref="SemaphoreSlim"/>, а не <c>lock</c>, потому что внутри есть
/// <c>await</c> — обычный <c>lock</c> удерживать через await нельзя.
/// </para>
/// <para>
/// Настройки распознавания повторяют то, что выстрадано в оригинале CallTap:
/// greedy-семплирование и, главное, <c>WithNoContext</c>.
/// </para>
/// </remarks>
public sealed class WhisperEngine(WhisperEngineOptions options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperFactory? _factory;
    private DateTimeOffset _lastUsedAt = DateTimeOffset.MinValue;
    private bool _disposed;
    private bool _warmedUp;

    /// <summary>Загружена ли модель в память прямо сейчас.</summary>
    public bool IsLoaded => Volatile.Read(ref _factory) is not null;

    /// <summary>
    /// Какая нативная библиотека реально загрузилась: Cuda, Vulkan, Cpu…
    /// </summary>
    /// <remarks>
    /// Заполняется только ПОСЛЕ первой загрузки модели. Не путать с
    /// <see cref="WhisperFactory.GetRuntimeInfo"/>: та печатает флаги набора
    /// инструкций процессора и про GPU-бэкенд не говорит ничего — из-за чего
    /// легко поверить, что работает GPU, когда на самом деле считает CPU.
    /// </remarks>
    public static string LoadedRuntime =>
        RuntimeOptions.LoadedLibrary?.ToString() ?? "ещё не загружен";

    /// <summary>
    /// Прогреть движок заранее. Вызывается параллельно с записью: пока
    /// пользователь говорит, модель уже читается с диска — на распознавании
    /// экономится холодный старт.
    /// </summary>
    /// <param name="warmUpGpu">Прогнать через движок короткий синтетический сигнал.</param>
    /// <param name="cancellationToken">Отмена прогрева.</param>
    /// <remarks>
    /// Прогрев GPU — не перестраховка, а замеренная необходимость. Vulkan
    /// компилирует вычислительные шейдеры при первом использовании: на RTX 3060
    /// первый прогон стоил 8–12 секунд против 0,55 с у последующих. Без
    /// прогрева первая же диктовка после установки выглядела бы зависанием.
    /// Шейдеры кэширует драйвер, но у f16 и q8_0 ядра разные — каждая
    /// квантизация греется отдельно.
    /// </remarks>
    public async Task LoadAsync(bool warmUpGpu = true, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureLoaded();

            if (warmUpGpu && !_warmedUp)
            {
                await TranscribeLockedAsync(WarmUpSignal(), progress: null, language: null, cancellationToken)
                    .ConfigureAwait(false);
                _warmedUp = true;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Распознать записанное. Сэмплы — 16 кГц моно float32.</summary>
    /// <param name="samples">Звук в формате <see cref="Audio.MicrophoneCapture"/>.</param>
    /// <param name="progress">Прогресс 0–100. Приходит из рабочего потока whisper.</param>
    /// <param name="cancellationToken">Отмена. whisper.cpp прерывается между окнами, не мгновенно.</param>
    /// <param name="language">
    /// Переопределить язык на этот прогон. Нужно для звонков: свой язык
    /// известен, а язык собеседника — нет.
    /// </param>
    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        IProgress<int>? progress = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (samples.IsEmpty)
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TranscribeLockedAsync(samples, progress, language, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Выгрузить модель, если ей действительно давно не пользовались.
    /// </summary>
    /// <remarks>
    /// Проверка времени внутри, а не у вызывающего таймера, намеренно:
    /// «осиротевший» таймер, поставленный до предыдущей диктовки, иначе
    /// выгрузил бы модель прямо под свежим распознаванием.
    /// </remarks>
    public async Task UnloadIfIdleAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_factory is not null && DateTimeOffset.UtcNow - _lastUsedAt >= options.IdleUnloadAfter)
            {
                _factory.Dispose();
                _factory = null;

                // Шейдеры остаются в кэше драйвера, но контекст whisper
                // пересоздаётся — признак прогрева сбрасываем честно.
                _warmedUp = false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Распознавание под уже взятым семафором.</summary>
    private async Task<IReadOnlyList<TranscriptSegment>> TranscribeLockedAsync(
        ReadOnlyMemory<float> samples,
        IProgress<int>? progress,
        string? language,
        CancellationToken cancellationToken)
    {
        WhisperFactory factory = EnsureLoaded();

        WhisperProcessorBuilder builder = factory.CreateBuilder()
            .WithLanguage(language ?? options.Language)
            .WithGreedySamplingStrategy()
            .WithThreads(options.EffectiveThreads)
            // Ключевая строка. По умолчанию whisper передаёт текст предыдущего
            // 30-секундного окна как контекст следующего. На длинной записи с
            // паузами это срывает модель в циклы повторов и включает
            // temperature fallback — распознавание замедляется кратно.
            // Ни диктовке, ни звонку этот контекст не нужен.
            .WithNoContext();

        if (!string.IsNullOrWhiteSpace(options.Prompt))
        {
            builder = builder.WithPrompt(options.Prompt);
        }

        if (progress is not null)
        {
            // whisper на последних окнах умеет отдать >100 — зажимаем, чтобы
            // прогресс-бар в интерфейсе не уезжал за край.
            builder = builder.WithProgressHandler(p => progress.Report(Math.Clamp(p, 0, 100)));
        }

        await using WhisperProcessor processor = builder.Build();

        var segments = new List<TranscriptSegment>();
        await foreach (SegmentData segment in processor
                           .ProcessAsync(samples, cancellationToken)
                           .ConfigureAwait(false))
        {
            string text = segment.Text.Trim();
            if (text.Length > 0)
            {
                segments.Add(new TranscriptSegment(segment.Start, segment.End, text));
            }
        }

        _lastUsedAt = DateTimeOffset.UtcNow;
        return segments;
    }

    /// <summary>Загрузка модели под уже взятым семафором.</summary>
    private WhisperFactory EnsureLoaded()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _lastUsedAt = DateTimeOffset.UtcNow;
        if (_factory is { } loaded)
        {
            return loaded;
        }

        if (!File.Exists(options.ModelPath))
        {
            throw new FileNotFoundException("Модель распознавания не найдена", options.ModelPath);
        }

        var factory = WhisperFactory.FromPath(options.ModelPath, new WhisperFactoryOptions
        {
            UseGpu = options.UseGpu,
            UseFlashAttention = options.UseFlashAttention,
        });

        _factory = factory;
        return factory;
    }

    /// <summary>
    /// Секунда тихого тона для прогрева. Тон, а не тишина: на тишине whisper
    /// коротит на «речи нет» и до шейдеров декодера дело не доходит —
    /// прогрелась бы только половина пайплайна.
    /// </summary>
    private static ReadOnlyMemory<float> WarmUpSignal()
    {
        const int sampleRate = Audio.MicrophoneCapture.TargetSampleRate;
        float[] signal = new float[sampleRate];
        for (int i = 0; i < signal.Length; i++)
        {
            signal[i] = 0.05f * MathF.Sin(2f * MathF.PI * 440f * i / sampleRate);
        }

        return signal;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _factory?.Dispose();
            _factory = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
