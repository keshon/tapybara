namespace TapRecorder.App.Localization;

/// <summary>
/// Все надписи интерфейса, по одному набору на язык.
/// </summary>
/// <remarks>
/// <para>
/// Не словарь «ключ → строка» и не .resx, а запись с <c>required</c>-свойствами.
/// Причина одна, но весомая: <c>required</c> заставляет компилятор проверить,
/// что КАЖДЫЙ язык определяет КАЖДУЮ строку. Забыть перевести надпись
/// невозможно — проект просто не соберётся. Словарь такой гарантии не даёт:
/// пропущенный ключ обнаруживается в рантайме, у пользователя.
/// </para>
/// <para>
/// Когда переводов станет больше двух и появятся сторонние переводчики,
/// осмысленно будет перейти на .resx с сателлитными сборками. Пока это
/// добавило бы церемонию без пользы.
/// </para>
/// </remarks>
public sealed record UiStrings
{
    // --- трей ---
    public required string TrayStart { get; init; }
    public required string TrayStop { get; init; }
    public required string TrayTranscribing { get; init; }
    public required string TrayLoadingModel { get; init; }
    public required string TrayCopyLast { get; init; }
    public required string TrayModel { get; init; }
    public required string TrayNoModels { get; init; }
    public required string TrayAutoPaste { get; init; }
    public required string TrayAutoStart { get; init; }
    public required string TrayRetryHotkey { get; init; }
    public required string TrayModelsFolder { get; init; }
    public required string TraySettings { get; init; }
    public required string TrayExit { get; init; }

    // --- состояния и уведомления ---
    public required string StatusReady { get; init; }
    public required string StatusLoadingModel { get; init; }
    public required string StatusModelMissing { get; init; }
    public required string StatusModelLoadFailed { get; init; }
    public required string StatusHotkeyBusy { get; init; }
    public required string StatusModelStillLoading { get; init; }
    public required string StatusInClipboard { get; init; }
    public required string NotifyNoModelTitle { get; init; }
    public required string NotifyNoModelBody { get; init; }
    public required string NotifyHotkeyBusyTitle { get; init; }
    public required string NotifyHotkeyBusyHint { get; init; }

    // --- пилюля-индикатор ---
    public required string PillTranscribing { get; init; }
    public required string PillInserted { get; init; }
    public required string PillInsertedViaClipboard { get; init; }
    public required string PillClipboardOnly { get; init; }
    public required string PillDone { get; init; }
    public required string PillCancelled { get; init; }

    // --- окно настроек ---
    public required string SettingsTitle { get; init; }
    public required string SectionRecognition { get; init; }
    public required string SectionInput { get; init; }
    public required string SectionText { get; init; }
    public required string SectionStorage { get; init; }
    public required string SectionInterface { get; init; }
    public required string SectionAbout { get; init; }

    public required string FieldModel { get; init; }
    public required string FieldRecognitionLanguage { get; init; }
    public required string FieldRecognitionLanguageHint { get; init; }
    public required string FieldPrompt { get; init; }
    public required string FieldPromptHint { get; init; }
    public required string FieldHotkey { get; init; }
    public required string FieldHotkeyHint { get; init; }
    public required string FieldHotkeyCapturing { get; init; }
    public required string FieldAutoPaste { get; init; }
    public required string FieldClipboardHistory { get; init; }
    public required string FieldSplitParagraphs { get; init; }
    public required string FieldParagraphPause { get; init; }
    public required string FieldReplacements { get; init; }
    public required string FieldReplacementsHint { get; init; }
    public required string FieldUiLanguage { get; init; }
    public required string FieldUiLanguageAuto { get; init; }
    public required string FieldModelsFolder { get; init; }
    public required string FieldPortable { get; init; }
    public required string FieldPortableHint { get; init; }
    public required string FieldIdleUnload { get; init; }
    public required string FieldUseVad { get; init; }
    public required string FieldUseVadHint { get; init; }
    public required string FieldVadModel { get; init; }
    public required string FieldVadThreshold { get; init; }
    public required string FieldVadThresholdHint { get; init; }

    public required string ButtonChange { get; init; }
    public required string ButtonReset { get; init; }
    public required string ButtonBrowse { get; init; }
    public required string ButtonOpen { get; init; }
    public required string ButtonClose { get; init; }
    public required string ButtonDefault { get; init; }
    public required string RestartRequired { get; init; }
    public required string LanguageEnglish { get; init; }
    public required string LanguageRussian { get; init; }
    public required string Minutes { get; init; }
    public required string Seconds { get; init; }
    public required string FooterStoragePath { get; init; }
    public required string AboutTagline { get; init; }
    public required string AboutVersion { get; init; }
    public required string AboutAuthors { get; init; }
    public required string AboutAuthorsValue { get; init; }
    public required string AboutRuntime { get; init; }
    public required string AboutRuntimeHint { get; init; }
    public required string AboutLicense { get; init; }
    public required string AboutComponents { get; init; }
    public required string ButtonCopyDiagnostics { get; init; }
    public required string DiagnosticsCopied { get; init; }

