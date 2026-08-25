using Whisper.net;

namespace TapRecorder.Core.Speech;

/// <summary>Участок записи, на котором есть речь.</summary>
public sealed record SpeechRegion(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>Настройки детектора речи.</summary>
public sealed record SpeechDetectorOptions
{
    public required string ModelPath { get; init; }

    /// <summary>
    /// Порог уверенности 0..1.
    /// </summary>
    /// <remarks>
    /// Ниже — детектор считает речью больше, включая шум; выше — обрезает
    /// тихую речь. Значение по умолчанию сдвинуто вниз от середины сознательно:
    /// пропущенное слово потеряно навсегда, а лишний фрагмент тишины стоит лишь
    /// доли секунды работы модели. Цена ошибок здесь несимметрична.
    /// </remarks>
    public float Threshold { get; init; } = 0.35f;

    /// <summary>Короче этого фрагменты речью не считаются.</summary>
    public TimeSpan MinSpeech { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Пауза короче этой не разрывает участок речи.</summary>
    public TimeSpan MinSilence { get; init; } = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Предельная длина одного участка.
    /// </summary>
    /// <remarks>
    /// Whisper обрабатывает окно в 30 секунд. Участок длиннее пришлось бы
    /// резать самим, причём вслепую — посреди слова. Пусть режет детектор,
    /// он хотя бы знает, где пауза.
    /// </remarks>
    public TimeSpan MaxSpeech { get; init; } = TimeSpan.FromSeconds(25);

    /// <summary>
    /// Запас тишины по краям участка.
    /// </summary>
    /// <remarks>
    /// Детектор срабатывает по энергии и обрезает тихие начала слов —
    /// взрывные согласные, глухие окончания. Без запаса распознавание теряет
    /// первый слог фразы.
    /// </remarks>
    public TimeSpan Padding { get; init; } = TimeSpan.FromMilliseconds(200);
}

/// <summary>
/// Поиск участков речи через Silero VAD.
/// </summary>
/// <remarks>
/// <para>
/// Нужен не ради экономии, а ради правильных таймингов. Whisper предсказывает
///тайминги вместе с текстом, и на записи, где почти всё — тишина, эти предсказания
/// разъезжаются: реплика, прозвучавшая на пятой секунде, получает отметку
/// «ноль». Для звонка это фатально — дальний канал молчит всё время, пока
/// говоришь ты, и хронология перемешивается.
/// </para>
/// <para>
/// С детектором каждый участок распознаётся отдельно и получает ФАКТИЧЕСКОЕ
/// смещение — то, которое измерено, а не предсказано. Побочно исчезают
/// галлюцинации на тишине (модель её просто не видит) и падает время работы.
/// </para>
/// </remarks>
public sealed class SpeechDetector(SpeechDetectorOptions options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperVadFactory? _factory;
    private bool _disposed;

    /// <summary>Найти участки речи. Сэмплы — 16 кГц моно float32.</summary>
    public async Task<IReadOnlyList<SpeechRegion>> DetectAsync(
        ReadOnlyMemory<float> samples,
        CancellationToken cancellationToken = default)
    {
        if (samples.IsEmpty)
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _factory ??= WhisperVadFactory.FromPath(options.ModelPath);

            await using WhisperVadProcessor processor = _factory.CreateBuilder()
                .WithThreshold(options.Threshold)
                .WithMinSpeechDuration(options.MinSpeech)
                .WithMinSilenceDuration(options.MinSilence)
                .WithMaxSpeechDuration(options.MaxSpeech)
                .WithSpeechPadding(options.Padding)
                .Build();

            var detected = await processor
                .DetectSpeechAsync(samples, cancellationToken)
                .ConfigureAwait(false);

            return [.. detected.Select(s => new SpeechRegion(s.Start, s.End))];
        }
        finally
        {
            _gate.Release();
        }
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
