using Tapybara.Core.Calls;
using Tapybara.Core.Dictation;
using Tapybara.Core.Models;

namespace Tapybara.App.Localization;

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
    public required string TrayCancel { get; init; }
    public required string TrayTranscribing { get; init; }
    public required string TrayLoadingModel { get; init; }
    public required string TrayHistory { get; init; }
    public required string TrayHistoryEmpty { get; init; }
    public required string TrayModel { get; init; }
    public required string TrayNoModels { get; init; }
    public required string TrayAutoPaste { get; init; }
    public required string TrayAutoStart { get; init; }
    public required string TrayRetryHotkey { get; init; }
    public required string TrayModelsFolder { get; init; }
    public required string TraySettings { get; init; }
    public required string TrayExit { get; init; }
    public required string TrayStartRecording { get; init; }
    public required string TrayStopRecording { get; init; }
    public required string TrayCallsFolder { get; init; }
    public required string StatusRecordingCall { get; init; }
    public required string StatusTranscribingCall { get; init; }
    public required string StatusCallSaved { get; init; }
    public required string NotifyCallReadyTitle { get; init; }
    public required string NotifyCallStoppedTitle { get; init; }
    public required string CallStoppedDeviceLost { get; init; }
    public required string CallStoppedLengthLimit { get; init; }
    public required string CallStoppedDiskFull { get; init; }

    // --- состояния и уведомления ---
    public required string StatusReady { get; init; }
    public required string StatusLoadingModel { get; init; }
    public required string StatusModelMissing { get; init; }
    public required string StatusModelLoadFailed { get; init; }
    public required string StatusHotkeyBusy { get; init; }
    public required string StatusModelStillLoading { get; init; }
    public required string StatusInClipboard { get; init; }
    public required string StatusCopied { get; init; }
    public required string StatusTextReady { get; init; }
    public required string StatusSettingsNotSaved { get; init; }
    public required string NotifyNoModelTitle { get; init; }
    public required string NotifyNoModelBody { get; init; }
    public required string NotifyHotkeyBusyTitle { get; init; }
    public required string NotifyHotkeyBusyHint { get; init; }
    public required string NotifyAlreadyRunningTitle { get; init; }
    public required string NotifyAlreadyRunningBody { get; init; }
    public required string NotifyCrashTitle { get; init; }
    public required string NotifyCrashBody { get; init; }

    // --- сообщения диктовки ---
    public required string DictationFailed { get; init; }
    public required string DictationTooShort { get; init; }
    public required string DictationCancelled { get; init; }
    public required string DictationEmpty { get; init; }
    public required string DictationLengthLimit { get; init; }
    public required string DictationDeviceLost { get; init; }

    // --- пилюля-индикатор ---
    public required string PillTranscribing { get; init; }
    public required string PillInserted { get; init; }
    public required string PillInsertedViaClipboard { get; init; }
    public required string PillClipboardOnly { get; init; }
    public required string PillDone { get; init; }
    public required string PillCancelled { get; init; }
    public required string PillRecordingCall { get; init; }
    public required string PillHintStop { get; init; }
    public required string PillHintCancel { get; init; }

    // --- окно настроек: разделы ---
    public required string SettingsTitle { get; init; }
    public required string SectionDictation { get; init; }
    public required string SectionRecognition { get; init; }
    public required string SectionModels { get; init; }
    public required string SectionText { get; init; }
    public required string SectionCalls { get; init; }
    public required string SectionGeneral { get; init; }
    public required string SectionAbout { get; init; }

    // --- окно настроек: заголовки групп ---
    public required string GroupHotkey { get; init; }
    public required string GroupAudioInput { get; init; }
    public required string GroupInsertion { get; init; }
    public required string GroupLimits { get; init; }
    public required string GroupModel { get; init; }
    public required string GroupLanguageAndStyle { get; init; }
    public required string GroupSpeechDetection { get; init; }
    public required string GroupModelsFolder { get; init; }
    public required string GroupDownload { get; init; }
    public required string GroupParagraphs { get; init; }
    public required string GroupReplacements { get; init; }
    public required string GroupRecording { get; init; }
    public required string GroupTranscript { get; init; }
    public required string GroupAppearance { get; init; }
    public required string GroupStartupAndStorage { get; init; }
    public required string GroupPrivacy { get; init; }

    // --- окно настроек: поля ---
    public required string FieldModel { get; init; }
    public required string FieldModelHint { get; init; }
    public required string FieldDecoding { get; init; }
    public required string FieldDecodingHint { get; init; }
    public required string DecodingFast { get; init; }
    public required string DecodingAccurate { get; init; }
    public required string DecodingThorough { get; init; }
    public required string FieldRecognitionLanguage { get; init; }
    public required string FieldRecognitionLanguageHint { get; init; }
    public required string FieldPrompt { get; init; }
    public required string FieldPromptHint { get; init; }
    public required string FieldHotkey { get; init; }
    public required string FieldHotkeyHint { get; init; }
    public required string FieldHotkeyCapturing { get; init; }
    public required string FieldHotkeyCaptureHint { get; init; }
    public required string FieldHotkeyTaken { get; init; }
    public required string FieldMicrophone { get; init; }
    public required string FieldMicrophoneHint { get; init; }
    public required string FieldSystemAudioDevice { get; init; }
    public required string FieldSystemAudioDeviceHint { get; init; }
    public required string DeviceSystemDefault { get; init; }
    public required string FieldAutoPaste { get; init; }
    public required string FieldAutoPasteHint { get; init; }
    public required string FieldClipboardHistory { get; init; }
    public required string FieldShowOverlay { get; init; }
    public required string FieldShowOverlayHint { get; init; }
    public required string FieldMaxDictation { get; init; }
    public required string FieldMaxDictationHint { get; init; }
    public required string FieldSplitParagraphs { get; init; }
    public required string FieldParagraphPause { get; init; }
    public required string FieldReplacements { get; init; }
    public required string FieldReplacementsHint { get; init; }
    public required string FieldUiLanguage { get; init; }
    public required string FieldUiLanguageAuto { get; init; }
    public required string FieldTheme { get; init; }
    public required string ThemeSystem { get; init; }
    public required string ThemeLight { get; init; }
    public required string ThemeDark { get; init; }
    public required string FieldModelsFolder { get; init; }
    public required string FieldModelsFolderHint { get; init; }
    public required string FieldPortable { get; init; }
    public required string FieldPortableHint { get; init; }
    public required string FieldIdleUnload { get; init; }
    public required string FieldIdleUnloadHint { get; init; }
    public required string FieldUseVad { get; init; }
    public required string FieldUseVadHint { get; init; }
    public required string FieldVadModel { get; init; }
    public required string FieldVadModelMissing { get; init; }
    public required string FieldVadThreshold { get; init; }
    public required string FieldVadThresholdHint { get; init; }
    public required string FieldNormalize { get; init; }
    public required string FieldNormalizeHint { get; init; }
    public required string FieldCallsFolder { get; init; }
    public required string FieldMaxCall { get; init; }
    public required string FieldMaxCallHint { get; init; }
    public required string FieldMyName { get; init; }
    public required string FieldMyNameHint { get; init; }
    public required string FieldOtherSideName { get; init; }
    public required string FieldOtherSideLanguage { get; init; }
    public required string FieldOtherSideLanguageHint { get; init; }
    public required string FieldAutoStart { get; init; }
    public required string FieldAutoStartHint { get; init; }
    public required string FieldAutoStartUnavailable { get; init; }
    public required string FieldTrayPreview { get; init; }
    public required string FieldTrayPreviewHint { get; init; }

    // --- загрузка моделей ---
    public required string ModelsIntro { get; init; }
    public required string ModelsInstalled { get; init; }
    public required string ModelsNothingInstalled { get; init; }
    public required string ModelsFullListLink { get; init; }
    public required string ModelsRecognitionHeader { get; init; }
    public required string ModelsDetectorHeader { get; init; }
    public required string ModelsDetectorNote { get; init; }
    public required string TierBest { get; init; }
    public required string TierRecommended { get; init; }
    public required string TierCompact { get; init; }
    public required string TierMinimal { get; init; }
    public required string ButtonDownload { get; init; }
    public required string ButtonCancelDownload { get; init; }
    public required string LabelInstalled { get; init; }
    public required string DownloadProgress { get; init; }
    public required string DownloadFailed { get; init; }
    public required string DownloadCancelled { get; init; }
    public required string ModelsMoveTitle { get; init; }
    public required string ModelsMoveQuestion { get; init; }
    public required string ModelsMoveQuestionOne { get; init; }
    public required string ModelsMoving { get; init; }
    public required string ModelsMoveFailed { get; init; }
    public required string ModelsDeleteTitle { get; init; }
    public required string ModelsDeleteQuestion { get; init; }
    public required string ModelsDeleteInUse { get; init; }
    public required string ModelsDeleteFailed { get; init; }
    public required string ModelsTotalSize { get; init; }
    public required string LabelFrom { get; init; }
    public required string LabelTo { get; init; }

    // --- подбор порога детектора ---
    public required string ButtonTest { get; init; }
    public required string ButtonStartTest { get; init; }
    public required string ButtonRepeatTest { get; init; }
    public required string ButtonStopTest { get; init; }
    public required string ButtonApplyRecommended { get; init; }
    public required string VadTestTitle { get; init; }
    public required string VadTestIntro { get; init; }
    public required string VadTestReady { get; init; }
    public required string VadTestSpeakNow { get; init; }
    public required string VadTestAnalyzing { get; init; }
    public required string VadTestResultsHeader { get; init; }
    public required string VadTestRow { get; init; }
    public required string VadTestRecommended { get; init; }
    public required string VadTestNoSpeech { get; init; }
    public required string VadTestDone { get; init; }

    // --- предупреждение о записи звонков ---
    public required string CallConsentTitle { get; init; }
    public required string CallConsentBody { get; init; }
    public required string CallConsentPoints { get; init; }
    public required string CallConsentAccept { get; init; }
    public required string CallConsentCancel { get; init; }
    public required string CallConsentNote { get; init; }

    // --- транскрипт звонка ---
    public required string TranscriptStartedAt { get; init; }
    public required string TranscriptDuration { get; init; }
    public required string TranscriptTrigger { get; init; }
    public required string TranscriptParticipants { get; init; }
    public required string TranscriptBleedRemoved { get; init; }
    public required string TranscriptBleedByText { get; init; }
    public required string TranscriptBleedByEnergy { get; init; }
    public required string TranscriptNothingRecognized { get; init; }
    public required string CallStageReading { get; init; }
    public required string CallStageMicrophone { get; init; }
    public required string CallStageOtherSide { get; init; }
    public required string CallStageFiltering { get; init; }

    // --- общее ---
    public required string ButtonBrowse { get; init; }
    public required string ButtonUseDefault { get; init; }
    public required string ButtonDelete { get; init; }
    public required string ButtonMove { get; init; }
    public required string ButtonKeep { get; init; }
    public required string ButtonCancel { get; init; }
    public required string ButtonOpen { get; init; }
    public required string ButtonClose { get; init; }
    public required string ButtonDefault { get; init; }
    public required string ButtonOpenLog { get; init; }
    public required string RestartRequired { get; init; }
    public required string LanguageEnglish { get; init; }
    public required string LanguageRussian { get; init; }
    public required string LanguageAutoDetect { get; init; }
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

    /// <summary>Как назвать сообщение контроллера диктовки.</summary>
    public string Describe(DictationStatus status) => status.Notice switch
    {
        DictationNotice.Failed => string.Format(Culture, DictationFailed, status.Detail),
        DictationNotice.ModelLoadFailed => string.Format(Culture, StatusModelLoadFailed, status.Detail),
        DictationNotice.TooShort => DictationTooShort,
        DictationNotice.Cancelled => DictationCancelled,
        DictationNotice.Empty => DictationEmpty,
        DictationNotice.LengthLimitReached => DictationLengthLimit,
        DictationNotice.DeviceLost => string.Format(Culture, DictationDeviceLost, status.Detail),
        _ => StatusReady,
    };

    /// <summary>Как назвать этап сборки транскрипта.</summary>
    public string Describe(CallTranscriptionStage stage) => stage switch
    {
        CallTranscriptionStage.ReadingTracks => CallStageReading,
        CallTranscriptionStage.TranscribingMicrophone => CallStageMicrophone,
        CallTranscriptionStage.TranscribingOtherSide => CallStageOtherSide,
        CallTranscriptionStage.FilteringBleed => CallStageFiltering,
        _ => StatusTranscribingCall,
    };

    /// <summary>Как назвать причину самостоятельной остановки записи.</summary>
    public string Describe(CallStopReason reason) => reason switch
    {
        CallStopReason.DeviceLost => CallStoppedDeviceLost,
        CallStopReason.LengthLimitReached => CallStoppedLengthLimit,
        CallStopReason.DiskFull => CallStoppedDiskFull,
        _ => CallStoppedDeviceLost,
    };

    /// <summary>Как назвать место модели в ряду «качество против размера».</summary>
    public string Describe(ModelTier tier) => tier switch
    {
        ModelTier.Best => TierBest,
        ModelTier.Recommended => TierRecommended,
        ModelTier.Compact => TierCompact,
        _ => TierMinimal,
    };

    /// <summary>Подписи для готового транскрипта звонка.</summary>
    public CallTranscriptLabels TranscriptLabels => new(
        TranscriptStartedAt,
        TranscriptDuration,
        TranscriptTrigger,
        TranscriptParticipants,
        TranscriptBleedRemoved,
        TranscriptBleedByText,
        TranscriptBleedByEnergy,
        TranscriptNothingRecognized);

    private static System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.CurrentCulture;

    public static UiStrings English { get; } = new()
    {
        TrayStart = "Start dictation",
        TrayStop = "Stop dictation",
        TrayCancel = "Cancel",
        TrayTranscribing = "Transcribing…",
        TrayLoadingModel = "Loading model…",
        TrayHistory = "Recent dictations",
        TrayHistoryEmpty = "Nothing yet",
        TrayModel = "Model",
        TrayNoModels = "No models found",
        TrayAutoPaste = "Insert automatically",
        TrayAutoStart = "Start with Windows",
        TrayRetryHotkey = "Claim the hotkey again",
        TrayModelsFolder = "Models folder…",
        TraySettings = "Settings…",
        TrayExit = "Exit",
        TrayStartRecording = "Record a call",
        TrayStopRecording = "Stop recording",
        TrayCallsFolder = "Calls folder…",
        StatusRecordingCall = "Recording a call · {0}",
        StatusTranscribingCall = "Transcribing the call…",
        StatusCallSaved = "Call saved: {0}",
        NotifyCallReadyTitle = "Transcript ready",
        NotifyCallStoppedTitle = "Recording stopped",
        CallStoppedDeviceLost = "The audio device went away. What was recorded is saved.",
        CallStoppedLengthLimit = "The length limit was reached. What was recorded is saved.",
        CallStoppedDiskFull = "The disk is nearly full. What was recorded is saved.",

        StatusReady = "Ready",
        StatusLoadingModel = "Loading model…",
        StatusModelMissing = "No model found",
        StatusModelLoadFailed = "Model failed to load: {0}",
        StatusHotkeyBusy = "{0} is taken by another application",
        StatusModelStillLoading = "The model is still loading — one moment",
        StatusInClipboard = "In clipboard: {0}",
        StatusCopied = "Copied to clipboard",
        StatusTextReady = "Inserted · {0} words",
        StatusSettingsNotSaved = "Settings could not be saved — see the log",
        NotifyNoModelTitle = "No recognition model",
        NotifyNoModelBody = "Open Settings › Models to download one. Nothing works until you do.",
        NotifyHotkeyBusyTitle = "Hotkey unavailable",
        NotifyHotkeyBusyHint = "Free it, then choose \"Claim the hotkey again\" from the menu.",
        NotifyAlreadyRunningTitle = "Tapybara is already running",
        NotifyAlreadyRunningBody = "It lives here in the tray. Press your hotkey to dictate.",
        NotifyCrashTitle = "Something went wrong",
        NotifyCrashBody = "Tapybara kept running. Details are in the log: {0}",

        DictationFailed = "Dictation failed: {0}",
        DictationTooShort = "Too short — nothing to transcribe",
        DictationCancelled = "Dictation cancelled",
        DictationEmpty = "No speech recognised",
        DictationLengthLimit = "Length limit reached — transcribing what was recorded",
        DictationDeviceLost = "The microphone went away: {0}",

        PillTranscribing = "Transcribing… {0}%",
        PillInserted = "✓ Inserted",
        PillInsertedViaClipboard = "✓ Inserted via clipboard",
        PillClipboardOnly = "In clipboard — Ctrl+V",
        PillDone = "Done",
        PillCancelled = "Cancelled",
        PillRecordingCall = "Recording a call",
        PillHintStop = "{0} to stop",
        PillHintCancel = "Esc to cancel",

        SettingsTitle = "Tapybara settings",
        SectionDictation = "Dictation",
        SectionRecognition = "Recognition",
        SectionModels = "Models",
        SectionText = "Text",
        SectionCalls = "Calls",
        SectionGeneral = "General",
        SectionAbout = "About",

        GroupHotkey = "Hotkey",
        GroupAudioInput = "Microphone",
        GroupInsertion = "Where the text goes",
        GroupLimits = "Safety limits",
        GroupModel = "Model",
        GroupLanguageAndStyle = "Language and style",
        GroupSpeechDetection = "Speech detection",
        GroupModelsFolder = "Where models are kept",
        GroupDownload = "Download a model",
        GroupParagraphs = "Paragraphs",
        GroupReplacements = "Replacements",
        GroupRecording = "Recording",
        GroupTranscript = "Transcript",
        GroupAppearance = "Appearance",
        GroupStartupAndStorage = "Startup and storage",
        GroupPrivacy = "Privacy",

        FieldModel = "Recognition model",
        FieldModelHint = "Only models already in your models folder are listed",
        FieldDecoding = "Decoding",
        FieldDecodingHint = "How many wordings the model weighs before settling on one",
        DecodingFast = "Fast — first wording that fits",
        DecodingAccurate = "Accurate — weigh 5 (recommended)",
        DecodingThorough = "Thorough — weigh 8",
        FieldRecognitionLanguage = "Recognition language",
        FieldRecognitionLanguageHint = "Detect automatically unless you always dictate in one language — a wrong language does not degrade recognition, it destroys it",
        FieldPrompt = "Prompt",
        FieldPromptHint = "Biases vocabulary and anchors punctuation style. Keep it short and typical of what you dictate — an unrelated prompt makes recognition worse and slower.",
        FieldHotkey = "Hotkey",
        FieldHotkeyHint = "Press it to start, press it again to stop. Escape cancels.",
        FieldHotkeyCapturing = "Press a combination…",
        FieldHotkeyCaptureHint = "Esc to cancel. Win combinations are reserved by Windows.",
        FieldHotkeyTaken = "{0} is already taken by another application",
        FieldMicrophone = "Microphone",
        FieldMicrophoneHint = "Used for dictation and for your side of a call",
        FieldSystemAudioDevice = "Record the other side from",
        FieldSystemAudioDeviceHint = "The output device the call is playing on. If you talk through a headset while your speakers are the system default, choose the headset here or the other side records as silence.",
        DeviceSystemDefault = "System default",
        FieldAutoPaste = "Insert text automatically",
        FieldAutoPasteHint = "Off means the text only goes to the clipboard",
        FieldClipboardHistory = "Keep dictations out of clipboard history (Win+V)",
        FieldShowOverlay = "Show the floating indicator",
        FieldShowOverlayHint = "Drag the pill to move it. It remembers where you put it.",
        FieldMaxDictation = "Stop a dictation after",
        FieldMaxDictationHint = "A fuse against a dictation you forgot to stop",
        FieldSplitParagraphs = "Split into paragraphs on pauses",
        FieldParagraphPause = "Pause length",
        FieldReplacements = "Replacements",
        FieldReplacementsHint = "One per line, as: heard = correct. Works for names with punctuation too, like C# or .NET.",
        FieldUiLanguage = "Interface language",
        FieldUiLanguageAuto = "Same as Windows",
        FieldTheme = "Theme",
        ThemeSystem = "Same as Windows",
        ThemeLight = "Light",
        ThemeDark = "Dark",
        FieldModelsFolder = "Models folder",
        FieldModelsFolderHint = "Models are large. Keep them wherever you have room.",
        FieldPortable = "Portable mode",
        FieldPortableHint = "Keep settings and models next to the program instead of in AppData. Existing files are not moved.",
        FieldIdleUnload = "Unload the model after",
        FieldIdleUnloadHint = "An idle Tapybara then costs no memory",
        FieldUseVad = "Find speech before transcribing",
        FieldUseVadHint = "Locates where speech actually is, so each line gets the position it was measured at instead of one the model guessed. Also removes silence hallucinations and speeds transcription up. Needs a detector model.",
        FieldVadModel = "Detector model",
        FieldVadModelMissing = "No detector model installed — get one on the Models page",
        FieldVadThreshold = "Detector sensitivity",
        FieldVadThresholdHint = "Lower catches quieter speech but also more noise. Measure it instead of guessing.",
        FieldNormalize = "Even out loudness before transcribing",
        FieldNormalizeHint = "A microphone placed away from your face records far quieter than normal, and the speech detector decides by loudness.",
        FieldCallsFolder = "Calls folder",
        FieldMaxCall = "Stop a recording after",
        FieldMaxCallHint = "Two channels cost about 230 MB per hour",
        FieldMyName = "Your name in transcripts",
        FieldMyNameHint = "How your own lines are labelled",
        FieldOtherSideName = "Other side label",
        FieldOtherSideLanguage = "Other side language",
        FieldOtherSideLanguageHint = "Detect automatically is the sensible default: you know what you speak, you do not know what they will.",
        FieldAutoStart = "Start with Windows",
        FieldAutoStartHint = "Tapybara starts in the tray when you sign in",
        FieldAutoStartUnavailable = "Unavailable when running through the .NET CLI",
        FieldTrayPreview = "Show the start of dictations in the tray",
        FieldTrayPreviewHint = "Off by default: the tray tooltip is the one place dictated text stays on screen, and people dictate passwords and private messages.",

        ModelsIntro = "Tapybara needs a Whisper model to recognise speech. Nothing is bundled — models are hundreds of megabytes and the right one depends on your machine. Download one here; it goes straight into your models folder.",
        ModelsInstalled = "Installed",
        ModelsNothingInstalled = "Nothing installed yet",
        ModelsFullListLink = "Browse every model on Hugging Face",
        ModelsRecognitionHeader = "Recognition models",
        ModelsDetectorHeader = "Speech detector",
        ModelsDetectorNote = "A second, tiny model that finds where speech is. Under a megabyte, and it improves timings and removes silence hallucinations.",
        TierBest = "Best quality",
        TierRecommended = "Recommended",
        TierCompact = "Smaller",
        TierMinimal = "Fastest, least accurate",
        ButtonDownload = "Download",
        ButtonCancelDownload = "Cancel",
        LabelInstalled = "Installed",
        DownloadProgress = "{0} of {1} · {2}/s",
        DownloadFailed = "Download failed: {0}",
        DownloadCancelled = "Download cancelled",
        ModelsMoveTitle = "Move your models?",
        ModelsMoveQuestion = "{0} models ({1}) are sitting in the old folder. Move them across, or leave them where they are?",
        ModelsMoveQuestionOne = "One model ({0}) is sitting in the old folder. Move it across, or leave it where it is?",
        ModelsMoving = "Moving {0}…",
        ModelsMoveFailed = "Could not move: {0}",
        ModelsDeleteTitle = "Delete this model?",
        ModelsDeleteQuestion = "{0} ({1}) will be deleted from disk. You can download it again later.",
        ModelsDeleteInUse = "This model is in use. It will be unloaded first.",
        ModelsDeleteFailed = "Could not delete: {0}",
        ModelsTotalSize = "{0} on disk",
        LabelFrom = "From",
        LabelTo = "To",

        ButtonTest = "Measure…",
        ButtonStartTest = "Start",
        ButtonRepeatTest = "Again",
        ButtonStopTest = "Stop",
        ButtonApplyRecommended = "Use recommended",
        VadTestTitle = "Tune the speech detector",
        VadTestIntro = "The right sensitivity depends on your microphone, your room and your voice, so there is no single correct value — but it can be measured. Count from one to ten at your usual pace and volume. The detector then runs at several settings, and you can see where it starts losing speech.",
        VadTestReady = "Ready when you are",
        VadTestSpeakNow = "Speak now — {0} s left",
        VadTestAnalyzing = "Measuring…",
        VadTestResultsHeader = "Speech found at each setting",
        VadTestRow = "{0:F1} s in {1} fragments",
        VadTestRecommended = "Recommended: {0:F2} — the strictest setting that still keeps your speech. Stricter means less noise, but a lost word cannot be recovered.",
        VadTestNoSpeech = "No speech detected. Check that the right microphone is selected and try again.",
        VadTestDone = "Done",

        CallConsentTitle = "Before you record a call",
        CallConsentBody = "Tapybara records two channels: your microphone, and everything playing on your chosen output device. Read this once — it will not ask again.",
        CallConsentPoints =
            "• The other side is recorded too. In many places recording someone without telling them is illegal. Ask first.\n\n"
            + "• Everything audible is captured — notifications, music, another meeting in a second window — not just the call.\n\n"
            + "• Recordings and transcripts are stored unencrypted in your calls folder. They never leave this machine, but anyone with access to it can read them.",
        CallConsentAccept = "I understand — start recording",
        CallConsentCancel = "Not now",
        CallConsentNote = "Recording captures the other side as well. Make sure everyone knows.",

        TranscriptStartedAt = "Started",
        TranscriptDuration = "Duration",
        TranscriptTrigger = "Trigger",
        TranscriptParticipants = "Participants",
        TranscriptBleedRemoved = "Other-side speech removed from your channel",
        TranscriptBleedByText = "by text",
        TranscriptBleedByEnergy = "by loudness",
        TranscriptNothingRecognized = "_No speech recognised._",
        CallStageReading = "Reading the tracks…",
        CallStageMicrophone = "Transcribing your channel…",
        CallStageOtherSide = "Transcribing the other side…",
        CallStageFiltering = "Filtering out bleed…",

        ButtonBrowse = "Browse…",
        ButtonUseDefault = "Use default",
        ButtonDelete = "Delete",
        ButtonMove = "Move them",
        ButtonKeep = "Leave them",
        ButtonCancel = "Cancel",
        ButtonOpen = "Open",
        ButtonClose = "Close",
        ButtonDefault = "Default",
        ButtonOpenLog = "Open log",
        RestartRequired = "Takes effect after a restart",
        LanguageEnglish = "English",
        LanguageRussian = "Russian",
        LanguageAutoDetect = "Detect automatically",
        Minutes = "min",
        Seconds = "s",
        FooterStoragePath = "Settings and models are stored in {0}",
        AboutTagline = "Local voice dictation. Everything runs on this machine — no audio and no text ever leave it.",
        AboutVersion = "Version",
        AboutAuthors = "Made by",
        AboutAuthorsValue = "Señor Mega and his best bud, Claude",
        AboutRuntime = "Compute backend",
        AboutRuntimeHint = "Which native whisper.cpp library actually loaded",
        AboutLicense = "Licence",
        AboutComponents = "Built on whisper.cpp, Whisper.net, NAudio and WPF-UI.",
        ButtonCopyDiagnostics = "Copy diagnostics",
        DiagnosticsCopied = "Copied",
    };

    public static UiStrings Russian { get; } = new()
    {
        TrayStart = "Начать диктовку",
        TrayStop = "Закончить диктовку",
        TrayCancel = "Отменить",
        TrayTranscribing = "Распознаю…",
        TrayLoadingModel = "Загружаю модель…",
        TrayHistory = "Последние диктовки",
        TrayHistoryEmpty = "Пока пусто",
        TrayModel = "Модель",
        TrayNoModels = "Моделей не найдено",
        TrayAutoPaste = "Вставлять автоматически",
        TrayAutoStart = "Запускать с Windows",
        TrayRetryHotkey = "Занять горячую клавишу заново",
        TrayModelsFolder = "Папка моделей…",
        TraySettings = "Настройки…",
        TrayExit = "Выход",
        TrayStartRecording = "Записать звонок",
        TrayStopRecording = "Остановить запись",
        TrayCallsFolder = "Папка звонков…",
        StatusRecordingCall = "Записываю звонок · {0}",
        StatusTranscribingCall = "Распознаю звонок…",
        StatusCallSaved = "Звонок сохранён: {0}",
        NotifyCallReadyTitle = "Транскрипт готов",
        NotifyCallStoppedTitle = "Запись остановлена",
        CallStoppedDeviceLost = "Звуковое устройство пропало. Записанное сохранено.",
        CallStoppedLengthLimit = "Достигнут предел длительности. Записанное сохранено.",
        CallStoppedDiskFull = "На диске почти нет места. Записанное сохранено.",

        StatusReady = "Готов",
        StatusLoadingModel = "Загружаю модель…",
        StatusModelMissing = "Модель не найдена",
        StatusModelLoadFailed = "Модель не загрузилась: {0}",
        StatusHotkeyBusy = "{0} занято другим приложением",
        StatusModelStillLoading = "Модель ещё грузится — секунду",
        StatusInClipboard = "В буфере: {0}",
        StatusCopied = "Скопировано в буфер обмена",
        StatusTextReady = "Вставлено · слов: {0}",
        StatusSettingsNotSaved = "Настройки не сохранились — смотрите журнал",
        NotifyNoModelTitle = "Нет модели распознавания",
        NotifyNoModelBody = "Откройте «Настройки › Модели» и скачайте её. Без модели ничего не работает.",
        NotifyHotkeyBusyTitle = "Горячая клавиша недоступна",
        NotifyHotkeyBusyHint = "Освободите её и выберите в меню «Занять горячую клавишу заново».",
        NotifyAlreadyRunningTitle = "Tapybara уже запущена",
        NotifyAlreadyRunningBody = "Она живёт здесь, в трее. Нажмите горячую клавишу, чтобы диктовать.",
        NotifyCrashTitle = "Что-то пошло не так",
        NotifyCrashBody = "Tapybara продолжает работать. Подробности в журнале: {0}",

        DictationFailed = "Диктовка не удалась: {0}",
        DictationTooShort = "Слишком коротко — нечего распознавать",
        DictationCancelled = "Диктовка отменена",
        DictationEmpty = "Речь не распознана",
        DictationLengthLimit = "Достигнут предел длительности — распознаю записанное",
        DictationDeviceLost = "Микрофон пропал: {0}",

        PillTranscribing = "Распознаю… {0}%",
        PillInserted = "✓ Вставлено",
        PillInsertedViaClipboard = "✓ Вставлено через буфер",
        PillClipboardOnly = "В буфере — Ctrl+V",
        PillDone = "Готово",
        PillCancelled = "Отменено",
        PillRecordingCall = "Записываю звонок",
        PillHintStop = "{0} — закончить",
        PillHintCancel = "Esc — отменить",

        SettingsTitle = "Настройки Tapybara",
        SectionDictation = "Диктовка",
        SectionRecognition = "Распознавание",
        SectionModels = "Модели",
        SectionText = "Текст",
        SectionCalls = "Звонки",
        SectionGeneral = "Общие",
        SectionAbout = "О программе",

        GroupHotkey = "Горячая клавиша",
        GroupAudioInput = "Микрофон",
        GroupInsertion = "Куда попадает текст",
        GroupLimits = "Предохранители",
        GroupModel = "Модель",
        GroupLanguageAndStyle = "Язык и стиль",
        GroupSpeechDetection = "Поиск речи",
        GroupModelsFolder = "Где лежат модели",
        GroupDownload = "Скачать модель",
        GroupParagraphs = "Абзацы",
        GroupReplacements = "Замены",
        GroupRecording = "Запись",
        GroupTranscript = "Транскрипт",
        GroupAppearance = "Внешний вид",
        GroupStartupAndStorage = "Запуск и хранение",
        GroupPrivacy = "Приватность",

        FieldModel = "Модель распознавания",
        FieldModelHint = "В списке только то, что уже лежит в папке моделей",
        FieldDecoding = "Декодирование",
        FieldDecodingHint = "Сколько вариантов фразы модель взвешивает, прежде чем выбрать один",
        DecodingFast = "Быстро — первый подходящий",
        DecodingAccurate = "Точно — взвешивать 5 (рекомендуется)",
        DecodingThorough = "Тщательно — взвешивать 8",
        FieldRecognitionLanguage = "Язык распознавания",
        FieldRecognitionLanguageHint = "Определять автоматически, если только вы не диктуете всегда на одном языке: неверный язык не ухудшает распознавание, а разрушает его",
        FieldPrompt = "Подсказка",
        FieldPromptHint = "Биасит словарь и задаёт стиль пунктуации. Держите её короткой и похожей на то, что вы диктуете: посторонняя подсказка делает распознавание хуже и медленнее.",
        FieldHotkey = "Горячая клавиша",
        FieldHotkeyHint = "Нажатие начинает диктовку, повторное заканчивает. Escape отменяет.",
        FieldHotkeyCapturing = "Нажмите сочетание…",
        FieldHotkeyCaptureHint = "Esc — отмена. Сочетания с Win зарезервированы Windows.",
        FieldHotkeyTaken = "{0} уже занято другим приложением",
        FieldMicrophone = "Микрофон",
        FieldMicrophoneHint = "Используется для диктовки и для вашего канала в звонке",
        FieldSystemAudioDevice = "Собеседника писать с",
        FieldSystemAudioDeviceHint = "Устройство вывода, на котором идёт разговор. Если вы говорите через гарнитуру, а по умолчанию стоят колонки, выберите здесь гарнитуру — иначе канал собеседника запишется тишиной.",
        DeviceSystemDefault = "Устройство по умолчанию",
        FieldAutoPaste = "Вставлять текст автоматически",
        FieldAutoPasteHint = "Выключено — текст только кладётся в буфер обмена",
        FieldClipboardHistory = "Прятать диктовки из истории буфера (Win+V)",
        FieldShowOverlay = "Показывать плавающий индикатор",
        FieldShowOverlayHint = "Пилюлю можно перетащить. Она запомнит, куда вы её поставили.",
        FieldMaxDictation = "Обрывать диктовку через",
        FieldMaxDictationHint = "Предохранитель на случай забытой диктовки",
        FieldSplitParagraphs = "Разбивать на абзацы по паузам",
        FieldParagraphPause = "Длина паузы",
        FieldReplacements = "Замены",
        FieldReplacementsHint = "По одной в строке, в виде: услышано = правильно. Работает и для названий со знаками — C# или .NET.",
        FieldUiLanguage = "Язык интерфейса",
        FieldUiLanguageAuto = "Как в Windows",
        FieldTheme = "Тема",
        ThemeSystem = "Как в Windows",
        ThemeLight = "Светлая",
        ThemeDark = "Тёмная",
        FieldModelsFolder = "Папка моделей",
        FieldModelsFolderHint = "Модели большие. Держите их там, где есть место.",
        FieldPortable = "Портативный режим",
        FieldPortableHint = "Хранить настройки и модели рядом с программой, а не в AppData. Существующие файлы не переносятся.",
        FieldIdleUnload = "Выгружать модель через",
        FieldIdleUnloadHint = "В простое Tapybara тогда не занимает память",
        FieldUseVad = "Искать речь перед распознаванием",
        FieldUseVadHint = "Находит, где на записи действительно речь: реплика получает измеренное положение, а не предсказанное моделью. Заодно исчезают галлюцинации на тишине и падает время работы. Нужна модель детектора.",
        FieldVadModel = "Модель детектора",
        FieldVadModelMissing = "Модель детектора не установлена — скачайте её в разделе «Модели»",
        FieldVadThreshold = "Чувствительность детектора",
        FieldVadThresholdHint = "Ниже — ловит более тихую речь, но и больше шума. Это можно не угадывать, а измерить.",
        FieldNormalize = "Выравнивать громкость перед распознаванием",
        FieldNormalizeHint = "Микрофон, отодвинутый от лица, пишет в разы тише обычного, а детектор речи решает по громкости.",
        FieldCallsFolder = "Папка звонков",
        FieldMaxCall = "Обрывать запись через",
        FieldMaxCallHint = "Два канала стоят около 230 МБ в час",
        FieldMyName = "Ваше имя в транскриптах",
        FieldMyNameHint = "Как подписывать ваши реплики",
        FieldOtherSideName = "Подпись собеседника",
        FieldOtherSideLanguage = "Язык собеседника",
        FieldOtherSideLanguageHint = "Определять автоматически — разумное умолчание: свой язык вы знаете, а язык собеседника заранее нет.",
        FieldAutoStart = "Запускать с Windows",
        FieldAutoStartHint = "Tapybara появится в трее при входе в систему",
        FieldAutoStartUnavailable = "Недоступно при запуске через .NET CLI",
        FieldTrayPreview = "Показывать начало диктовки в трее",
        FieldTrayPreviewHint = "По умолчанию выключено: подсказка трея — единственное место, где надиктованное надолго остаётся на экране, а диктуют в том числе пароли и переписку.",

        ModelsIntro = "Tapybara нужна модель Whisper, чтобы распознавать речь. В комплект она не входит: модели весят сотни мегабайт, и подходящая зависит от вашей машины. Скачайте её здесь — она сразу попадёт в папку моделей.",
        ModelsInstalled = "Установлены",
        ModelsNothingInstalled = "Пока ничего не установлено",
        ModelsFullListLink = "Посмотреть все модели на Hugging Face",
        ModelsRecognitionHeader = "Модели распознавания",
        ModelsDetectorHeader = "Детектор речи",
        ModelsDetectorNote = "Вторая, крошечная модель: находит, где речь. Меньше мегабайта, а тайминги становятся точнее и исчезают галлюцинации на тишине.",
        TierBest = "Лучшее качество",
        TierRecommended = "Рекомендуется",
        TierCompact = "Компактнее",
        TierMinimal = "Быстрее всего, точность ниже",
        ButtonDownload = "Скачать",
        ButtonCancelDownload = "Отменить",
        LabelInstalled = "Установлена",
        DownloadProgress = "{0} из {1} · {2}/с",
        DownloadFailed = "Скачать не удалось: {0}",
        DownloadCancelled = "Скачивание отменено",
        ModelsMoveTitle = "Перенести модели?",
        ModelsMoveQuestion = "В прежней папке лежат модели: {0} шт. ({1}). Перенести их в новую или оставить на месте?",
        ModelsMoveQuestionOne = "В прежней папке лежит одна модель ({0}). Перенести её в новую или оставить на месте?",
        ModelsMoving = "Переношу {0}…",
        ModelsMoveFailed = "Не удалось перенести: {0}",
        ModelsDeleteTitle = "Удалить модель?",
        ModelsDeleteQuestion = "{0} ({1}) будет удалена с диска. Скачать её заново можно в любой момент.",
        ModelsDeleteInUse = "Модель сейчас используется — сначала она будет выгружена.",
        ModelsDeleteFailed = "Не удалось удалить: {0}",
        ModelsTotalSize = "{0} на диске",
        LabelFrom = "Откуда",
        LabelTo = "Куда",

        ButtonTest = "Измерить…",
        ButtonStartTest = "Начать",
        ButtonRepeatTest = "Ещё раз",
        ButtonStopTest = "Остановить",
        ButtonApplyRecommended = "Применить рекомендованное",
        VadTestTitle = "Настройка детектора речи",
        VadTestIntro = "Верная чувствительность зависит от микрофона, комнаты и голоса, поэтому единственного правильного значения не существует — но его можно измерить. Посчитайте вслух от одного до десяти в обычном темпе и с обычной громкостью. Детектор прогонит запись на нескольких порогах, и будет видно, где он начинает терять речь.",
        VadTestReady = "Готовы — начинайте",
        VadTestSpeakNow = "Говорите — осталось {0} с",
        VadTestAnalyzing = "Измеряю…",
        VadTestResultsHeader = "Сколько речи найдено на каждом пороге",
        VadTestRow = "{0:F1} с в {1} фрагментах",
        VadTestRecommended = "Рекомендуется {0:F2} — самый строгий порог, который ещё не теряет вашу речь. Строже означает меньше шума, но потерянное слово не восстановить.",
        VadTestNoSpeech = "Речь не обнаружена. Проверьте, что выбран нужный микрофон, и попробуйте снова.",
        VadTestDone = "Готово",

        CallConsentTitle = "Прежде чем записывать звонок",
        CallConsentBody = "Tapybara пишет два канала: ваш микрофон и всё, что звучит на выбранном устройстве вывода. Прочитайте это один раз — больше спрашивать не будем.",
        CallConsentPoints =
            "• Собеседник записывается тоже. Во многих странах запись человека без его ведома незаконна. Спросите заранее.\n\n"
            + "• Пишется всё слышимое — уведомления, музыка, другая встреча в соседнем окне, — а не только разговор.\n\n"
            + "• Записи и транскрипты лежат в папке звонков без шифрования. Машину они не покидают, но их прочитает любой, у кого есть к ней доступ.",
        CallConsentAccept = "Понятно — начать запись",
        CallConsentCancel = "Не сейчас",
        CallConsentNote = "Запись захватывает и собеседника. Убедитесь, что все об этом знают.",

        TranscriptStartedAt = "Начало",
        TranscriptDuration = "Длительность",
        TranscriptTrigger = "Триггер",
        TranscriptParticipants = "Участники",
        TranscriptBleedRemoved = "Отсеяно чужой речи из своего канала",
        TranscriptBleedByText = "по тексту",
        TranscriptBleedByEnergy = "по громкости",
        TranscriptNothingRecognized = "_Речь не распознана._",
        CallStageReading = "Читаю дорожки…",
        CallStageMicrophone = "Распознаю ваш канал…",
        CallStageOtherSide = "Распознаю собеседников…",
        CallStageFiltering = "Отсеиваю чужую речь…",

        ButtonBrowse = "Обзор…",
        ButtonUseDefault = "По умолчанию",
        ButtonDelete = "Удалить",
        ButtonMove = "Перенести",
        ButtonKeep = "Оставить",
        ButtonCancel = "Отмена",
        ButtonOpen = "Открыть",
        ButtonClose = "Закрыть",
        ButtonDefault = "По умолчанию",
        ButtonOpenLog = "Открыть журнал",
        RestartRequired = "Вступит в силу после перезапуска",
        LanguageEnglish = "Английский",
        LanguageRussian = "Русский",
        LanguageAutoDetect = "Определять автоматически",
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
        DiagnosticsCopied = "Скопировано",
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
