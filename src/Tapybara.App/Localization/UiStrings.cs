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
    public required string TrayRetryHotkey { get; init; }
    public required string TraySettings { get; init; }
    public required string TrayExit { get; init; }
    public required string TrayStartRecording { get; init; }
    public required string TrayStopRecording { get; init; }
    public required string StatusRecordingCall { get; init; }
    public required string StatusTranscribingCall { get; init; }
    public required string StatusCallSaved { get; init; }
    public required string NotifyCallReadyTitle { get; init; }
    public required string NotifyCallReadyBody { get; init; }
    public required string NotifyCallNamesTitle { get; init; }
    public required string NotifyCallNamesBody { get; init; }
    public required string StatusTranscribingCallProgress { get; init; }
    public required string PillTranscribingCall { get; init; }
    public required string PillCallReady { get; init; }
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
    public required string StatusTextReady { get; init; }
    public required string StatusSettingsNotSaved { get; init; }
    public required string NotifyNoModelTitle { get; init; }
    public required string NotifyNoModelBody { get; init; }
    public required string NotifyHotkeyBusyTitle { get; init; }
    public required string NotifyHotkeyBusyHint { get; init; }
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
    public required string PillStopCall { get; init; }

    // --- окно настроек: разделы ---
    public required string SettingsTitle { get; init; }
    public required string SectionDictation { get; init; }
    public required string SectionModels { get; init; }
    public required string SectionCalls { get; init; }
    public required string SectionGeneral { get; init; }
    public required string SectionAbout { get; init; }

    // --- окно настроек: заголовки групп ---
    public required string GroupHotkey { get; init; }
    public required string GroupAudioInput { get; init; }
    public required string GroupInsertion { get; init; }
    public required string GroupLimits { get; init; }
    public required string GroupSpeechDetection { get; init; }
    public required string GroupModelsFolder { get; init; }
    public required string GroupParagraphs { get; init; }
    public required string GroupReplacements { get; init; }
    public required string GroupRecording { get; init; }
    public required string GroupAppearance { get; init; }
    public required string GroupPrivacy { get; init; }

    // --- окно настроек: поля ---
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
    public required string FieldHotkeyDuplicate { get; init; }
    public required string FieldCallHotkeyHint { get; init; }
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
    public required string FieldUiLanguage { get; init; }
    public required string FieldUiLanguageAuto { get; init; }
    public required string FieldTheme { get; init; }
    public required string ThemeSystem { get; init; }
    public required string ThemeLight { get; init; }
    public required string ThemeDark { get; init; }
    public required string FieldModelsFolder { get; init; }
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
    public required string FieldOtherSideLanguage { get; init; }
    public required string FieldOtherSideLanguageHint { get; init; }
    public required string FieldAutoStart { get; init; }
    public required string FieldAutoStartHint { get; init; }
    public required string FieldAutoStartUnavailable { get; init; }
    public required string FieldTrayPreview { get; init; }
    public required string FieldTrayPreviewHint { get; init; }

    // --- загрузка моделей ---
    public required string ModelsIntro { get; init; }
    public required string ModelsFullListLink { get; init; }
    public required string ModelsRecognitionHeader { get; init; }
    public required string ModelsDetectorHeader { get; init; }
    public required string TierBest { get; init; }
    public required string TierRecommended { get; init; }
    public required string TierCompact { get; init; }
    public required string TierMinimal { get; init; }
    public required string ButtonDownload { get; init; }
    public required string ButtonCancelDownload { get; init; }
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
    public required string TranscriptVoicesSplit { get; init; }
    public required string TranscriptVoicesHinted { get; init; }
    public required string TranscriptVoicesGuessed { get; init; }
    public required string TranscriptUnknownSpeaker { get; init; }
    public required string TranscriptVoice { get; init; }

    // --- звонок: кто в нём был ---
    public required string ParticipantsEntryPlaceholder { get; init; }
    public required string ParticipantsEntryHint { get; init; }
    public required string ParticipantsMeHint { get; init; }
    public required string ParticipantsAddHint { get; init; }
    public required string ParticipantsRemoveHint { get; init; }
    public required string CallReviewTitle { get; init; }
    public required string CallReviewNotePlaceholder { get; init; }
    public required string CallReviewDelete { get; init; }
    public required string CallReviewSave { get; init; }
    public required string CallReviewHintOne { get; init; }
    public required string CallDeleteTitle { get; init; }
    public required string CallDeleteBody { get; init; }
    public required string CallDeleteConfirm { get; init; }

    // --- окно записей ---
    public required string UnitSeconds { get; init; }
    public required string UnitMinutes { get; init; }
    public required string UnitHoursMinutes { get; init; }
    public required string CallsEmpty { get; init; }
    public required string CallsNothingSelected { get; init; }
    public required string CallsOpenFolder { get; init; }
    public required string CallsTranscribe { get; init; }
    public required string CallsTranscribeAgain { get; init; }
    public required string CallsTotals { get; init; }
    public required string CallsMegabytes { get; init; }
    public required string CallStateRecording { get; init; }
    public required string CallStateTranscribing { get; init; }
    public required string CallStateReady { get; init; }
    public required string CallStateNeedsNames { get; init; }
    public required string UnitHours { get; init; }
    public required string UnitGigabytes { get; init; }
    public required string UnitKilobytes { get; init; }
    public required string CallUntitled { get; init; }
    public required string VoiceChange { get; init; }
    public required string VoicesResplitMenu { get; init; }
    public required string FieldRememberVoices { get; init; }
    public required string GroupListening { get; init; }
    public required string FieldPlayingProgress { get; init; }
    public required string FieldPlayingProgressHint { get; init; }
    public required string FieldRememberVoicesHint { get; init; }
    public required string ButtonForgetVoices { get; init; }
    public required string ForgetVoicesTitle { get; init; }
    public required string ForgetVoicesBody { get; init; }
    public required string VoiceSuggestion { get; init; }
    public required string VoiceAcceptSuggestion { get; init; }
    public required string BannerAcceptSuggestions { get; init; }
    public required string BannerSuggested { get; init; }
    public required string DictionaryVoiceKnown { get; init; }
    public required string DictionaryForgetVoice { get; init; }
    public required string WelcomeTitle { get; init; }
    public required string WelcomeStepModel { get; init; }
    public required string WelcomeStepMicrophone { get; init; }
    public required string WelcomeStepTry { get; init; }
    public required string WelcomeModelHeading { get; init; }
    public required string WelcomeModelBody { get; init; }
    public required string WelcomeModelDetector { get; init; }
    public required string WelcomeModelHave { get; init; }
    public required string WelcomeMicHeading { get; init; }
    public required string WelcomeMicBody { get; init; }
    public required string WelcomeTryHeading { get; init; }
    public required string WelcomeTryBody { get; init; }
    public required string WelcomeTryPlaceholder { get; init; }
    public required string WelcomeTryDone { get; init; }
    public required string ButtonNext { get; init; }
    public required string ButtonBack { get; init; }
    public required string ButtonFinish { get; init; }
    public required string ButtonSkip { get; init; }
    public required string NavDictations { get; init; }
    public required string NavCalls { get; init; }
    public required string NavDictionary { get; init; }
    public required string NavSettings { get; init; }
    public required string TrayOpen { get; init; }
    public required string DictationsSearch { get; init; }
    public required string DictationsEmpty { get; init; }
    public required string DictationsHistoryOff { get; init; }
    public required string DictationsCount { get; init; }
    public required string DictationCopy { get; init; }
    public required string DictationDelete { get; init; }
    public required string DictationWords { get; init; }
    public required string DictionaryIntro { get; init; }
    public required string DictionaryReplacementsHint { get; init; }
    public required string DictionaryPeople { get; init; }
    public required string DictionaryPeopleHint { get; init; }
    public required string DictionaryPeopleEmpty { get; init; }
    public required string DictionaryHeardPlaceholder { get; init; }
    public required string DictionaryCorrectPlaceholder { get; init; }
    public required string DictionaryExport { get; init; }
    public required string DictionaryImport { get; init; }
    public required string DictionaryFileFilter { get; init; }
    public required string DictionaryFileName { get; init; }
    public required string DictionaryExported { get; init; }
    public required string DictionaryImported { get; init; }
    public required string DictionaryImportedVoices { get; init; }
    public required string DictionaryImportedPrompt { get; init; }
    public required string DictionaryImportedNothing { get; init; }
    public required string DictionaryVoicesOtherModel { get; init; }
    public required string DictionaryVoicesOff { get; init; }
    public required string DictionaryFileFailed { get; init; }
    public required string DictionaryNotADictionary { get; init; }
    public required string DictionarySearch { get; init; }
    public required string DictionaryAnotherVariant { get; init; }
    public required string DictionaryNoMatches { get; init; }
    public required string CardHeading { get; init; }
    public required string CardTitleField { get; init; }
    public required string CardTitlePlaceholder { get; init; }
    public required string CardWho { get; init; }
    public required string CardHintNone { get; init; }
    public required string CardHintMany { get; init; }
    public required string CardOpenCall { get; init; }
    public required string CardReady { get; init; }
    public required string CardNeedsNames { get; init; }
    public required string CardWaiting { get; init; }
    public required string SectionAdvanced { get; init; }
    public required string GroupWhoSpeaks { get; init; }
    public required string GroupStorage { get; init; }
    public required string GroupRecognition { get; init; }
    public required string GroupStartup { get; init; }
    public required string FieldKeepHistory { get; init; }
    public required string FieldKeepHistoryHint { get; init; }
    public required string ButtonClearHistory { get; init; }
    public required string ClearHistoryTitle { get; init; }
    public required string ClearHistoryBody { get; init; }
    public required string AboutDataFolder { get; init; }
    public required string AdvancedIntro { get; init; }
    public required string CallsSearchPlaceholder { get; init; }
    public required string DayToday { get; init; }
    public required string DayYesterday { get; init; }
    public required string CallsListen { get; init; }
    public required string CallsStopListening { get; init; }
    public required string CallsCopyText { get; init; }
    public required string CallsMore { get; init; }
    public required string CallsOpenTranscriptFile { get; init; }
    public required string CallsTabTranscript { get; init; }
    public required string CallsTabNote { get; init; }
    public required string BannerNeedsNamesOne { get; init; }
    public required string BannerNeedsNamesMany { get; init; }
    public required string BannerLegacy { get; init; }
    public required string BannerNotTranscribed { get; init; }
    public required string BannerTranscribing { get; init; }
    public required string BannerRecording { get; init; }
    public required string BannerDamaged { get; init; }
    public required string PeopleHeader { get; init; }
    public required string VoiceItsMe { get; init; }
    public required string VoiceDetach { get; init; }
    public required string VoiceWho { get; init; }
    public required string VoiceOtherName { get; init; }
    public required string VoicePlayQuote { get; init; }
    public required string VoiceMe { get; init; }
    public required string DictationNoModel { get; init; }
    public required string DictationDownloadModel { get; init; }
    public required string DictationOpenModels { get; init; }
    public required string UpdatesTitle { get; init; }
    public required string UpdatesManual { get; init; }
    public required string UpdatesReleases { get; init; }
    public required string UpdatesIdle { get; init; }
    public required string UpdatesChecking { get; init; }
    public required string UpdatesUpToDate { get; init; }
    public required string UpdatesDownloading { get; init; }
    public required string UpdatesReady { get; init; }
    public required string UpdatesFailed { get; init; }
    public required string UpdatesCheckNow { get; init; }
    public required string UpdatesRestart { get; init; }
    public required string UpdatesBusy { get; init; }
    public required string FieldAutoUpdate { get; init; }
    public required string FieldAutoUpdateHint { get; init; }
    public required string TrayUpdateReady { get; init; }
    public required string FieldPortableInstalled { get; init; }
    public required string AboutRuntimeNotLoaded { get; init; }
    public required string VoiceNotThis { get; init; }
    public required string VoiceSomeoneElse { get; init; }
    public required string VoiceSomeoneElseSplit { get; init; }
    public required string VoicesMoreTitle { get; init; }
    public required string VoicesMoreHint { get; init; }
    public required string VoicesSplitInto { get; init; }
    public required string VoiceRejectedHint { get; init; }
    public required string LineDoubtful { get; init; }
    public required string ModelsSegmentationHeader { get; init; }
    public required string ModelsEmbeddingHeader { get; init; }
    public required string ModelsForCalls { get; init; }
    public required string ModelsCustom { get; init; }
    public required string VoicesOpenModels { get; init; }
    public required string VoiceSomeoneElseGetModel { get; init; }
    public required string VoicesResplitCount { get; init; }
    public required string VoicesResplitGo { get; init; }
    public required string ModelsMissingList { get; init; }
    public required string VoicesNeedModels { get; init; }
    public required string CardModelsMissing { get; init; }
    public required string LineFixWord { get; init; }
    public required string LineEditText { get; init; }
    public required string LineFixSelection { get; init; }
    public required string CallsStopTranscribing { get; init; }
    public required string PersonRenameEverywhere { get; init; }
    public required string RenameTitle { get; init; }
    public required string RenameBody { get; init; }
    public required string RenameInCalls { get; init; }
    public required string RenameMentions { get; init; }
    public required string RenameGo { get; init; }
    public required string CallsCopyAnonymized { get; init; }
    public required string AnonymizeTitle { get; init; }
    public required string AnonymizeBody { get; init; }
    public required string AnonymizeMentions { get; init; }
    public required string AnonymizeNumbered { get; init; }
    public required string AnonymizeParticipant { get; init; }
    public required string AnonymizeCopy { get; init; }
    public required string FixSimilar { get; init; }
    public required string FixRemember { get; init; }
    public required string FixReplaceAll { get; init; }
    public required string FixOnlyHere { get; init; }
    public required string RetranscribeEditedTitle { get; init; }
    public required string RetranscribeEditedBody { get; init; }
    public required string DictionaryApplyToCalls { get; init; }
    public required string DictionaryApplyTitle { get; init; }
    public required string DictionaryApplyBody { get; init; }
    public required string DictionaryApplyGo { get; init; }
    public required string DictionaryApplyNothing { get; init; }
    public required string DictionaryApplied { get; init; }
    public required string RecordCall { get; init; }
    public required string RecordDictate { get; init; }
    public required string RecordStop { get; init; }
    public required string RecordTranscribing { get; init; }
    public required string DictateHint { get; init; }
    public required string VoiceNamePlaceholder { get; init; }
    public required string LineSaidBy { get; init; }
    public required string LinePlayFrom { get; init; }
    public required string LineCopy { get; init; }
    public required string LineYourMicrophone { get; init; }
    public required string ReplacementTitle { get; init; }
    public required string ButtonAdd { get; init; }
    public required string CallStateNotTranscribed { get; init; }
    public required string CallStateDamaged { get; init; }
    public required string TranscriptBleedRemoved { get; init; }
    public required string TranscriptBleedByText { get; init; }
    public required string TranscriptBleedByEnergy { get; init; }
    public required string TranscriptNothingRecognized { get; init; }
    public required string CallStageReading { get; init; }
    public required string CallStageMicrophone { get; init; }
    public required string CallStageOtherSide { get; init; }
    public required string CallStageFiltering { get; init; }
    public required string CallStageSplittingVoices { get; init; }
    public required string GroupVoices { get; init; }
    public required string FieldSplitVoices { get; init; }
    public required string FieldSplitVoicesHint { get; init; }
    public required string FieldVoiceThreshold { get; init; }
    public required string FieldVoiceThresholdHint { get; init; }

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
    public required string AboutSite { get; init; }
    public required string AboutSiteHint { get; init; }
    public required string AboutSource { get; init; }
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
        CallTranscriptionStage.SplittingVoices => CallStageSplittingVoices,
        CallTranscriptionStage.FilteringBleed => CallStageFiltering,
        _ => StatusTranscribingCall,
    };

    /// <summary>Этап и проценты распознавания звонка — для трея.</summary>
    public string Describe(CallTranscriptionProgress progress) =>
        string.Format(Culture, StatusTranscribingCallProgress, Describe(progress.Stage), progress.Percent);

    /// <summary>Как назвать причину самостоятельной остановки записи.</summary>
    public string Describe(CallStopReason reason) => reason switch
    {
        CallStopReason.DeviceLost => CallStoppedDeviceLost,
        CallStopReason.LengthLimitReached => CallStoppedLengthLimit,
        CallStopReason.DiskFull => CallStoppedDiskFull,
        _ => CallStoppedDeviceLost,
    };

    /// <summary>Как назвать место модели в ряду «качество против размера».</summary>
    /// <summary>Название типа модели — тот же заголовок, что у его группы на странице моделей.</summary>
    public string KindName(Tapybara.Core.Models.ModelKind kind) => kind switch
    {
        Tapybara.Core.Models.ModelKind.VoiceSegmentation => ModelsSegmentationHeader,
        Tapybara.Core.Models.ModelKind.VoiceEmbedding => ModelsEmbeddingHeader,
        Tapybara.Core.Models.ModelKind.SpeechDetector => ModelsDetectorHeader,
        _ => ModelsRecognitionHeader,
    };

    /// <summary>«Разделение голосов», «Слепки голоса» — через запятую, в кавычках.</summary>
    public string KindNames(IEnumerable<Tapybara.Core.Models.ModelKind> kinds) =>
        string.Join(", ", kinds.Select(k => Quote(KindName(k))));

    private string Quote(string text) => Formatting.TwoLetterISOLanguageName == "ru" ? $"«{text}»" : $"“{text}”";

    public string Describe(ModelTier tier) => tier switch
    {
        ModelTier.Best => TierBest,
        ModelTier.Recommended => TierRecommended,
        ModelTier.Compact => TierCompact,
        _ => TierMinimal,
    };

    /// <summary>
    /// Длительность записи словами, а не как время на часах.
    /// </summary>
    /// <remarks>
    /// «42:00» рядом с «12:54» читается как ещё одно время суток, а «0:47» —
    /// как без тринадцати час. Единицы снимают вопрос ценой трёх строк.
    /// Отметки времени ВНУТРИ транскрипта — другое дело: там «12:04» означает
    /// позицию в записи, и минуты с секундами читаются однозначно.
    /// </remarks>
    public string Duration(TimeSpan length)
    {
        if (length.TotalMinutes < 1)
        {
            return string.Format(Culture, UnitSeconds, (int)length.TotalSeconds);
        }

        if (length.TotalHours < 1)
        {
            return string.Format(Culture, UnitMinutes, (int)length.TotalMinutes);
        }

        // «1 ч 0 мин» — лишнее слово: ровный час так и пишется.
        return length.Minutes == 0
            ? string.Format(Culture, UnitHours, (int)length.TotalHours)
            : string.Format(Culture, UnitHoursMinutes, (int)length.TotalHours, length.Minutes);
    }

    /// <summary>
    /// Размер: килобайты до мегабайта, мегабайты до гигабайта, дальше
    /// гигабайты с десятыми.
    /// </summary>
    /// <remarks>
    /// <para>«1861 МБ» приходится пересчитывать в уме; «1,8 ГБ» — нет.</para>
    /// <para>
    /// Одна функция на всё приложение. Раньше у настроек и у окна первого
    /// запуска были свои копии с английскими единицами и десятичной запятой
    /// системы: английский интерфейс показывал «1,6 GB», русский — «574 MB».
    /// Единицы десятичные, как в каталоге моделей: модель «574 МБ» должна
    /// и здесь быть 574, а не 547.
    /// </para>
    /// </remarks>
    public string Size(long bytes) => bytes switch
    {
        >= 1_000_000_000 => string.Format(Culture, UnitGigabytes, (bytes / 1e9).ToString("F1", Culture)),
        >= 1_000_000 => string.Format(Culture, CallsMegabytes, Math.Round(bytes / 1e6).ToString("F0", Culture)),
        _ => string.Format(Culture, UnitKilobytes, Math.Round(Math.Max(bytes, 0) / 1e3).ToString("F0", Culture)),
    };

    /// <summary>
    /// Дата на языке интерфейса, а не Windows.
    /// </summary>
    /// <remarks>
    /// Раньше даты брали язык системы: английский интерфейс показывал
    /// «Yesterday» над «21 сентября». Формат — день и месяц словом, год —
    /// только если не текущий.
    /// </remarks>
    public string Date(DateTimeOffset at, bool withYear = false, bool withTime = false)
    {
        DateTime local = at.LocalDateTime;
        string format = withYear || local.Year != DateTime.Today.Year ? "d MMMM yyyy" : "d MMMM";
        return local.ToString(withTime ? format + ", HH:mm" : format, Culture);
    }

    /// <summary>Время суток — 24-часовое на обоих языках.</summary>
    public string Time(DateTimeOffset at) => at.LocalDateTime.ToString("HH:mm", Culture);

    /// <summary>Культура для чисел и дат — по языку интерфейса.</summary>
    public required System.Globalization.CultureInfo Formatting { get; init; }

    /// <summary>Подписи для готового транскрипта звонка.</summary>
    public CallTranscriptLabels TranscriptLabels => new(
        TranscriptStartedAt,
        TranscriptDuration,
        TranscriptTrigger,
        TranscriptParticipants,
        TranscriptVoicesSplit,
        TranscriptVoicesHinted,
        TranscriptVoicesGuessed,
        TranscriptUnknownSpeaker,
        TranscriptVoice,
        TranscriptBleedRemoved,
        TranscriptBleedByText,
        TranscriptBleedByEnergy,
        TranscriptNothingRecognized);

    private System.Globalization.CultureInfo Culture => Formatting;

    public static UiStrings English { get; } = new()
    {
        Formatting = System.Globalization.CultureInfo.GetCultureInfo("en-GB"),
        TrayStart = "Start dictation",
        TrayStop = "Stop dictation",
        TrayCancel = "Cancel",
        TrayTranscribing = "Transcribing…",
        TrayLoadingModel = "Loading model…",
        TrayRetryHotkey = "Claim the hotkey again",
        TraySettings = "Settings…",
        TrayExit = "Exit",
        TrayStartRecording = "Record a call",
        TrayStopRecording = "Stop recording",
        StatusRecordingCall = "Recording a call · {0}",
        StatusTranscribingCall = "Transcribing the call…",
        StatusCallSaved = "Call saved: {0}",
        NotifyCallReadyTitle = "Transcript ready",
        NotifyCallReadyBody = "Click to open the call.",
        NotifyCallNamesTitle = "Name the voices",
        NotifyCallNamesBody = "Voices on the call: {0}. Click to hear each and pick names.",
        StatusTranscribingCallProgress = "{0} {1}%",
        PillTranscribingCall = "Transcribing the call · {0}%",
        PillCallReady = "✓ Transcript ready",
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
        StatusTextReady = "Inserted · {0} words",
        StatusSettingsNotSaved = "Settings could not be saved — see the log",
        NotifyNoModelTitle = "No recognition model",
        NotifyNoModelBody = "Open Settings › Models to download one. Nothing works until you do.",
        NotifyHotkeyBusyTitle = "Hotkey unavailable",
        NotifyHotkeyBusyHint = "Free it, then choose \"Claim the hotkey again\" from the menu.",
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
        PillRecordingCall = "Call · {0}",
        PillHintStop = "{0} to stop",
        PillHintCancel = "Esc to cancel",
        PillStopCall = "Stop recording the call",

        SettingsTitle = "Tapybara settings",
        SectionDictation = "Dictation",
        SectionModels = "Models",
        SectionCalls = "Calls",
        SectionGeneral = "General",
        SectionAbout = "About",

        GroupHotkey = "Hotkey",
        GroupAudioInput = "Microphone",
        GroupInsertion = "Where the text goes",
        GroupLimits = "Safety limits",
        GroupSpeechDetection = "Speech detection",
        GroupModelsFolder = "Where models are kept",
        GroupParagraphs = "Paragraphs",
        GroupReplacements = "Replacements",
        GroupRecording = "Recording",
        GroupAppearance = "Appearance",
        GroupPrivacy = "Privacy",

        FieldDecoding = "Decoding",
        FieldDecodingHint = "How many wordings the model weighs before settling on one. Five by default",
        DecodingFast = "Fast — 1 wording",
        DecodingAccurate = "Accurate — 5 wordings",
        DecodingThorough = "Thorough — 8 wordings",
        FieldRecognitionLanguage = "Recognition language",
        FieldRecognitionLanguageHint = "Detect automatically unless you always dictate in one language — a wrong language does not degrade recognition, it destroys it",
        FieldPrompt = "Prompt",
        FieldPromptHint = "Biases vocabulary and anchors punctuation style. Keep it short and typical of what you dictate — an unrelated prompt makes recognition worse and slower.",
        FieldHotkey = "Hotkey",
        FieldHotkeyHint = "Press it to start, press it again to stop. Escape cancels.",
        FieldHotkeyCapturing = "Press a combination…",
        FieldHotkeyCaptureHint = "Esc to cancel. Win combinations are reserved by Windows.",
        FieldHotkeyTaken = "{0} is already taken by another application",
        FieldHotkeyDuplicate = "{0} is already Tapybara's other hotkey",
        FieldCallHotkeyHint = "Press it to start recording a call, press it again to stop.",
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
        FieldUiLanguage = "Interface language",
        FieldUiLanguageAuto = "Same as Windows",
        FieldTheme = "Theme",
        ThemeSystem = "Same as Windows",
        ThemeLight = "Light",
        ThemeDark = "Dark",
        FieldModelsFolder = "Models folder",
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
        FieldOtherSideLanguage = "Other side language",
        FieldOtherSideLanguageHint = "Detect automatically is the sensible default: you know what you speak, you do not know what they will.",
        FieldAutoStart = "Start with Windows",
        FieldAutoStartHint = "Tapybara starts in the tray when you sign in",
        FieldAutoStartUnavailable = "Unavailable when running through the .NET CLI",
        FieldTrayPreview = "Show the start of dictations in the tray",
        FieldTrayPreviewHint = "Off by default: the tray tooltip is the one place dictated text stays on screen, and people dictate passwords and private messages.",

        ModelsIntro = "Tapybara needs a Whisper model to recognise speech. Nothing is bundled — models are hundreds of megabytes and the right one depends on your machine. Download one here; it goes straight into your models folder.",
        ModelsFullListLink = "Browse every model on Hugging Face",
        ModelsRecognitionHeader = "Recognition models",
        ModelsDetectorHeader = "Speech detector",
        TierBest = "Best quality",
        TierRecommended = "Recommended",
        TierCompact = "Smaller",
        TierMinimal = "Fastest, least accurate",
        ButtonDownload = "Download",
        ButtonCancelDownload = "Cancel",
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
        TranscriptVoicesSplit = "Voices told apart",
        TranscriptVoicesHinted = "using the participant list",
        TranscriptVoicesGuessed = "without a participant list",
        TranscriptUnknownSpeaker = "Speaker",
        TranscriptVoice = "Voice",
        ParticipantsEntryPlaceholder = "add / find",
        ParticipantsEntryHint = "Type a name and press Enter, or filter the ones you already use",
        ParticipantsMeHint = "You are always on the call — that is your microphone track",
        ParticipantsAddHint = "Add to this call",
        ParticipantsRemoveHint = "Remove from this call",
        CallReviewTitle = "Call recorded",
        CallReviewNotePlaceholder = "Saved next to the recording as note.md",
        CallReviewDelete = "Delete recording",
        CallReviewSave = "Done",
        CallReviewHintOne = "One person: no voice splitting needed, and the name goes straight into the transcript",
        CallDeleteTitle = "Delete this recording?",
        CallDeleteBody = "Both tracks, the transcript and the note go with it. This cannot be undone.",
        CallDeleteConfirm = "Delete recording",
        UnitSeconds = "{0} s",
        UnitMinutes = "{0} min",
        UnitHoursMinutes = "{0} h {1} min",
        CallsEmpty = "No recordings yet. Start one from the tray menu, and it will appear here.",
        CallsNothingSelected = "Pick a recording on the left.",
        CallsOpenFolder = "Folder",
        CallsTranscribe = "Transcribe",
        CallsTranscribeAgain = "Transcribe again",
        CallsTotals = "Recordings: {0} · {1}",
        CallsMegabytes = "{0} MB",
        CallStateRecording = "recording",
        CallStateTranscribing = "transcribing",
        CallStateReady = "ready",
        CallStateNeedsNames = "name the voices",
        UnitHours = "{0} h",
        UnitGigabytes = "{0} GB",
        UnitKilobytes = "{0} KB",
        CallUntitled = "Call",
        VoiceChange = "Change",
        VoicesResplitMenu = "Split the voices again…",
        FieldRememberVoices = "Remember voices",
        GroupListening = "Listening",
        FieldPlayingProgress = "Show what is playing",
        FieldPlayingProgressHint = "The playing line fills as it sounds.",
        FieldRememberVoicesHint = "When you name a voice, its voiceprint is kept on this computer, and on later calls Tapybara suggests who is speaking. A voiceprint is biometric data; it never leaves this computer.",
        ButtonForgetVoices = "Forget all voices",
        ForgetVoicesTitle = "Forget all voices?",
        ForgetVoicesBody = "Every remembered voiceprint will be deleted. Names stay; Tapybara will simply stop suggesting who is speaking until you name voices again.",
        VoiceSuggestion = "Sounds like {0} · {1}%",
        VoiceAcceptSuggestion = "That's right",
        BannerAcceptSuggestions = "Accept suggestions",
        BannerSuggested = "Recognised from earlier calls: {0}. Check and accept, or pick names yourself.",
        DictionaryVoiceKnown = "voice remembered",
        DictionaryForgetVoice = "Forget voice",
        WelcomeTitle = "Welcome to Tapybara",
        WelcomeStepModel = "Model",
        WelcomeStepMicrophone = "Microphone",
        WelcomeStepTry = "Try it",
        WelcomeModelHeading = "Download a recognition model",
        WelcomeModelBody = "Everything is recognised on this computer. The model is the one thing to download.",
        WelcomeModelDetector = "The speech detector (under 1 MB) comes with it.",
        WelcomeModelHave = "I already have a model…",
        WelcomeMicHeading = "Check the microphone",
        WelcomeMicBody = "Say something. The bar should move when you speak and stay low when you are silent.",
        WelcomeTryHeading = "Dictate your first sentence",
        WelcomeTryBody = "Click into the field below, press {0}, say something, and press {0} again. The text will appear here — and anywhere else you type.",
        WelcomeTryPlaceholder = "Your words will appear here",
        WelcomeTryDone = "It works. Tapybara lives in the tray from now on; click its icon to open this window again.",
        ButtonNext = "Next",
        ButtonBack = "Back",
        ButtonFinish = "Start using Tapybara",
        ButtonSkip = "Skip",
        NavDictations = "Dictations",
        NavCalls = "Calls",
        NavDictionary = "Dictionary",
        NavSettings = "Settings",
        TrayOpen = "Open Tapybara",
        DictationsSearch = "Search dictations",
        DictationsEmpty = "Everything you dictate appears here, so a dictation that went into the wrong window is never lost. Press {0} in any text field and speak.",
        DictationsHistoryOff = "History is switched off, so new dictations are not kept. Turn it on in Settings › General.",
        DictationsCount = "{0} dictations",
        DictationCopy = "Copy",
        DictationDelete = "Delete",
        DictationWords = "{0} words",
        DictionaryIntro = "What to do with the words the model keeps mishearing. Changes apply to new dictations and transcripts.",
        DictionaryReplacementsHint = "On the left, how the model hears it — one word can have several variants, press Enter after each. On the right, how it should be. Punctuation works too, like C# or .NET.",
        DictionaryPeople = "People",
        DictionaryPeopleHint = "Offered as chips after a call and when naming voices, most recent first. Renaming here does not change past transcripts.",
        DictionaryPeopleEmpty = "Names appear here after your first call.",
        DictionaryHeardPlaceholder = "heard as…",
        DictionaryCorrectPlaceholder = "correct",
        DictionaryExport = "Export…",
        DictionaryImport = "Import…",
        DictionaryFileFilter = "Tapybara dictionary|*.json",
        DictionaryFileName = "Tapybara dictionary",
        DictionaryExported = "Saved to {0}. Import this file on another computer to bring the dictionary across.",
        DictionaryImported = "Loaded. Replacements: {0} new, {1} changed. People: {2} new.",
        DictionaryImportedVoices = "Voices added for {0}.",
        DictionaryImportedPrompt = "The prompt was taken from the file.",
        DictionaryImportedNothing = "Nothing new in this file — everything in it is already here.",
        DictionaryVoicesOtherModel = "Voices were not loaded: they were made by a different voice model.",
        DictionaryVoicesOff = "Voices were not loaded: remembering voices is off in settings.",
        DictionaryFileFailed = "Could not use the file: {0}",
        DictionaryNotADictionary = "This is not a Tapybara dictionary file.",
        DictionarySearch = "Search replacements",
        DictionaryAnotherVariant = "another…",
        DictionaryNoMatches = "Nothing found.",
        CardHeading = "Call recorded · {0}",
        CardTitleField = "Name this call",
        CardTitlePlaceholder = "For example: release with Kirill",
        CardWho = "Who was there besides you",
        CardHintNone = "You can skip this: the voices will be told apart anyway, and you can name them from quotes.",
        CardHintMany = "{0} people: I'll look for that many voices, then show quotes so you can tell who is who.",
        CardOpenCall = "Open call",
        CardReady = "Transcript ready",
        CardNeedsNames = "Transcript ready — name the voices",
        CardWaiting = "Waiting for the previous call…",
        SectionAdvanced = "Advanced",
        GroupWhoSpeaks = "Who is speaking",
        GroupStorage = "Storage",
        GroupRecognition = "Recognition",
        GroupStartup = "Startup",
        FieldKeepHistory = "Keep dictation history",
        FieldKeepHistoryHint = "Everything you dictate is kept on this computer, in Tapybara › Dictations. Turn it off if you dictate passwords.",
        ButtonClearHistory = "Clear history",
        ClearHistoryTitle = "Clear dictation history?",
        ClearHistoryBody = "Every saved dictation will be deleted from this computer. This cannot be undone.",
        AboutDataFolder = "Settings and data",
        AdvancedIntro = "Sensible values are already set. Change these when something specific is wrong.",
        CallsSearchPlaceholder = "Search calls and transcripts",
        DayToday = "Today",
        DayYesterday = "Yesterday",
        CallsListen = "Listen",
        CallsStopListening = "Stop",
        CallsCopyText = "Copy text",
        CallsMore = "More",
        CallsOpenTranscriptFile = "Open transcript.md",
        CallsTabTranscript = "Transcript",
        CallsTabNote = "Note",
        BannerNeedsNamesOne = "One voice has no name yet. Its quotes are on the right; the name goes into all its lines at once.",
        BannerNeedsNamesMany = "Voices without a name: {0}. Their quotes are on the right; a name goes into all of that voice's lines at once.",
        BannerLegacy = "This call was transcribed by an older version, which did not keep who said what. Transcribe it again to name the voices.",
        BannerNotTranscribed = "Not transcribed yet.",
        BannerTranscribing = "Transcribing… {0}%",
        BannerRecording = "The recording is still going.",
        BannerDamaged = "There is no audio in this recording.",
        PeopleHeader = "People · {0}",
        VoiceItsMe = "That's me",
        VoiceDetach = "Detach",
        VoiceWho = "Who is this?",
        VoiceOtherName = "other…",
        VoicePlayQuote = "Listen to this quote",
        DictationNoModel = "Dictation needs a recognition model, and none is downloaded yet.",
        DictationDownloadModel = "Download a model",
        DictationOpenModels = "Open Models",
        UpdatesTitle = "Updates",
        UpdatesManual = "This copy was unpacked from a zip, so it updates by hand: download the new release and replace the folder. The installer on the same page keeps itself up to date.",
        UpdatesReleases = "Releases",
        UpdatesIdle = "Not checked yet.",
        UpdatesChecking = "Checking…",
        UpdatesUpToDate = "Version {0} is the latest.",
        UpdatesDownloading = "Downloading version {0} — {1}%",
        UpdatesReady = "Version {0} is downloaded and installs the next time Tapybara starts.",
        UpdatesFailed = "Could not check: {0}",
        UpdatesCheckNow = "Check now",
        UpdatesRestart = "Restart now",
        UpdatesBusy = "Not now: something is being recorded or transcribed. The update installs the next time Tapybara starts.",
        FieldAutoUpdate = "Check for updates automatically",
        FieldAutoUpdateHint = "At start and every few hours, against the releases on GitHub. The request asks for the list of releases and nothing else. Downloads happen in the background; Tapybara never restarts on its own.",
        TrayUpdateReady = "Restart to update to {0}",
        FieldPortableInstalled = "Not available in the installed version: an update replaces the program folder, and anything kept beside the program would go with it. For a portable copy, use the zip from the releases page.",
        AboutRuntimeNotLoaded = "not loaded yet",
        VoiceNotThis = "Not this person",
        VoiceSomeoneElse = "Someone else",
        VoiceSomeoneElseSplit = "Someone else — split the voices",
        VoicesMoreTitle = "Someone else was probably on the call",
        VoicesMoreHint = "About {0} on the other side doesn't sound like any voice found here. Splitting again, with one more voice, usually sorts it out.",
        VoicesSplitInto = "Split into {0} voices",
        VoiceRejectedHint = "Two quotes from here turned out to be someone else. This may be two people in one voice.",
        LineDoubtful = "Not sure this is the right person: the voice doesn't match well. Click to choose who said it.",
        ModelsSegmentationHeader = "Voice splitting",
        ModelsEmbeddingHeader = "Voiceprints",
        ModelsForCalls = "for calls",
        ModelsCustom = "your file",
        VoicesOpenModels = "Get the model",
        VoiceSomeoneElseGetModel = "Someone else — get the model to split voices…",
        VoicesResplitCount = "Voices to look for",
        VoicesResplitGo = "Split again",
        ModelsMissingList = "Not installed yet: {0}. The button downloads the recommended one.",
        VoicesNeedModels = "Splitting needs: {0}.",
        CardModelsMissing = "Telling these voices apart needs models that are not installed yet: {0}.",
        LineFixWord = "Fix this word…",
        LineEditText = "Edit the line",
        LineFixSelection = "Fix “{0}”…",
        CallsStopTranscribing = "Stop",
        PersonRenameEverywhere = "Rename everywhere…",
        RenameTitle = "Rename {0}",
        RenameBody = "Calls, the voice book and the list of people follow the new name. Nothing is transcribed again.",
        RenameInCalls = "In calls: {0}",
        RenameMentions = "Mentions in the text",
        RenameGo = "Rename",
        CallsCopyAnonymized = "Copy without names…",
        AnonymizeTitle = "Copy without names",
        AnonymizeBody = "Only the copy changes: the call keeps the real names.",
        AnonymizeMentions = "Replace mentions in the text too",
        AnonymizeNumbered = "Everyone — “Participant N”",
        AnonymizeParticipant = "Participant {0}",
        AnonymizeCopy = "Copy",
        FixSimilar = "Similar in this call",
        FixRemember = "Remember in the dictionary",
        FixReplaceAll = "Replace: {0}",
        FixOnlyHere = "Only here",
        RetranscribeEditedTitle = "Transcribe again?",
        RetranscribeEditedBody = "The transcript has edits made by hand. Transcribing again rebuilds the text from the recording, and those edits will be lost. Words saved to the dictionary will be fixed again.",
        DictionaryApplyToCalls = "Apply to past calls",
        DictionaryApplyTitle = "Apply the dictionary to past calls?",
        DictionaryApplyBody = "Places to correct: {0}, in {1} calls. The text is corrected as it is; nothing is transcribed again.",
        DictionaryApplyGo = "Apply",
        DictionaryApplyNothing = "Past calls already match the dictionary.",
        DictionaryApplied = "Corrected {0} places in {1} calls.",
        RecordCall = "Record a call",
        RecordDictate = "Dictate",
        RecordStop = "Stop",
        RecordTranscribing = "Transcribing…",
        DictateHint = "Dictate here: the text lands in this list and in the clipboard. To type straight into another app, put the caret there and press the hotkey.",
        VoiceMe = "your microphone",
        VoiceNamePlaceholder = "Name",
        LineSaidBy = "Said by",
        LinePlayFrom = "Listen from {0}",
        LineCopy = "Copy",
        LineYourMicrophone = "Your microphone",
        ReplacementTitle = "Add a replacement",
        ButtonAdd = "Add",
        CallStateNotTranscribed = "not transcribed",
        CallStateDamaged = "no audio",
        TranscriptBleedRemoved = "Other-side speech removed from your channel",
        TranscriptBleedByText = "by text",
        TranscriptBleedByEnergy = "by loudness",
        TranscriptNothingRecognized = "_No speech recognised._",
        CallStageReading = "Reading the tracks…",
        CallStageMicrophone = "Transcribing your channel…",
        CallStageOtherSide = "Transcribing the other side…",
        CallStageFiltering = "Filtering out bleed…",
        CallStageSplittingVoices = "Telling the voices apart…",
        GroupVoices = "Telling voices apart",
        FieldSplitVoices = "Tell the other side's voices apart",
        FieldSplitVoicesHint = "Only the other channel: yours is your own microphone. Adds minutes to a long call",
        FieldVoiceThreshold = "How different voices must be",
        FieldVoiceThresholdHint = "Only used when you have not named the participants. Naming them is far more accurate",

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
        AboutSite = "Project",
        AboutSiteHint = "Documentation, releases and the source",
        AboutSource = "Source on GitHub",
        ButtonCopyDiagnostics = "Copy diagnostics",
        DiagnosticsCopied = "Copied",
    };

    public static UiStrings Russian { get; } = new()
    {
        Formatting = System.Globalization.CultureInfo.GetCultureInfo("ru-RU"),
        TrayStart = "Начать диктовку",
        TrayStop = "Закончить диктовку",
        TrayCancel = "Отменить",
        TrayTranscribing = "Распознаю…",
        TrayLoadingModel = "Загружаю модель…",
        TrayRetryHotkey = "Занять горячую клавишу заново",
        TraySettings = "Настройки…",
        TrayExit = "Выход",
        TrayStartRecording = "Записать звонок",
        TrayStopRecording = "Остановить запись",
        StatusRecordingCall = "Записываю звонок · {0}",
        StatusTranscribingCall = "Распознаю звонок…",
        StatusCallSaved = "Звонок сохранён: {0}",
        NotifyCallReadyTitle = "Транскрипт готов",
        NotifyCallReadyBody = "Нажмите, чтобы открыть звонок.",
        NotifyCallNamesTitle = "Назовите голоса",
        NotifyCallNamesBody = "Голосов на звонке: {0}. Нажмите, чтобы послушать каждый и выбрать имена.",
        StatusTranscribingCallProgress = "{0} {1}%",
        PillTranscribingCall = "Распознаю звонок · {0}%",
        PillCallReady = "✓ Транскрипт готов",
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
        StatusTextReady = "Вставлено · слов: {0}",
        StatusSettingsNotSaved = "Настройки не сохранились — смотрите журнал",
        NotifyNoModelTitle = "Нет модели распознавания",
        NotifyNoModelBody = "Откройте «Настройки › Модели» и скачайте её. Без модели ничего не работает.",
        NotifyHotkeyBusyTitle = "Горячая клавиша недоступна",
        NotifyHotkeyBusyHint = "Освободите её и выберите в меню «Занять горячую клавишу заново».",
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
        PillRecordingCall = "Звонок · {0}",
        PillHintStop = "{0} — закончить",
        PillHintCancel = "Esc — отменить",
        PillStopCall = "Остановить запись звонка",

        SettingsTitle = "Настройки Tapybara",
        SectionDictation = "Диктовка",
        SectionModels = "Модели",
        SectionCalls = "Звонки",
        SectionGeneral = "Общие",
        SectionAbout = "О программе",

        GroupHotkey = "Горячая клавиша",
        GroupAudioInput = "Микрофон",
        GroupInsertion = "Куда попадает текст",
        GroupLimits = "Предохранители",
        GroupSpeechDetection = "Поиск речи",
        GroupModelsFolder = "Где лежат модели",
        GroupParagraphs = "Абзацы",
        GroupReplacements = "Замены",
        GroupRecording = "Запись",
        GroupAppearance = "Внешний вид",
        GroupPrivacy = "Приватность",

        FieldDecoding = "Декодирование",
        FieldDecodingHint = "Сколько вариантов фразы модель взвешивает, прежде чем выбрать один. По умолчанию пять",
        DecodingFast = "Быстро — 1 вариант",
        DecodingAccurate = "Точно — 5 вариантов",
        DecodingThorough = "Тщательно — 8 вариантов",
        FieldRecognitionLanguage = "Язык распознавания",
        FieldRecognitionLanguageHint = "Определять автоматически, если только вы не диктуете всегда на одном языке: неверный язык не ухудшает распознавание, а разрушает его",
        FieldPrompt = "Подсказка",
        FieldPromptHint = "Биасит словарь и задаёт стиль пунктуации. Держите её короткой и похожей на то, что вы диктуете: посторонняя подсказка делает распознавание хуже и медленнее.",
        FieldHotkey = "Горячая клавиша",
        FieldHotkeyHint = "Нажатие начинает диктовку, повторное заканчивает. Escape отменяет.",
        FieldHotkeyCapturing = "Нажмите сочетание…",
        FieldHotkeyCaptureHint = "Esc — отмена. Сочетания с Win зарезервированы Windows.",
        FieldHotkeyTaken = "{0} уже занято другим приложением",
        FieldHotkeyDuplicate = "{0} уже занято второй горячей клавишей Tapybara",
        FieldCallHotkeyHint = "Нажатие начинает запись звонка, повторное — останавливает.",
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
        FieldUiLanguage = "Язык интерфейса",
        FieldUiLanguageAuto = "Как в Windows",
        FieldTheme = "Тема",
        ThemeSystem = "Как в Windows",
        ThemeLight = "Светлая",
        ThemeDark = "Тёмная",
        FieldModelsFolder = "Папка моделей",
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
        FieldOtherSideLanguage = "Язык собеседника",
        FieldOtherSideLanguageHint = "Определять автоматически — разумное умолчание: свой язык вы знаете, а язык собеседника заранее нет.",
        FieldAutoStart = "Запускать с Windows",
        FieldAutoStartHint = "Tapybara появится в трее при входе в систему",
        FieldAutoStartUnavailable = "Недоступно при запуске через .NET CLI",
        FieldTrayPreview = "Показывать начало диктовки в трее",
        FieldTrayPreviewHint = "По умолчанию выключено: подсказка трея — единственное место, где надиктованное надолго остаётся на экране, а диктуют в том числе пароли и переписку.",

        ModelsIntro = "Tapybara нужна модель Whisper, чтобы распознавать речь. В комплект она не входит: модели весят сотни мегабайт, и подходящая зависит от вашей машины. Скачайте её здесь — она сразу попадёт в папку моделей.",
        ModelsFullListLink = "Посмотреть все модели на Hugging Face",
        ModelsRecognitionHeader = "Модели распознавания",
        ModelsDetectorHeader = "Детектор речи",
        TierBest = "Лучшее качество",
        TierRecommended = "Рекомендуется",
        TierCompact = "Компактнее",
        TierMinimal = "Быстрее всего, точность ниже",
        ButtonDownload = "Скачать",
        ButtonCancelDownload = "Отменить",
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
        TranscriptVoicesSplit = "Голоса разделены",
        TranscriptVoicesHinted = "по списку участников",
        TranscriptVoicesGuessed = "без списка участников",
        TranscriptUnknownSpeaker = "Собеседник",
        TranscriptVoice = "Голос",
        ParticipantsEntryPlaceholder = "имя / поиск",
        ParticipantsEntryHint = "Наберите имя и нажмите Enter — или отфильтруйте тех, с кем уже говорили",
        ParticipantsMeHint = "Вы на звонке всегда — это ваша дорожка микрофона",
        ParticipantsAddHint = "Добавить к этому звонку",
        ParticipantsRemoveHint = "Убрать из этого звонка",
        CallReviewTitle = "Звонок записан",
        CallReviewNotePlaceholder = "Сохранится рядом с записью в note.md",
        CallReviewDelete = "Удалить запись",
        CallReviewSave = "Готово",
        CallReviewHintOne = "Один собеседник: голоса разделять не нужно, имя попадёт прямо в транскрипт",
        CallDeleteTitle = "Удалить эту запись?",
        CallDeleteBody = "Вместе с ней уйдут обе дорожки, транскрипт и заметка. Отменить будет нельзя.",
        CallDeleteConfirm = "Удалить запись",
        UnitSeconds = "{0} с",
        UnitMinutes = "{0} мин",
        UnitHoursMinutes = "{0} ч {1} мин",
        CallsEmpty = "Записей пока нет. Начните запись из меню в трее — она появится здесь.",
        CallsNothingSelected = "Выберите запись слева.",
        CallsOpenFolder = "Папка",
        CallsTranscribe = "Распознать",
        CallsTranscribeAgain = "Распознать заново",
        CallsTotals = "Записей: {0} · {1}",
        CallsMegabytes = "{0} МБ",
        CallStateRecording = "пишется",
        CallStateTranscribing = "распознаётся",
        CallStateReady = "готово",
        CallStateNeedsNames = "назовите голоса",
        UnitHours = "{0} ч",
        UnitGigabytes = "{0} ГБ",
        UnitKilobytes = "{0} КБ",
        CallUntitled = "Звонок",
        VoiceChange = "Изменить",
        VoicesResplitMenu = "Разделить голоса заново…",
        FieldRememberVoices = "Запоминать голоса",
        GroupListening = "Прослушивание",
        FieldPlayingProgress = "Показывать, что звучит",
        FieldPlayingProgressHint = "Звучащая реплика заливается по ходу звука.",
        FieldRememberVoicesHint = "Когда вы называете голос, его слепок сохраняется на этом компьютере, и на следующих звонках Tapybara подсказывает, кто говорит. Слепок голоса — биометрия; он не покидает этот компьютер.",
        ButtonForgetVoices = "Забыть все голоса",
        ForgetVoicesTitle = "Забыть все голоса?",
        ForgetVoicesBody = "Все запомненные слепки голосов будут удалены. Имена останутся — просто Tapybara перестанет подсказывать, кто говорит, пока вы снова не назовёте голоса.",
        VoiceSuggestion = "Похоже на: {0} · {1}%",
        VoiceAcceptSuggestion = "Верно",
        BannerAcceptSuggestions = "Принять подсказки",
        BannerSuggested = "Узнаны по прошлым звонкам: {0}. Проверьте и примите — или выберите имена сами.",
        DictionaryVoiceKnown = "голос запомнен",
        DictionaryForgetVoice = "Забыть голос",
        WelcomeTitle = "Добро пожаловать в Tapybara",
        WelcomeStepModel = "Модель",
        WelcomeStepMicrophone = "Микрофон",
        WelcomeStepTry = "Проба",
        WelcomeModelHeading = "Скачайте модель распознавания",
        WelcomeModelBody = "Всё распознаётся на этом компьютере. Модель — единственное, что нужно скачать.",
        WelcomeModelDetector = "Детектор речи (меньше 1 МБ) скачается вместе с ней.",
        WelcomeModelHave = "У меня уже есть модель…",
        WelcomeMicHeading = "Проверьте микрофон",
        WelcomeMicBody = "Скажите что-нибудь. Полоска должна двигаться, когда вы говорите, и замирать, когда молчите.",
        WelcomeTryHeading = "Надиктуйте первую фразу",
        WelcomeTryBody = "Щёлкните в поле ниже, нажмите {0}, скажите что-нибудь и нажмите {0} ещё раз. Текст появится здесь — и в любом другом поле, где вы печатаете.",
        WelcomeTryPlaceholder = "Здесь появятся ваши слова",
        WelcomeTryDone = "Работает. Дальше Tapybara живёт в трее; щёлкните по значку, чтобы открыть окно.",
        ButtonNext = "Далее",
        ButtonBack = "Назад",
        ButtonFinish = "Начать",
        ButtonSkip = "Пропустить",
        NavDictations = "Диктовки",
        NavCalls = "Звонки",
        NavDictionary = "Словарь",
        NavSettings = "Настройки",
        TrayOpen = "Открыть Tapybara",
        DictationsSearch = "Поиск по диктовкам",
        DictationsEmpty = "Здесь появится всё, что вы надиктуете, — диктовка, ушедшая не в то окно, не потеряется. Нажмите {0} в любом поле ввода и говорите.",
        DictationsHistoryOff = "История выключена, новые диктовки не сохраняются. Включить её можно в «Настройки › Общие».",
        DictationsCount = "Диктовок: {0}",
        DictationCopy = "Копировать",
        DictationDelete = "Удалить",
        DictationWords = "слов: {0}",
        DictionaryIntro = "Что делать со словами, которые модель слышит не так. Правки действуют на новые диктовки и распознавания.",
        DictionaryReplacementsHint = "Слева — как модель слышит; у одного слова может быть несколько вариантов, Enter после каждого. Справа — как должно быть. Работает и для названий со знаками — C# или .NET.",
        DictionaryPeople = "Люди",
        DictionaryPeopleHint = "Предлагаются чипами после звонка и при именовании голосов, свежие — первыми. Переименование здесь не меняет прошлые транскрипты.",
        DictionaryPeopleEmpty = "Имена появятся здесь после первого звонка.",
        DictionaryHeardPlaceholder = "как услышано…",
        DictionaryCorrectPlaceholder = "правильно",
        DictionaryExport = "Выгрузить…",
        DictionaryImport = "Загрузить…",
        DictionaryFileFilter = "Словарь Tapybara|*.json",
        DictionaryFileName = "Словарь Tapybara",
        DictionaryExported = "Сохранено в {0}. Загрузите этот файл на другом компьютере — словарь переедет целиком.",
        DictionaryImported = "Загружено. Замены: новых {0}, изменено {1}. Люди: новых {2}.",
        DictionaryImportedVoices = "Голоса добавлены: {0}.",
        DictionaryImportedPrompt = "Подсказка взята из файла.",
        DictionaryImportedNothing = "В файле нет ничего нового — всё уже есть.",
        DictionaryVoicesOtherModel = "Голоса не загружены: их сняла другая модель голосов.",
        DictionaryVoicesOff = "Голоса не загружены: запоминание голосов выключено в настройках.",
        DictionaryFileFailed = "Не удалось работать с файлом: {0}",
        DictionaryNotADictionary = "Это не файл словаря Tapybara.",
        DictionarySearch = "Поиск по заменам",
        DictionaryAnotherVariant = "ещё…",
        DictionaryNoMatches = "Ничего не найдено.",
        CardHeading = "Звонок записан · {0}",
        CardTitleField = "Как назвать",
        CardTitlePlaceholder = "Например: релиз с Кириллом",
        CardWho = "Кто был, кроме вас",
        CardHintNone = "Можно не отмечать: голоса разделятся и так, а назвать их можно будет по цитатам.",
        CardHintMany = "Человек: {0}. Поищу столько голосов, а потом покажу цитаты — выберете, кто есть кто.",
        CardOpenCall = "Открыть звонок",
        CardReady = "Транскрипт готов",
        CardNeedsNames = "Транскрипт готов — назовите голоса",
        CardWaiting = "Жду, пока распознается предыдущий звонок…",
        SectionAdvanced = "Дополнительно",
        GroupWhoSpeaks = "Кто говорит",
        GroupStorage = "Хранение",
        GroupRecognition = "Распознавание",
        GroupStartup = "Запуск",
        FieldKeepHistory = "Хранить историю диктовок",
        FieldKeepHistoryHint = "Всё надиктованное хранится на этом компьютере, в Tapybara › Диктовки. Выключите, если диктуете пароли.",
        ButtonClearHistory = "Очистить историю",
        ClearHistoryTitle = "Очистить историю диктовок?",
        ClearHistoryBody = "Все сохранённые диктовки будут удалены с этого компьютера. Отменить будет нельзя.",
        AboutDataFolder = "Настройки и данные",
        AdvancedIntro = "Разумные значения уже выставлены. Меняйте, когда что-то конкретное работает не так.",
        CallsSearchPlaceholder = "Поиск по звонкам и тексту",
        DayToday = "Сегодня",
        DayYesterday = "Вчера",
        CallsListen = "Слушать",
        CallsStopListening = "Остановить",
        CallsCopyText = "Копировать текст",
        CallsMore = "Ещё",
        CallsOpenTranscriptFile = "Открыть transcript.md",
        CallsTabTranscript = "Транскрипт",
        CallsTabNote = "Заметка",
        BannerNeedsNamesOne = "Один голос без имени. Справа его цитаты — имя подставится во все его реплики сразу.",
        BannerNeedsNamesMany = "Голосов без имени: {0}. Справа их цитаты — имя подставится во все реплики голоса сразу.",
        BannerLegacy = "Этот звонок распознан прежней версией, которая не сохраняла, кто что сказал. Распознайте его заново, чтобы назвать голоса.",
        BannerNotTranscribed = "Звонок ещё не распознан.",
        BannerTranscribing = "Распознаю… {0}%",
        BannerRecording = "Запись ещё идёт.",
        BannerDamaged = "В этой записи нет звука.",
        PeopleHeader = "Участники · {0}",
        VoiceItsMe = "Это я",
        VoiceDetach = "Отделить",
        VoiceWho = "Кто это?",
        VoiceOtherName = "другое…",
        VoicePlayQuote = "Послушать цитату",
        DictationNoModel = "Для диктовки нужна модель распознавания, а она ещё не скачана.",
        DictationDownloadModel = "Скачать модель",
        DictationOpenModels = "Открыть модели",
        UpdatesTitle = "Обновления",
        UpdatesManual = "Эта копия распакована из zip и обновляется руками: скачайте новый релиз и замените папку. Установщик с той же страницы обновляется сам.",
        UpdatesReleases = "Релизы",
        UpdatesIdle = "Ещё не проверялось.",
        UpdatesChecking = "Проверяю…",
        UpdatesUpToDate = "Версия {0} — последняя.",
        UpdatesDownloading = "Скачиваю версию {0} — {1}%",
        UpdatesReady = "Версия {0} скачана и установится при следующем запуске Tapybara.",
        UpdatesFailed = "Не удалось проверить: {0}",
        UpdatesCheckNow = "Проверить",
        UpdatesRestart = "Перезапустить",
        UpdatesBusy = "Не сейчас: идёт запись или распознавание. Обновление установится при следующем запуске Tapybara.",
        FieldAutoUpdate = "Проверять обновления автоматически",
        FieldAutoUpdateHint = "При запуске и раз в несколько часов, по релизам на GitHub. Запрос — только список релизов, больше ничего. Скачивание идёт в фоне; сама Tapybara не перезапускается.",
        TrayUpdateReady = "Перезапустить и обновить до {0}",
        FieldPortableInstalled = "Недоступно в установленной версии: обновление заменяет папку программы, и всё, что лежит рядом с ней, пропало бы. Для портативной копии возьмите zip со страницы релизов.",
        AboutRuntimeNotLoaded = "ещё не загружен",
        VoiceNotThis = "Не этот человек",
        VoiceSomeoneElse = "Кто-то другой",
        VoiceSomeoneElseSplit = "Кто-то другой — разделить голоса",
        VoicesMoreTitle = "Похоже, на звонке был ещё кто-то",
        VoicesMoreHint = "Около {0} речи на той стороне не похожи ни на один найденный голос. Обычно помогает разделить заново, на один голос больше.",
        VoicesSplitInto = "Разделить заново — голосов: {0}",
        VoiceRejectedHint = "Две цитаты отсюда оказались чужими. Возможно, в этом голосе два человека.",
        LineDoubtful = "Не уверен, что это тот человек: голос не очень похож. Нажмите, чтобы выбрать, кто это сказал.",
        ModelsSegmentationHeader = "Разделение голосов",
        ModelsEmbeddingHeader = "Слепки голоса",
        ModelsForCalls = "для звонков",
        ModelsCustom = "свой файл",
        VoicesOpenModels = "Скачать модель",
        VoiceSomeoneElseGetModel = "Кто-то другой — скачать модель для разделения…",
        VoicesResplitCount = "Сколько голосов искать",
        VoicesResplitGo = "Разделить заново",
        ModelsMissingList = "Ещё не скачано: {0}. Кнопка скачает рекомендованную модель.",
        VoicesNeedModels = "Чтобы разделить, нужно: {0}.",
        CardModelsMissing = "Чтобы различить эти голоса, нужны модели, которые ещё не скачаны: {0}.",
        LineFixWord = "Исправить слово…",
        LineEditText = "Исправить реплику",
        LineFixSelection = "Исправить «{0}»…",
        CallsStopTranscribing = "Остановить",
        PersonRenameEverywhere = "Переименовать везде…",
        RenameTitle = "Переименовать: {0}",
        RenameBody = "Звонки, книга голосов и список людей перейдут на новое имя. Заново ничего не распознаётся.",
        RenameInCalls = "В звонках: {0}",
        RenameMentions = "Упоминания в тексте",
        RenameGo = "Переименовать",
        CallsCopyAnonymized = "Копировать без имён…",
        AnonymizeTitle = "Копия без имён",
        AnonymizeBody = "Меняется только копия — в звонке остаются настоящие имена.",
        AnonymizeMentions = "Заменить и упоминания в тексте",
        AnonymizeNumbered = "Все — «Участник N»",
        AnonymizeParticipant = "Участник {0}",
        AnonymizeCopy = "Копировать",
        FixSimilar = "Похожие в этом звонке",
        FixRemember = "Запомнить в словаре",
        FixReplaceAll = "Заменить: {0}",
        FixOnlyHere = "Только здесь",
        RetranscribeEditedTitle = "Распознать заново?",
        RetranscribeEditedBody = "В транскрипте есть ручные правки. Повторное распознавание соберёт текст заново из записи, и они пропадут. Слова, сохранённые в словаре, исправятся снова.",
        DictionaryApplyToCalls = "Применить к прошлым звонкам",
        DictionaryApplyTitle = "Применить словарь к прошлым звонкам?",
        DictionaryApplyBody = "Исправится мест: {0}, звонков: {1}. Правится готовый текст, заново ничего не распознаётся.",
        DictionaryApplyGo = "Применить",
        DictionaryApplyNothing = "Прошлые звонки уже соответствуют словарю.",
        DictionaryApplied = "Исправлено мест: {0}, звонков: {1}.",
        RecordCall = "Записать звонок",
        RecordDictate = "Диктовать",
        RecordStop = "Стоп",
        RecordTranscribing = "Распознаю…",
        DictateHint = "Диктовка сюда: текст появится в этом списке и в буфере обмена. Чтобы печатать сразу в другое приложение, поставьте туда курсор и нажмите горячую клавишу.",
        VoiceMe = "ваш микрофон",
        VoiceNamePlaceholder = "Имя",
        LineSaidBy = "Эту реплику сказал",
        LinePlayFrom = "Слушать с {0}",
        LineCopy = "Копировать",
        LineYourMicrophone = "Ваш микрофон",
        ReplacementTitle = "Добавить замену",
        ButtonAdd = "Добавить",
        CallStateNotTranscribed = "не распознано",
        CallStateDamaged = "нет звука",
        TranscriptBleedRemoved = "Отсеяно чужой речи из своего канала",
        TranscriptBleedByText = "по тексту",
        TranscriptBleedByEnergy = "по громкости",
        TranscriptNothingRecognized = "_Речь не распознана._",
        CallStageReading = "Читаю дорожки…",
        CallStageMicrophone = "Распознаю ваш канал…",
        CallStageOtherSide = "Распознаю собеседников…",
        CallStageFiltering = "Отсеиваю чужую речь…",
        CallStageSplittingVoices = "Разделяю голоса…",
        GroupVoices = "Разделение голосов",
        FieldSplitVoices = "Разделять голоса собеседников",
        FieldSplitVoicesHint = "Только чужой канал: свой — это ваш микрофон. На длинном звонке добавляет минуты",
        FieldVoiceThreshold = "Насколько голоса должны различаться",
        FieldVoiceThresholdHint = "Работает, только если участники не названы. Назвать их — заметно точнее",

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
        AboutSite = "Проект",
        AboutSiteHint = "Документация, сборки и исходники",
        AboutSource = "Исходники на GitHub",
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
