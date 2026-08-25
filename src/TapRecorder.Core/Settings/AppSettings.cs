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

    /// <summary>Язык распознавания.</summary>
    public string Language { get; init; } = "ru";

    /// <summary>
    /// Подсказка словаря. Влияет не только на термины, но и на стиль:
    /// модель подражает пунктуации и оформлению промпта.
    /// </summary>
    public string? Prompt { get; init; }

    /// <summary>Сочетание клавиш для старта и остановки диктовки.</summary>
    public HotkeyCombo Hotkey { get; init; } = HotkeyCombo.Default;

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
