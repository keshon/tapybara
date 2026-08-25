namespace Tapybara.Core.Speech;

/// <summary>Настройки движка распознавания.</summary>
/// <remarks>
/// <c>record</c> с <c>init</c>-свойствами: объект собирается один раз при
/// создании и дальше неизменяем. Для настроек это то, что надо, — никто не
/// поменяет язык у движка из-под работающего распознавания.
/// </remarks>
public sealed record WhisperEngineOptions
{
    /// <summary>Путь к ggml-модели. <c>required</c> — без него объект не создать.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Язык распознавания. <c>"auto"</c> — определять по звуку.</summary>
    public string Language { get; init; } = "ru";

    /// <summary>
    /// Подсказка словаря: биасит распознавание в сторону нужных терминов.
    /// Не команда для модели, а «затравка» контекста — сюда идут имена
    /// продуктов и жаргон, которые ASR иначе коверкает.
    /// </summary>
    public string? Prompt { get; init; }

    /// <summary>Считать на GPU. Выключать имеет смысл только для сравнения скорости.</summary>
    public bool UseGpu { get; init; } = true;

    /// <summary>
    /// Flash attention — заметно быстрее и экономнее по памяти на длинных
    /// записях. Поддерживается CUDA-сборкой; на неподдерживающем бэкенде
    /// whisper.cpp тихо откатывается на обычное внимание.
    /// </summary>
    public bool UseFlashAttention { get; init; } = true;

    /// <summary>
    /// Число потоков CPU. <c>null</c> — оставить процессору пару ядер
    /// на всё остальное, чтобы система не задыхалась во время распознавания.
    /// </summary>
    public int? Threads { get; init; }

    /// <summary>Через сколько простоя выгружать модель из памяти.</summary>
    public TimeSpan IdleUnloadAfter { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Фактическое число потоков с учётом <c>null</c>-значения по умолчанию.</summary>
    public int EffectiveThreads => Threads ?? Math.Max(2, Environment.ProcessorCount - 2);
}