    public static UiStrings English { get; } = new()
    {
        TrayStart = "Start dictation",
        TrayStop = "Stop dictation",
        TrayTranscribing = "Transcribing…",
        TrayLoadingModel = "Loading model…",
        TrayCopyLast = "Copy last text",
        TrayModel = "Model",
        TrayNoModels = "No models found",
        TrayAutoPaste = "Insert automatically",
        TrayAutoStart = "Start with Windows",
        TrayRetryHotkey = "Claim the hotkey again",
        TrayModelsFolder = "Models folder…",
        TraySettings = "Settings…",
        TrayExit = "Exit",

        StatusReady = "Ready",
        StatusLoadingModel = "Loading model…",
        StatusModelMissing = "No model found",
        StatusModelLoadFailed = "Model failed to load: {0}",
        StatusHotkeyBusy = "{0} is taken by another application",
        StatusModelStillLoading = "The model is still loading — one moment",
        StatusInClipboard = "In clipboard: {0}",
        NotifyNoModelTitle = "No recognition model",
        NotifyNoModelBody = "Put a ggml model in {0} and pick it from the menu.",
        NotifyHotkeyBusyTitle = "Hotkey unavailable",
        NotifyHotkeyBusyHint = "Free it, then choose \"Claim the hotkey again\" from the menu.",

        PillTranscribing = "Transcribing… {0}%",
        PillInserted = "✓ Inserted",
        PillInsertedViaClipboard = "✓ Inserted via clipboard",
        PillClipboardOnly = "In clipboard — Ctrl+V",
        PillDone = "Done",
        PillCancelled = "Cancelled",

        SettingsTitle = "TapRecorder settings",
        SectionRecognition = "Recognition",
        SectionInput = "Input",
        SectionText = "Text",
        SectionStorage = "Storage",
        SectionInterface = "Interface",
        SectionAbout = "About",

        FieldModel = "Model",
        FieldRecognitionLanguage = "Recognition language",
        FieldRecognitionLanguageHint = "Two-letter code, or auto to detect",
        FieldPrompt = "Prompt",
        FieldPromptHint = "Biases vocabulary and anchors punctuation style. Keep it short and typical of what you dictate — an unrelated prompt makes recognition worse and slower.",
        FieldHotkey = "Hotkey",
        FieldHotkeyHint = "Escape cancels a dictation in progress",
        FieldHotkeyCapturing = "Press a combination…",
        FieldAutoPaste = "Insert text automatically",
        FieldClipboardHistory = "Keep dictations out of clipboard history (Win+V)",
        FieldSplitParagraphs = "Split into paragraphs on pauses",
        FieldParagraphPause = "Pause length",
        FieldReplacements = "Replacements",
        FieldReplacementsHint = "One per line, as: heard = correct",
        FieldUiLanguage = "Interface language",
        FieldUiLanguageAuto = "Same as Windows",
        FieldModelsFolder = "Models folder",
        FieldPortable = "Portable mode",
        FieldPortableHint = "Keep settings and models next to the program instead of in AppData. Existing files are not moved.",
        FieldIdleUnload = "Unload the model after",
        FieldUseVad = "Detect speech before transcribing",
        FieldUseVadHint = "Used for call recordings. Finds where speech actually is, so a line gets the position it was measured at instead of one the model guessed. Also removes silence hallucinations and speeds transcription up.",
        FieldVadModel = "Speech detector model",
        FieldVadThreshold = "Detector sensitivity",
        FieldVadThresholdHint = "Lower catches quieter speech but also more noise",

        ButtonChange = "Change",
        ButtonReset = "Reset",
        ButtonBrowse = "Browse…",
        ButtonOpen = "Open",
        ButtonClose = "Close",
        ButtonDefault = "Default",
        RestartRequired = "Takes effect after a restart",
        LanguageEnglish = "English",
        LanguageRussian = "Russian",
        Minutes = "min",
        Seconds = "s",
        FooterStoragePath = "Settings and models are stored in {0}",
        AboutTagline = "Local voice dictation. Everything runs on this machine — no audio and no text ever leave it.",
        AboutVersion = "Version",
        AboutAuthors = "Made by",
        AboutAuthorsValue = "Señor Mega and his best bud, Claude",
        AboutRuntime = "Compute backend",
        AboutRuntimeHint = "Which native library whisper.cpp actually loaded",
        AboutLicense = "License",
        AboutComponents = "Built on whisper.cpp, Whisper.net, NAudio and WPF-UI.",
        ButtonCopyDiagnostics = "Copy diagnostics",
        DiagnosticsCopied = "Diagnostics copied to the clipboard",
    };

