using TapRecorder.Core.Windows;

namespace TapRecorder.Core.Settings;

/// <summary>Настройки приложения. Сохраняются в JSON, правятся из окна настроек.</summary>
public sealed record AppSettings
{
    /// <summary>Имя файла модели в папке моделей.</summary>
    /// <remarks>
    /// Хранится имя, а не полный путь: папка моделей может переехать вместе с
    /// приложением, а привязка к абсолютному пути это ломает.
    /// </remarks>
    public string ModelFileName { get; init; } = "ggml-podlodka-turbo-q8_0.bin";

    /// <summary>Язык распознавания. Не ограничен двумя — whisper знает 99.</summary>
    public string Language { get; init; } = "ru";

    /// <summary>
    /// Язык интерфейса: <c>"en"</c>, <c>"ru"</c> или <c>null</c> — по системной локали.
    /// </summary>
    public string? UiLanguage { get; init; }

    /// <summary>
    /// Папка с моделями, заданная пользователем. <c>null</c> — папка по умолчанию
    /// внутри данных приложения.
    /// </summary>
    /// <remarks>
    /// Модели весят гигабайты, и держать их на системном диске хочется не всем.
    /// </remarks>
    public string? ModelsDirectory { get; init; }

    /// <summary>
    /// Подсказка словаря. Влияет не только на термины, но и на стиль:
    /// модель подражает пунктуации и оформлению промпта.
    /// </summary>
    public string? Prompt { get; init; }

    /// <summary>Сочетание клавиш для старта и остановки диктовки.</summary>
    public HotkeyCombo Hotkey { get; init; } = HotkeyCombo.Default;

    /// <summary>
    /// Разбивать текст на абзацы по паузам в речи.
    /// </summary>
    /// <remarks>
    /// Whisper абзацы не размечает, но у его сегментов есть тайминги, и пауза
    /// хорошо соответствует смене мысли. Точность таймингов у разных моделей
    /// разная, поэтому это переключатель, а не поведение по умолчанию без права
    /// отказа.
    /// </remarks>
    public bool SplitParagraphsByPauses { get; init; } = true;

    /// <summary>Пауза в секундах, начиная с которой начинается новый абзац.</summary>
    public double ParagraphPauseSeconds { get; init; } = 1.5;

    /// <summary>Пауза для разбивки с учётом переключателя.</summary>
    public TimeSpan EffectiveParagraphPause => SplitParagraphsByPauses
        ? TimeSpan.FromSeconds(Math.Max(0.2, ParagraphPauseSeconds))
        : TimeSpan.Zero;

    /// <summary>Вставлять текст автоматически или только класть в буфер.</summary>
    public bool AutoPaste { get; init; } = true;

    /// <summary>Прятать вставляемый текст из истории буфера обмена (Win+V).</summary>
    public bool ExcludeFromClipboardHistory { get; init; } = true;

    /// <summary>Через сколько минут простоя выгружать модель из памяти.</summary>
    public int IdleUnloadMinutes { get; init; } = 10;

    /// <summary>
    /// Предохранитель от забытой диктовки: запись обрывается через столько минут.
    /// </summary>
    public int MaxDictationMinutes { get; init; } = 15;

    /// <summary>
    /// Замены в распознанном тексте: «как услышала модель» → «как надо».
    /// Сюда идут названия продуктов и жаргон, которые ASR стабильно коверкает.
    /// </summary>
    public Dictionary<string, string> Replacements { get; init; } = [];
}
