using System.Text.Json.Serialization;
using Tapybara.Core.Windows;

namespace Tapybara.Core.Settings;

/// <summary>Какую тему интерфейса показывать.</summary>
public enum AppTheme
{
    /// <summary>Как в Windows.</summary>
    System,
    Light,
    Dark,
}

/// <summary>Настройки приложения. Сохраняются в JSON, правятся из окна настроек.</summary>
public sealed record AppSettings
{
    /// <summary>Имя файла модели в папке моделей.</summary>
    /// <remarks>
    /// Хранится имя, а не полный путь: папка моделей может переехать вместе с
    /// приложением, а привязка к абсолютному пути это ломает.
    /// </remarks>
    public string ModelFileName { get; init; } = "ggml-large-v3-turbo-q5_0.bin";

    /// <summary>
    /// Язык распознавания. Не ограничен двумя — whisper знает 99.
    /// </summary>
    /// <remarks>
    /// По умолчанию <c>auto</c>, а не язык автора. Навязанный не тому человеку
    /// язык не «слегка ухудшает» распознавание, а превращает речь в
    /// бессмыслицу, и выглядит это как сломанная модель, а не как неверная
    /// настройка.
    /// </remarks>
    public string Language { get; init; } = "auto";

    /// <summary>
    /// Язык интерфейса: <c>"en"</c>, <c>"ru"</c> или <c>null</c> — по системной локали.
    /// </summary>
    public string? UiLanguage { get; init; }

    /// <summary>Тема интерфейса.</summary>
    public AppTheme Theme { get; init; } = AppTheme.System;

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
    /// Устройство записи для диктовки и своего канала звонка.
    /// <c>null</c> — устройство по умолчанию.
    /// </summary>
    /// <remarks>
    /// Хранится идентификатор устройства, а не имя: имена повторяются («Микрофон»
    /// на трёх разных картах), а идентификатор уникален и переживает
    /// переподключение.
    /// </remarks>
    public string? MicrophoneDeviceId { get; init; }

    /// <summary>
    /// Устройство вывода, с которого пишется чужой канал звонка.
    /// <c>null</c> — устройство по умолчанию.
    /// </summary>
    /// <remarks>
    /// Отдельная настройка, потому что типичная схема — гарнитура для звонка
    /// при колонках как устройстве по умолчанию. Записывая «то, что по
    /// умолчанию», мы записали бы тишину.
    /// </remarks>
    public string? SystemAudioDeviceId { get; init; }

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
    /// <remarks>
    /// <c>JsonIgnore</c> обязателен. Без него вычисляемое свойство уезжает в
    /// settings.json как настоящая настройка: файл читает и правит человек, а
    /// поле, которое ничего не задаёт и молча игнорируется при чтении, —
    /// прямое приглашение потратить вечер на выяснение, почему его правка ни
    /// на что не влияет.
    /// </remarks>
    [JsonIgnore]
    public TimeSpan EffectiveParagraphPause => SplitParagraphsByPauses
        ? TimeSpan.FromSeconds(Math.Max(0.2, ParagraphPauseSeconds))
        : TimeSpan.Zero;

    /// <summary>
    /// Искать речь детектором перед распознаванием.
    /// </summary>
    /// <remarks>
    /// Работает и в диктовке, и в звонках. Главное, что это даёт, — правильные
    /// тайминги: положение реплики измеряется, а не предсказывается моделью
    /// вместе с текстом. Побочно исчезают галлюцинации на тишине и падает
    /// время работы.
    /// </remarks>
    public bool UseVoiceActivityDetection { get; init; } = true;

    /// <summary>Имя файла модели детектора речи в папке моделей.</summary>
    public string VadModelFileName { get; init; } = "ggml-silero-v6.2.0.bin";

    /// <summary>Порог уверенности детектора речи, 0..1.</summary>
    public double VadThreshold { get; init; } = 0.35;

    /// <summary>
    /// Приводить громкость записи к рабочему уровню перед распознаванием.
    /// </summary>
    /// <remarks>
    /// Микрофон, отодвинутый от лица, даёт запись в разы тише нормальной, и
    /// детектор речи такую просто не слышит.
    /// </remarks>
    public bool NormalizeAudio { get; init; } = true;

    /// <summary>Куда складывать записи звонков. <c>null</c> — папка по умолчанию.</summary>
    public string? CallsDirectory { get; init; }

    /// <summary>Как подписывать свои реплики в транскрипте звонка.</summary>
    public string MyName { get; init; } = "Me";

    /// <summary>
    /// Как подписывать реплики из системного канала, когда участники не указаны.
    /// </summary>
    public string OtherSideName { get; init; } = "Them";

    /// <summary>
    /// Язык собеседников в звонке.
    /// </summary>
    /// <remarks>
    /// По умолчанию <c>auto</c>, и это не лень. Свой язык известен заранее,
    /// а язык собеседника — нет. Навязанный не тому каналу язык не «слегка
    /// ухудшает» распознавание, а превращает речь в бессмыслицу: английское
    /// «permit denied», распознанное с принудительным русским, дало
    /// «Хермит отказал».
    /// </remarks>
    public string OtherSideLanguage { get; init; } = "auto";

    /// <summary>
    /// Пользователь видел предупреждение о записи звонков.
    /// </summary>
    /// <remarks>
    /// Запись разговора пишет и собеседника тоже, а согласие на это во многих
    /// юрисдикциях обязательно. Показать это один раз — минимум, который
    /// приложение обязано сделать.
    /// </remarks>
    public bool CallRecordingAcknowledged { get; init; }

    /// <summary>Вставлять текст автоматически или только класть в буфер.</summary>
    public bool AutoPaste { get; init; } = true;

    /// <summary>Прятать вставляемый текст из истории буфера обмена (Win+V).</summary>
    public bool ExcludeFromClipboardHistory { get; init; } = true;

    /// <summary>Показывать плавающий индикатор диктовки.</summary>
    public bool ShowOverlay { get; init; } = true;

    /// <summary>
    /// Куда пользователь перетащил индикатор, в логических единицах.
    /// <c>null</c> — сверху по центру активного экрана.
    /// </summary>
    public double? OverlayLeft { get; init; }

    /// <summary>См. <see cref="OverlayLeft"/>.</summary>
    public double? OverlayTop { get; init; }

    /// <summary>
    /// Показывать начало распознанного текста в подсказке трея.
    /// </summary>
    /// <remarks>
    /// По умолчанию выключено. Это единственное место, где надиктованное
    /// остаётся на экране надолго, а диктуют в том числе пароли и переписку.
    /// </remarks>
    public bool ShowTextPreviewInTray { get; init; }

    /// <summary>Через сколько минут простоя выгружать модель из памяти.</summary>
    public int IdleUnloadMinutes { get; init; } = 10;

    /// <summary>
    /// Предохранитель от забытой диктовки: запись обрывается через столько минут.
    /// </summary>
    public int MaxDictationMinutes { get; init; } = 15;

    /// <summary>
    /// Предохранитель от забытой записи звонка, в минутах.
    /// </summary>
    /// <remarks>
    /// Два канала стоят около 230 МБ в час. Запись, забытая на ночь, забивает
    /// диск, а обнаруживается это уже как отказ записи посреди следующего
    /// разговора.
    /// </remarks>
    public int MaxCallMinutes { get; init; } = 240;

    /// <summary>
    /// Замены в распознанном тексте: «как услышала модель» → «как надо».
    /// Сюда идут названия продуктов и жаргон, которые ASR стабильно коверкает.
    /// </summary>
    public Dictionary<string, string> Replacements { get; init; } = [];
}