    public static UiStrings Russian { get; } = new()
    {
        TrayStart = "Начать диктовку",
        TrayStop = "Закончить диктовку",
        TrayTranscribing = "Распознаю…",
        TrayLoadingModel = "Загружаю модель…",
        TrayCopyLast = "Скопировать последний текст",
        TrayModel = "Модель",
        TrayNoModels = "Моделей не найдено",
        TrayAutoPaste = "Вставлять автоматически",
        TrayAutoStart = "Запускать при входе в систему",
        TrayRetryHotkey = "Занять горячую клавишу заново",
        TrayModelsFolder = "Папка моделей…",
        TraySettings = "Настройки…",
        TrayExit = "Выход",

        StatusReady = "Готов",
        StatusLoadingModel = "Загружаю модель…",
        StatusModelMissing = "Модель не найдена",
        StatusModelLoadFailed = "Модель не загрузилась: {0}",
        StatusHotkeyBusy = "{0} занят другим приложением",
        StatusModelStillLoading = "Модель ещё загружается — подожди немного",
        StatusInClipboard = "В буфере: {0}",
        NotifyNoModelTitle = "Нет модели распознавания",
        NotifyNoModelBody = "Положи ggml-модель в {0} и выбери её в меню.",
        NotifyHotkeyBusyTitle = "Горячая клавиша занята",
        NotifyHotkeyBusyHint = "Освободи её и выбери в меню «Занять горячую клавишу заново».",

        PillTranscribing = "Распознаю… {0}%",
        PillInserted = "✓ Вставлено",
        PillInsertedViaClipboard = "✓ Вставлено через буфер",
        PillClipboardOnly = "В буфере — Ctrl+V",
        PillDone = "Готово",
        PillCancelled = "Отменено",

        SettingsTitle = "Настройки TapRecorder",
        SectionRecognition = "Распознавание",
        SectionInput = "Ввод",
        SectionText = "Текст",
        SectionStorage = "Хранение",
        SectionInterface = "Интерфейс",
        SectionAbout = "О программе",

        FieldModel = "Модель",
        FieldRecognitionLanguage = "Язык распознавания",
        FieldRecognitionLanguageHint = "Двухбуквенный код или auto для определения",
        FieldPrompt = "Промпт",
        FieldPromptHint = "Подсказывает термины и задаёт стиль пунктуации. Держи его коротким и похожим на то, что диктуешь: промпт не по теме ухудшает и замедляет распознавание.",
        FieldHotkey = "Горячая клавиша",
        FieldHotkeyHint = "Escape отменяет начатую диктовку",
        FieldHotkeyCapturing = "Нажми сочетание…",
        FieldAutoPaste = "Вставлять текст автоматически",
        FieldClipboardHistory = "Не сохранять диктовки в истории буфера (Win+V)",
        FieldSplitParagraphs = "Разбивать на абзацы по паузам",
        FieldParagraphPause = "Длина паузы",
        FieldReplacements = "Замены",
        FieldReplacementsHint = "По одной в строке, в виде: услышано = правильно",
        FieldUiLanguage = "Язык интерфейса",
        FieldUiLanguageAuto = "Как в Windows",
        FieldModelsFolder = "Папка моделей",
        FieldPortable = "Портативный режим",
        FieldPortableHint = "Хранить настройки и модели рядом с программой, а не в AppData. Уже имеющиеся файлы не переносятся.",
        FieldIdleUnload = "Выгружать модель через",
        FieldUseVad = "Искать речь перед распознаванием",
        FieldUseVadHint = "Используется для записей звонков. Находит, где речь есть на самом деле, и реплика получает измеренное положение вместо предсказанного моделью. Заодно убирает галлюцинации на тишине и ускоряет распознавание.",
        FieldVadModel = "Модель детектора речи",
        FieldVadThreshold = "Чувствительность детектора",
        FieldVadThresholdHint = "Ниже — ловит более тихую речь, но и больше шума",

        ButtonChange = "Изменить",
        ButtonReset = "Сбросить",
        ButtonBrowse = "Выбрать…",
        ButtonOpen = "Открыть",
        ButtonClose = "Закрыть",
        ButtonDefault = "По умолчанию",
        RestartRequired = "Вступит в силу после перезапуска",
        LanguageEnglish = "Английский",
        LanguageRussian = "Русский",
        Minutes = "мин",
        Seconds = "с",
        FooterStoragePath = "Настройки и модели хранятся в {0}",
        AboutTagline = "Локальная голосовая диктовка. Всё считается на этой машине — ни звук, ни текст никуда не уходят.",
        AboutVersion = "Версия",
        AboutAuthors = "Авторы",
        AboutAuthorsValue = "Señor Mega and his best bud, Claude",
        AboutRuntime = "Вычислительный бэкенд",
        AboutRuntimeHint = "Какая нативная библиотека whisper.cpp реально загрузилась",
        AboutLicense = "Лицензия",
        AboutComponents = "Работает на whisper.cpp, Whisper.net, NAudio и WPF-UI.",
        ButtonCopyDiagnostics = "Скопировать диагностику",
        DiagnosticsCopied = "Диагностика скопирована в буфер обмена",
    };
}

/// <summary>Действующий набор надписей. Короткое имя намеренно — он повсюду.</summary>
public static class L
{
    /// <summary>Текущий язык интерфейса.</summary>
    public static UiStrings S { get; private set; } = UiStrings.English;

    /// <summary>Переключить язык. <c>null</c> — по системной локали.</summary>
    public static void Use(string? language)
    {
        string resolved = language ?? Core.Settings.LanguageDefaults.DetectUiLanguage();
        S = resolved.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? UiStrings.Russian
            : UiStrings.English;
    }
}
