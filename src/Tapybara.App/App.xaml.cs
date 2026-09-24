using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Dictation;
using Tapybara.Core.Models;
using Tapybara.Core.Settings;
using Tapybara.Core.Speech;
using Tapybara.Core.Windows;

// WinForms и WPF определяют типы с одинаковыми именами, а ImplicitUsings
// подключает пространства имён обоих. Явные псевдонимы снимают
// неоднозначность один раз на файл — иначе пришлось бы писать полное имя
// при каждом употреблении.
using Application = System.Windows.Application;

namespace Tapybara.App;

/// <summary>
/// Точка сборки приложения: связывает хоткей, контроллер диктовки, трей и оверлей.
/// </summary>
/// <remarks>
/// Здесь же — единственное место, где события из фоновых потоков переводятся
/// в поток интерфейса. Контроллер о WPF не знает и знать не должен.
/// </remarks>
public partial class App : Application, IDisposable
{
    /// <summary>Имя мьютекса, по которому второй экземпляр узнаёт о первом.</summary>
    /// <remarks>
    /// Два экземпляра дрались бы за глобальный хоткей: второй просто не смог бы
    /// его занять, а пользователь получил бы «горячая клавиша занята» от
    /// собственного приложения.
    /// </remarks>
    private const string InstanceMutexName = @"Local\Tapybara.SingleInstance";

    /// <summary>
    /// Событие, которым второй экземпляр будит первый.
    /// </summary>
    /// <remarks>
    /// Без него повторный запуск выглядел как «ничего не произошло»: процесс
    /// молча завершался, и человек, забывший про иконку в трее, решал, что
    /// приложение сломано.
    /// </remarks>
    private const string WakeEventName = @"Local\Tapybara.Wake";

    /// <summary>Виртуальный код клавиши Escape.</summary>
    private const ushort VirtualKeyEscape = 0x1B;

    private Mutex? _instanceMutex;
    private EventWaitHandle? _wakeEvent;
    private RegisteredWaitHandle? _wakeRegistration;

    private SettingsHost _settings = null!;
    private WhisperEngine? _engine;
    private SpeechDetector? _detector;
    private SpeechTranscriber? _transcriber;
    private DictationController? _controller;
    private CallTranscriber? _callTranscriber;

    /// <summary>
    /// Разделитель голосов. Собирается лениво, при первом звонке, которому он нужен.
    /// </summary>
    /// <remarks>
    /// Обе его модели вместе весят тридцать пять мегабайт и держат нативный
    /// контекст. Большинство звонков — один на один, и там он не нужен вовсе:
    /// поднимать его на старте значило бы платить за то, чем обычно не
    /// пользуются.
    /// </remarks>
    private SpeakerDiarizer? _diarizer;

    /// <summary>Настройки, под которые собран текущий разделитель голосов.</summary>
    private (string Segmentation, string Embedding, double Threshold)? _diarizerBuiltFor;
    private HotkeyListener? _hotkey;

    /// <summary>Хоткей записи звонка. Живёт в своём потоке, как и хоткей диктовки.</summary>
    private HotkeyListener? _callHotkey;

    /// <summary>Какое из двух сочетаний занять не удалось — для пункта «занять заново».</summary>
    private bool _hotkeyFailed;
    private bool _callHotkeyFailed;
    private TrayIconHost? _tray;
    private OverlayWindow? _overlay;
    private DispatcherTimer? _elapsedTimer;

    /// <summary>Наблюдение за Escape. Живёт только пока идёт диктовка.</summary>
    private KeyWatcher? _cancelWatcher;

    /// <summary>Окно настроек. Оно одно: второе рассинхронизировалось бы с первым.</summary>
    private SettingsWindow? _settingsWindow;
    /// <summary>Главное окно: диктовки, звонки, словарь. Одно на приложение.</summary>
    private MainWindow? _mainWindow;

    /// <summary>История диктовок на диске.</summary>
    private DictationJournal _journal = null!;

    /// <summary>Книга голосов: как звучат люди, с которыми разговаривали.</summary>
    private VoiceBook _voiceBook = null!;

    /// <summary>
    /// Снятие слепков голоса. Собирается лениво, как и разделитель.
    /// </summary>
    /// <remarks>
    /// Та же модель слепков, что внутри разделителя, но отдельным объектом:
    /// разделитель слепки наружу не отдаёт.
    /// </remarks>
    private VoiceprintExtractor? _voiceprints;

    /// <summary>Модель, под которую собран текущий <see cref="_voiceprints"/>.</summary>
    private string? _voiceprintsBuiltFor;

    /// <summary>Окно первого запуска, пока оно открыто.</summary>
    private WelcomeWindow? _welcomeWindow;

    /// <summary>Открытые карточки «звонок записан» — по папке звонка.</summary>
    private readonly Dictionary<string, CallReviewWindow> _reviewCards = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Папка звонка, который распознаётся прямо сейчас.</summary>
    private string? _transcribingCallDirectory;

    /// <summary>Как далеко распознавание текущего звонка — для пилюли и списка.</summary>
    private CallTranscriptionProgress? _callProgress;

    /// <summary>Отмена распознавания текущего звонка — если его удаляют на ходу.</summary>
    private CancellationTokenSource? _callCancellation;

    /// <summary>
    /// Работа над звонками идёт по одной.
    /// </summary>
    /// <remarks>
    /// Разделитель голосов не потокобезопасен, а распознавание, разделение и
    /// перерисовка правят одни и те же файлы. Звонок, остановленный, пока
    /// распознаётся предыдущий, просто встаёт в очередь.
    /// </remarks>
    private readonly SemaphoreSlim _callGate = new(1, 1);

    /// <summary>Звонки, ждущие своей очереди на распознавание.</summary>
    private readonly HashSet<string> _queuedCalls = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Откладывает возврат пилюли звонка, пока видна вспышка диктовки.</summary>
    private DispatcherTimer? _callOverlayDelay;

    private readonly CallRecorder _callRecorder = new();

    /// <summary>Пересборка движка идёт — второй одновременный запуск запрещён.</summary>
    private readonly SemaphoreSlim _rebuildGate = new(1, 1);

    /// <summary>Следит за папкой моделей: файлы туда кладут и мимо программы.</summary>
    private FileSystemWatcher? _modelsWatcher;

    /// <summary>Гасит очередь событий файловой системы в одно обновление.</summary>
    private DispatcherTimer? _modelsDebounce;

    /// <summary>
    /// Почему движок не собрался. <c>null</c> — всё в порядке.
    /// </summary>
    /// <remarks>
    /// Битую модель Whisper.net принимает молча: <c>FromPath</c> проходит, а
    /// падает уже <c>CreateBuilder</c> — то есть в момент распознавания. Без
    /// этого поля приложение выглядело готовым, принимало диктовку, и человек
    /// узнавал о неисправности, наговорив минуту.
    /// </remarks>
    private string? _engineFailure;

    /// <summary>Что показать на пилюле при возврате в покой.</summary>
    private string? _idleNote;

    /// <summary>Вспышка с результатом уже показана — не перетирать её.</summary>
    private bool _resultFlashed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            WakeRunningInstance();
            Shutdown();
            return;
        }

        // Сначала журнал и перехват падений — до всего остального. Иначе
        // ошибка в самой инициализации осталась бы без единого следа.
        InstallCrashHandlers();
        AppLog.Info($"Запуск Tapybara {AppVersion}, данные в {AppPaths.DataDirectory}");

        _settings = new SettingsHost(SettingsStore.Load());
        _settings.Changed += OnSettingsChanged;
        _settings.SaveFailed += () => _tray?.SetStatus(L.S.StatusSettingsNotSaved);

        L.Use(_settings.Current.UiLanguage);
        SystemTheme.Apply(_settings.Current.Theme);
        SystemTheme.StartWatching();
        SystemTheme.Changed += OnSystemThemeChanged;

        StartWakeListener();

        _overlay = new OverlayWindow();
        _overlay.Clicked += OnOverlayClicked;
        _overlay.StopCallRequested += () =>
        {
            if (_callRecorder.IsRecording)
            {
                _ = ToggleCallRecordingAsync();
            }
        };
        _overlay.OpenCallsRequested += () => OpenCalls(_transcribingCallDirectory);
        _overlay.Moved += (left, top) =>
            _settings.Update(s => s with { OverlayLeft = left, OverlayTop = top });
        ApplyOverlayPosition();

        _tray = new TrayIconHost();
        _tray.ToggleRequested += () => _ = ToggleAsync();
        _tray.CancelRequested += () => _ = CancelAsync();
        _tray.ExitRequested += Shutdown;
        _tray.RetryHotkeyRequested += () =>
        {
            StartHotkey();
            StartCallHotkey();
        };
        _tray.SettingsRequested += OpenSettings;
        _tray.RecordCallRequested += () => _ = ToggleCallRecordingAsync();
        _tray.OpenRequested += () => OpenMain();

        _callRecorder.Stopped += (reason, detail) => OnUi(() => OnCallStoppedItself(reason, detail));

        // Волна на пилюле звонка — по своему микрофону. Раньше пилюля звонка
        // стояла с плоской линией, и на часовой записи нечем было убедиться,
        // что микрофон вообще что-то слышит.
        _callRecorder.MicLevel += level => OnUi(() =>
        {
            if (_controller is not { State: DictationState.Recording })
            {
                _overlay!.PushLevel(level);
            }
        });

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _elapsedTimer.Tick += (_, _) => OnElapsedTick();

        _journal = new DictationJournal(AppPaths.DictationsPath);
        _voiceBook = new VoiceBook(AppPaths.VoicesPath);

        StartHotkey();
        StartCallHotkey();

        StartModelsWatcher();

        // Оборванные прошлым разом записи чиним в фоне: операция дисковая,
        // а старт приложения задерживать незачем.
        _ = Task.Run(() => CallRepair.RepairAll(CallsDirectory()));

        // Первый запуск — окно, которое ведёт до первой фразы. У тех, кто
        // обновился и модель уже скачал, флаг ставится молча.
        if (!_settings.Current.OnboardingDone)
        {
            if (AvailableModels().Count > 0)
            {
                _settings.Update(s => s with { OnboardingDone = true });
            }
            else
            {
                ShowWelcome();
            }
        }

        _ = RebuildEngineAsync();

        // Ярлык с этим аргументом открывает настройки сразу: искать иконку в
        // трее, чтобы поменять горячую клавишу, — лишний шаг.
        if (e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
        {
            OpenSettings();
        }

        if (e.Args.Any(a => string.Equals(a, "--calls", StringComparison.OrdinalIgnoreCase)))
        {
            OpenMain(MainPage.Calls);
        }
        else if (e.Args.Any(a => string.Equals(a, "--open", StringComparison.OrdinalIgnoreCase)))
        {
            OpenMain();
        }

        // Пройти первый запуск заново — например, чтобы показать программу
        // другому человеку на его машине.
        if (e.Args.Any(a => string.Equals(a, "--welcome", StringComparison.OrdinalIgnoreCase)))
        {
            ShowWelcome();
        }
    }

    private static string AppVersion =>
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    // --- папка моделей ------------------------------------------------------

    /// <summary>
    /// Следить за папкой моделей.
    /// </summary>
    /// <remarks>
    /// Модель не обязательно приходит через нашу кнопку «Скачать»: файл
    /// копируют проводником, кладут с флешки, распаковывают из архива. Без
    /// наблюдения список моделей в трее оставался прежним до перезапуска
    /// приложения — и человек, только что положивший файл в папку, видел, что
    /// программа его «не замечает».
    /// </remarks>
    private void StartModelsWatcher()
    {
        StopModelsWatcher();

        string directory = ModelLocator.FindModelsDirectory(_settings.Current.ModelsDirectory)
                           ?? AppPaths.DefaultModelsDirectory;

        try
        {
            Directory.CreateDirectory(directory);

            var watcher = new FileSystemWatcher(directory, "*.bin")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };

            // Копирование большого файла порождает поток событий, а
            // переименование — сразу два. Собираем всё в одно обновление,
            // иначе на каждой полусекунде копирования полутора гигабайт мы
            // перестраивали бы меню.
            _modelsDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _modelsDebounce.Tick += (_, _) =>
            {
                _modelsDebounce!.Stop();
                OnModelsChanged();
            };

            void Bump(object? _, FileSystemEventArgs __) => OnUi(() =>
            {
                _modelsDebounce!.Stop();
                _modelsDebounce.Start();
            });

            watcher.Created += Bump;
            watcher.Deleted += Bump;
            watcher.Renamed += (_, e) => Bump(null, e);
            watcher.Changed += Bump;
            watcher.EnableRaisingEvents = true;

            _modelsWatcher = watcher;
            AppLog.Info($"Слежу за папкой моделей: {directory}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Warn($"Не удалось следить за папкой моделей {directory}.", ex);
        }
    }

    private void StopModelsWatcher()
    {
        _modelsDebounce?.Stop();
        _modelsDebounce = null;

        if (_modelsWatcher is { } watcher)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _modelsWatcher = null;
        }
    }

    /// <summary>
    /// Удалить модель, отпустив её, если она сейчас загружена.
    /// </summary>
    /// <returns>Причина отказа или <c>null</c>, если удалось.</returns>
    /// <remarks>
    /// Файл загруженной модели удерживается движком, и удаление «в лоб»
    /// кончилось бы отказом с невнятной системной формулировкой. Поэтому
    /// движок сначала разбирается, а после удаления собирается заново — уже
    /// на том, что осталось.
    /// </remarks>
    private async Task<string?> DeleteModelAsync(InstalledModel model)
    {
        AppSettings settings = _settings.Current;

        bool inUse = string.Equals(model.FileName, settings.ModelFileName, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(model.FileName, settings.VadModelFileName, StringComparison.OrdinalIgnoreCase);

        if (inUse)
        {
            await _rebuildGate.WaitAsync().ConfigureAwait(true);
            try
            {
                await DisposeEngineAndControllerAsync().ConfigureAwait(true);
            }
            finally
            {
                _rebuildGate.Release();
            }
        }

        // Наблюдатель поднимет своё событие сам, но оно придёт с задержкой:
        // на время удаления его лучше не слушать, чтобы не пересобирать
        // движок дважды.
        if (ModelStorage.Delete(model.Path) is { } failure)
        {
            if (inUse)
            {
                _ = RebuildEngineAsync();
            }

            return failure;
        }

        // Настройка указывает на файл, которого больше нет — переводим её на
        // то, что осталось, иначе следующая сборка движка возьмёт «любую» и
        // меню покажет отмеченной не ту.
        if (model.Kind == ModelKind.Recognition
            && string.Equals(model.FileName, settings.ModelFileName, StringComparison.OrdinalIgnoreCase)
            && AvailableModels() is [string replacement, ..])
        {
            _settings.Update(s => s with { ModelFileName = replacement });
        }
        else if (inUse)
        {
            _ = RebuildEngineAsync();
        }

        return null;
    }

    // --- падения и журнал --------------------------------------------------

    /// <summary>
    /// Перехват необработанных исключений.
    /// </summary>
    /// <remarks>
    /// Приложение живёт в трее, окон в норме не показывает, и любое
    /// необработанное исключение в потоке интерфейса убивало его молча: иконка
    /// просто исчезала. Ни сообщения, ни файла, ни возможности понять, что
    /// произошло. Перехват здесь превращает падение в запись в журнале и
    /// уведомление — и, где возможно, оставляет приложение работать.
    /// </remarks>
    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("Необработанное исключение в потоке интерфейса.", args.Exception);
            args.Handled = true;

            _tray?.ShowBalloon(
                L.S.NotifyCrashTitle,
                string.Format(CultureInfo.CurrentCulture, L.S.NotifyCrashBody, AppLog.LogPath),
                BalloonKind.Warning,
                OpenLog);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Необработанное исключение вне интерфейса.", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Warn("Необработанное исключение в фоновой задаче.", args.Exception);
            args.SetObserved();
        };
    }

    private static void OpenLog()
    {
        try
        {
            if (File.Exists(AppLog.LogPath))
            {
                Process.Start(new ProcessStartInfo(AppLog.LogPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            AppLog.Warn("Не удалось открыть журнал.", ex);
        }
    }

    // --- второй экземпляр ---------------------------------------------------

    private void StartWakeListener()
    {
        try
        {
            _wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName);
            _wakeRegistration = ThreadPool.RegisterWaitForSingleObject(
                _wakeEvent,
                (_, _) => OnUi(ShowAlreadyRunning),
                state: null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            AppLog.Warn("Не удалось поднять сигнал о повторном запуске.", ex);
        }
    }

    private static void WakeRunningInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(WakeEventName, out EventWaitHandle? handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            AppLog.Warn("Не удалось разбудить работающий экземпляр.", ex);
        }
    }

    /// <summary>
    /// Программу запустили второй раз — показать окно.
    /// </summary>
    /// <remarks>
    /// Раньше второй запуск показывал уведомление «уже запущена, она в
    /// трее». Человек, дважды щёлкнувший по ярлыку, хотел открыть программу, а
    /// не узнать, где она прячется.
    /// </remarks>
    private void ShowAlreadyRunning() => OpenMain();

    // --- сборка движка -----------------------------------------------------

    /// <summary>
    /// Создать движок и контроллер под текущие настройки.
    /// </summary>
    /// <remarks>
    /// Вызывается заново при смене модели: движок держит загруженную модель,
    /// и подменить её у живого объекта нельзя — проще пересобрать.
    /// <para>
    /// Семафор обязателен. Два быстрых изменения подряд (модель из меню и
    /// язык в настройках) запускали две пересборки одновременно: вторая
    /// перезаписывала поле <c>_engine</c>, а первая оставалась висеть
    /// неосвобождённой — с гигабайтами модели в памяти.
    /// </para>
    /// </remarks>
    private async Task RebuildEngineAsync()
    {
        await _rebuildGate.WaitAsync().ConfigureAwait(true);
        _tray!.SetEngineBusy(true);
        try
        {
            await RebuildEngineCoreAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось собрать движок.", ex);
            _tray.SetStatus(string.Format(CultureInfo.CurrentCulture, L.S.StatusModelLoadFailed, ex.Message));
        }
        finally
        {
            _tray.SetEngineBusy(false);
            _rebuildGate.Release();
        }
    }

    private async Task RebuildEngineCoreAsync()
    {
        // ВАЖНО: освобождение асинхронное. Синхронное ожидание здесь морозило
        // весь интерфейс: движок держит семафор всё время прогрева, а прогрев
        // после смены модели занимает секунды — иконка в трее переставала
        // отвечать, и это выглядело как зависание приложения.
        await DisposeEngineAndControllerAsync().ConfigureAwait(true);

        AppSettings settings = _settings.Current;

        string? modelPath = ModelLocator.Resolve(settings.ModelFileName, settings.ModelsDirectory)
                            ?? ModelLocator.ResolveAnyAvailable(settings.ModelsDirectory);

        if (modelPath is null)
        {
            _engineFailure = null;
            _tray!.SetStatus(L.S.StatusModelMissing);

            // Окно первого запуска и так говорит, что модель нужна, и
            // помогает её скачать. Уведомление поверх него — второй голос,
            // твердящий то же самое.
            if (_welcomeWindow is null)
            {
                _tray.ShowBalloon(
                    L.S.NotifyNoModelTitle,
                    L.S.NotifyNoModelBody,
                    BalloonKind.Warning,
                    () => OpenSettings(SettingsSection.Models));
            }
            AppLog.Warn("Модель распознавания не найдена.");
            return;
        }

        // Если настроенной модели нет, а нашлась другая — честно запоминаем ту,
        // что реально используется, иначе меню показывало бы отмеченной не ту.
        string actualFileName = Path.GetFileName(modelPath);
        if (!string.Equals(actualFileName, settings.ModelFileName, StringComparison.OrdinalIgnoreCase))
        {
            _settings.Update(s => s with { ModelFileName = actualFileName });
            settings = _settings.Current;
        }

        _engine = new WhisperEngine(new WhisperEngineOptions
        {
            ModelPath = modelPath,
            Language = settings.Language,
            Prompt = settings.Prompt,
            BeamSize = settings.BeamSize,
            IdleUnloadAfter = TimeSpan.FromMinutes(settings.IdleUnloadMinutes),
        });

        SpeechDetector? detector = null;
        if (settings.UseVoiceActivityDetection)
        {
            string? vadPath = ModelLocator.ResolveVadModel(settings.VadModelFileName, settings.ModelsDirectory);
            if (vadPath is not null)
            {
                detector = new SpeechDetector(new SpeechDetectorOptions
                {
                    ModelPath = vadPath,
                    Threshold = (float)settings.VadThreshold,
                });
            }
            else
            {
                AppLog.Info("Детектор речи включён, но его модели нет — работаю без него.");
            }
        }

        _detector = detector;

        // Один распознаватель на всё: и диктовка, и звонки. Раньше детектор
        // и нормализация подключались ТОЛЬКО к звонкам, а диктовка ходила в
        // движок напрямую — три настройки и целый мастер подбора порога не
        // влияли на главную функцию приложения вообще.
        _transcriber = new SpeechTranscriber(_engine, detector, settings.NormalizeAudio);

        _callTranscriber = new CallTranscriber(
            _transcriber,
            () => _settings.Current,
            () => L.S.TranscriptLabels,
            EnsureDiarizer,
            EnsureVoiceprints);

        _controller = new DictationController(_transcriber, () => _settings.Current);
        _controller.StateChanged += state => OnUi(() => OnStateChanged(state));
        _controller.LevelChanged += level => OnUi(() => _overlay!.PushLevel(level));
        _controller.ProgressChanged += percent => OnUi(() => _overlay!.ShowTranscribing(percent));
        _controller.TextReady += text => OnUi(() => OnTextReady(text));
        _controller.Status += status => OnUi(() => _tray!.SetStatus(L.S.Describe(status)));

        _tray!.SetStatus(L.S.StatusLoadingModel);

        try
        {
            var timer = Stopwatch.StartNew();
            await _engine.LoadAsync().ConfigureAwait(true);
            _engineFailure = null;
            _tray.SetStatus($"{L.S.StatusReady} · {WhisperEngine.LoadedRuntime} · {timer.Elapsed.TotalSeconds:F1} {L.S.Seconds}");
            AppLog.Info($"Модель загружена за {timer.Elapsed.TotalSeconds:F1} с, бэкенд {WhisperEngine.LoadedRuntime}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Модель не загрузилась.", ex);
            _engineFailure = ex.Message;

            // Разбираем собранное обратно. Оставить контроллер живым при
            // непригодной модели значит принимать диктовку, которую нечем
            // распознать: пользователь говорит, а отказ приходит после.
            await DisposeEngineAndControllerAsync().ConfigureAwait(true);

            _tray.SetStatus(string.Format(CultureInfo.CurrentCulture, L.S.StatusModelLoadFailed, ex.Message));
            _tray.ShowBalloon(
                L.S.NotifyNoModelTitle,
                string.Format(CultureInfo.CurrentCulture, L.S.StatusModelLoadFailed, ex.Message),
                BalloonKind.Warning,
                () => OpenSettings(SettingsSection.Models));
        }
    }

    /// <summary>
    /// Занять глобальный хоткей. Безопасно вызывать повторно — например из
    /// меню, когда конфликтующее приложение наконец закрыли.
    /// </summary>
    private void StartHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = null;

        HotkeyCombo combo = _settings.Current.Hotkey;

        try
        {
            var listener = new HotkeyListener(combo);

            // Событие приходит из потока listener'а — сразу уводим его в UI.
            listener.Pressed += () => OnUi(() => _ = ToggleAsync());
            listener.Start();
            _hotkey = listener;
            _hotkeyFailed = false;
            _tray!.SetHotkeyDisplay(combo.ToString());
            _settingsWindow?.ReportHotkeyResult(HotkeyTarget.Dictation, true);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось занять {combo}.", ex);
            _hotkeyFailed = true;
            ReportHotkeyBusy(combo, ex, SettingsSection.Dictation);
            _settingsWindow?.ReportHotkeyResult(HotkeyTarget.Dictation, false);
        }

        _tray!.SetHotkeyFailed(_hotkeyFailed || _callHotkeyFailed);
    }

    /// <summary>
    /// Занять хоткей записи звонка. Устроен так же, как хоткей диктовки.
    /// </summary>
    /// <remarks>
    /// Отдельный слушатель, а не второе сочетание в первом: каждое сочетание
    /// занимается и освобождается само по себе, и занятое чужой программой
    /// сочетание звонка не должно отнимать у человека диктовку.
    /// </remarks>
    private void StartCallHotkey()
    {
        _callHotkey?.Dispose();
        _callHotkey = null;

        HotkeyCombo combo = _settings.Current.CallHotkey;

        try
        {
            var listener = new HotkeyListener(combo);
            listener.Pressed += () => OnUi(() => _ = ToggleCallRecordingAsync());
            listener.Start();
            _callHotkey = listener;
            _callHotkeyFailed = false;
            _tray!.SetCallHotkeyDisplay(combo.ToString());
            _settingsWindow?.ReportHotkeyResult(HotkeyTarget.Call, true);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось занять {combo} для звонков.", ex);
            _callHotkeyFailed = true;
            ReportHotkeyBusy(combo, ex, SettingsSection.Calls);
            _settingsWindow?.ReportHotkeyResult(HotkeyTarget.Call, false);
        }

        _tray!.SetHotkeyFailed(_hotkeyFailed || _callHotkeyFailed);
    }

    private void ReportHotkeyBusy(HotkeyCombo combo, Exception ex, SettingsSection section)
    {
        _tray!.SetStatus(string.Format(CultureInfo.CurrentCulture, L.S.StatusHotkeyBusy, combo));
        _tray.ShowBalloon(
            L.S.NotifyHotkeyBusyTitle,
            ex.Message + Environment.NewLine + L.S.NotifyHotkeyBusyHint,
            BalloonKind.Warning,
            () => OpenSettings(section));
    }

    // --- работа диктовки ---------------------------------------------------

    private async Task ToggleAsync()
    {
        if (_controller is null)
        {
            // Различаем «ещё грузится» и «загрузить не удалось»: это разные
            // ситуации, и ждать во второй бессмысленно.
            _tray!.SetStatus(_engineFailure is { } failure
                ? string.Format(CultureInfo.CurrentCulture, L.S.StatusModelLoadFailed, failure)
                : AvailableModels().Count == 0
                    ? L.S.StatusModelMissing
                    : L.S.StatusModelStillLoading);
            return;
        }

        await _controller.ToggleAsync();
    }

    /// <summary>Клик по пилюле: во время распознавания это отмена, иначе — переключение.</summary>
    private void OnOverlayClicked()
    {
        if (_controller is { State: DictationState.Transcribing })
        {
            _ = CancelAsync();
            return;
        }

        _ = ToggleAsync();
    }

    private void OnStateChanged(DictationState state)
    {
        _tray!.UpdateState(state);

        switch (state)
        {
            case DictationState.Recording:
                _idleNote = null;
                _resultFlashed = false;
                if (_settings.Current.ShowOverlay)
                {
                    _overlay!.ShowRecording(_settings.Current.Hotkey.ToString());
                }

                UpdateElapsedTimer();
                StartCancelWatcher();
                break;

            case DictationState.Transcribing:
                UpdateElapsedTimer();
                if (_settings.Current.ShowOverlay)
                {
                    _overlay!.ShowTranscribing(0);
                }

                break;

            case DictationState.Idle:
                UpdateElapsedTimer();
                StopCancelWatcher();

                // Порядок событий: TextReady приходит РАНЬШЕ перехода в покой,
                // поэтому без этой проверки «Готово» затирало бы вспышку
                // «Вставлено» через миг после её появления.
                if (!_resultFlashed && _overlay!.IsVisible)
                {
                    _overlay.FlashAndHide(_idleNote ?? L.S.PillDone);
                }

                // Звонок мог писаться или распознаваться всё это время — вернём
                // его пилюлю, но после вспышки результата, а не поверх неё.
                ShowCallOverlaySoon();

                break;
        }
    }

    private void OnTextReady(string text)
    {
        _resultFlashed = true;

        AppSettings settings = _settings.Current;

        // В историю — до вставки: если вставка упадёт, текст всё равно
        // сохранится, а ради этого история и заведена.
        if (settings.KeepDictationHistory)
        {
            _journal.Append(new DictationEntry(DateTimeOffset.Now, text));
        }

        try
        {
            if (settings.AutoPaste)
            {
                TextInserter.PasteViaClipboard(text, settings.ExcludeFromClipboardHistory);
                _overlay!.FlashAndHide(L.S.PillInserted);
            }
            else
            {
                ClipboardWriter.SetText(text, settings.ExcludeFromClipboardHistory);
                _overlay!.FlashAndHide(L.S.PillClipboardOnly);
            }

            _tray!.SetStatus(DescribeResult(text, settings));
        }
        catch (Exception ex)
        {
            // Текст уже в буфере: вставка могла не пройти из-за окна с правами
            // администратора. Пользователь не должен потерять надиктованное.
            AppLog.Warn("Вставка не удалась — текст остался в буфере.", ex);
            _overlay!.FlashAndHide(L.S.PillInsertedViaClipboard);
            _tray!.SetStatus(ex.Message);
        }
    }

    /// <summary>
    /// Что написать в трее про готовый текст.
    /// </summary>
    /// <remarks>
    /// По умолчанию — количество слов, а не сам текст. Подсказка трея была
    /// единственным местом, где надиктованное надолго оставалось на экране,
    /// а диктуют в том числе пароли и личную переписку. Кому предпросмотр
    /// нужен — включает его в настройках.
    /// </remarks>
    private static string DescribeResult(string text, AppSettings settings)
    {
        if (settings.ShowTextPreviewInTray)
        {
            return Preview(text);
        }

        int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return string.Format(CultureInfo.CurrentCulture, L.S.StatusTextReady, words);
    }

    /// <summary>
    /// Следить за Escape на время диктовки.
    /// </summary>
    /// <remarks>
    /// Именно СЛЕДИТЬ, а не занимать. Прежняя версия регистрировала Escape
    /// как глобальный хоткей, и на всё время диктовки — а это минуты — клавиша
    /// переставала доходить до активного окна: не закрывались диалоги, не
    /// сворачивались меню, не отменялось автодополнение. Низкоуровневый хук
    /// видит нажатие и передаёт его дальше.
    /// </remarks>
    private void StartCancelWatcher()
    {
        StopCancelWatcher();

        try
        {
            var watcher = new KeyWatcher(VirtualKeyEscape);
            watcher.Pressed += () => _ = CancelAsync();
            watcher.Start();
            _cancelWatcher = watcher;
        }
        catch (Win32Exception ex)
        {
            // Не поставился хук — не повод ронять диктовку. Остановить её
            // всё равно можно хоткеем, меню или кликом по пилюле.
            AppLog.Warn("Не удалось начать наблюдение за Escape.", ex);
            _cancelWatcher = null;
        }
    }

    private void StopCancelWatcher()
    {
        _cancelWatcher?.Dispose();
        _cancelWatcher = null;
    }

    private async Task CancelAsync()
    {
        if (_controller is null)
        {
            return;
        }

        _idleNote = L.S.PillCancelled;
        await _controller.CancelAsync();
    }

    // --- запись звонков ----------------------------------------------------

    /// <summary>Начать запись звонка, если стоим; закончить и распознать, если пишем.</summary>
    private async Task ToggleCallRecordingAsync()
    {
        try
        {
            if (!_callRecorder.IsRecording)
            {
                if (!ConfirmCallRecording())
                {
                    return;
                }

                AppSettings settings = _settings.Current;
                _callRecorder.Start(new CallRecordingOptions(
                    CallsDirectory(),
                    settings.MicrophoneDeviceId,
                    settings.SystemAudioDeviceId,
                    TimeSpan.FromMinutes(Math.Clamp(settings.MaxCallMinutes, 1, 24 * 60))));

                _tray!.SetRecordingCall(true);
                if (settings.ShowOverlay && _controller is not { State: DictationState.Recording })
                {
                    _overlay!.ShowCallRecording(settings.CallHotkey.ToString());
                }

                UpdateElapsedTimer();
                return;
            }

            CallSession? session = await _callRecorder.StopAsync();
            _tray!.SetRecordingCall(false);
            if (_overlay!.Mode == OverlayMode.CallRecording)
            {
                _overlay.HideNow();
            }

            UpdateElapsedTimer();

            if (session is not null)
            {
                // Распознавание начинается сразу и окно не ждёт. Кто был на
                // звонке, нужно знать только к разделению голосов, а это
                // конец работы: к тому времени человек обычно уже ответил,
                // а если нет — голоса разделятся без подсказки и их назовут
                // по цитатам.
                ReviewCall(session);
                await TranscribeCallAsync(session);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Сбой записи звонка.", ex);
            _tray!.SetRecordingCall(false);
            _tray.SetStatus(ex.Message);
            UpdateElapsedTimer();
        }
    }

    /// <summary>
    /// Показать предупреждение о записи — один раз за всё время.
    /// </summary>
    /// <remarks>
    /// Запись разговора захватывает и собеседника, а во многих странах делать
    /// это без его ведома незаконно. Кроме того, пишется весь звук устройства,
    /// а не только звонок. Сказать об этом один раз — минимум, который
    /// приложение обязано сделать, прежде чем начать.
    /// </remarks>
    private bool ConfirmCallRecording()
    {
        if (_settings.Current.CallRecordingAcknowledged)
        {
            return true;
        }

        var dialog = new CallConsentWindow();
        if (_settingsWindow is { IsVisible: true })
        {
            dialog.Owner = _settingsWindow;
        }

        bool accepted = dialog.ShowDialog() == true;
        if (accepted)
        {
            _settings.Update(s => s with { CallRecordingAcknowledged = true });
        }

        return accepted;
    }

    private void OnCallStoppedItself(CallStopReason reason, string? detail)
    {
        _tray!.SetRecordingCall(false);
        _overlay!.HideNow();
        UpdateElapsedTimer();

        string message = L.S.Describe(reason);
        _tray.SetStatus(message);
        _tray.ShowBalloon(
            L.S.NotifyCallStoppedTitle,
            detail is null ? message : $"{message} {detail}",
            BalloonKind.Warning,
            () => OpenMain(MainPage.Calls));
    }

    /// <summary>
    /// Собрать разделитель голосов, если его модели на месте.
    /// </summary>
    /// <returns><c>null</c>, если моделей нет — тогда звонок разберётся без имён.</returns>
    /// <remarks>
    /// Пересобирается, когда меняются модели или порог, и переживает остальные
    /// правки настроек: загрузка тридцати пяти мегабайт ONNX на каждое
    /// переключение галочки — это секунды ожидания там, где ничего не менялось.
    /// <para>
    /// Отсутствие моделей — не ошибка. Разделение голосов нужно меньшинству
    /// звонков, и требовать скачать его ради разговора один на один незачем.
    /// </para>
    /// </remarks>
    private SpeakerDiarizer? EnsureDiarizer()
    {
        AppSettings settings = _settings.Current;
        if (!settings.SplitVoices)
        {
            return null;
        }

        string? segmentation = ModelLocator.Resolve(
            settings.VoiceSegmentationModelFileName, settings.ModelsDirectory);
        string? embedding = ModelLocator.Resolve(
            settings.VoiceEmbeddingModelFileName, settings.ModelsDirectory);

        if (segmentation is null || embedding is null)
        {
            // Называем недостающий файл поимённо. Разделению нужны ОБЕ модели,
            // и на странице моделей они стоят рядом — скачать одну и решить,
            // что готово, слишком легко. Сообщение «моделей нет» в этом случае
            // отправляет искать то, что наполовину уже лежит на диске.
            string missing = string.Join(", ", new[]
            {
                segmentation is null ? settings.VoiceSegmentationModelFileName : null,
                embedding is null ? settings.VoiceEmbeddingModelFileName : null,
            }.OfType<string>());

            AppLog.Info($"Разделение голосов включено, но не хватает моделей: {missing}. Собеседники останутся без имён.");
            return null;
        }

        var wanted = (segmentation, embedding, settings.VoiceSplitThreshold);
        if (_diarizer is not null && _diarizerBuiltFor == wanted)
        {
            return _diarizer;
        }

        _diarizer?.Dispose();
        _diarizer = null;
        _diarizerBuiltFor = null;

        try
        {
            _diarizer = new SpeakerDiarizer(new SpeakerDiarizerOptions
            {
                SegmentationModelPath = segmentation,
                EmbeddingModelPath = embedding,
                ClusterThreshold = (float)settings.VoiceSplitThreshold,
            });

            _diarizerBuiltFor = wanted;
            AppLog.Info($"Разделитель голосов готов: {Path.GetFileName(embedding)}.");
        }
        catch (Exception ex)
        {
            // Битая или чужая модель роняет нативную сторону при создании.
            // Звонок при этом уже записан, и терять его из-за имён нельзя.
            AppLog.Error("Не удалось собрать разделитель голосов.", ex);
            _tray?.SetStatus(ex.Message);
        }

        return _diarizer;
    }

    /// <summary>
    /// Собрать снятие слепков голоса, если запоминание включено и модель есть.
    /// </summary>
    /// <returns><c>null</c> — слепков не будет, звонок от этого не хуже.</returns>
    private VoiceprintExtractor? EnsureVoiceprints()
    {
        AppSettings settings = _settings.Current;
        if (!settings.RememberVoices)
        {
            return null;
        }

        string? embedding = ModelLocator.Resolve(settings.VoiceEmbeddingModelFileName, settings.ModelsDirectory);
        if (embedding is null)
        {
            return null;
        }

        if (_voiceprints is not null && _voiceprintsBuiltFor == embedding)
        {
            return _voiceprints;
        }

        _voiceprints?.Dispose();
        _voiceprints = null;
        _voiceprintsBuiltFor = null;

        try
        {
            _voiceprints = new VoiceprintExtractor(embedding);
            _voiceprintsBuiltFor = embedding;
        }
        catch (Exception ex)
        {
            // Та же осторожность, что у разделителя: чужая или битая модель
            // роняет нативную сторону, а звонок из-за подсказок имён терять нельзя.
            AppLog.Error("Не удалось собрать снятие слепков голоса.", ex);
        }

        return _voiceprints;
    }

    /// <summary>
    /// Запомнить голоса, имена которых известны наверняка.
    /// </summary>
    /// <remarks>
    /// Наверняка — это когда человек назвал голос сам, или когда на той
    /// стороне был один отмеченный участник: весь чужой канал — он, по
    /// построению. Так книга голосов учится и на разговорах один на один, где
    /// человек ничего не называл, а только отметил собеседника.
    /// </remarks>
    private void LearnCertainVoices(string directory)
    {
        if (!_settings.Current.RememberVoices
            || CallMeta.Load(directory) is not { } session
            || CallTranscriptStore.Load(directory) is not { } transcript)
        {
            return;
        }

        foreach ((string voice, string name) in session.VoiceNames)
        {
            if (transcript.VoicePrints.TryGetValue(voice, out float[]? print))
            {
                _voiceBook.Learn(name, print);
            }
        }

        if (session.Participants.Count == 1)
        {
            float[]? whole = transcript.VoicePrints.GetValueOrDefault(CallVoices.WholeOtherSide)
                             ?? (transcript.VoicePrints.Count == 1 ? transcript.VoicePrints.Values.First() : null);
            if (whole is not null)
            {
                _voiceBook.Learn(session.Participants[0], whole);
            }
        }
    }

    /// <summary>
    /// Спросить, кто был на звонке, — не задерживая распознавание.
    /// </summary>
    /// <remarks>
    /// Раньше распознавание ждало закрытия этого окна. Окно не забирает фокус,
    /// и его легко не заметить, — а незамеченное окно означало звонок без
    /// транскрипта. Теперь участники пишутся в мету при каждом щелчке, и
    /// распознавание читает их оттуда, когда до них доходит.
    /// <para>
    /// Не <c>ShowDialog</c>: модальное окно забирает фокус, а звонок часто
    /// заканчивается поверх чего-то, во что человек продолжает печатать.
    /// </para>
    /// </remarks>
    private void ReviewCall(CallSession session)
    {
        AppSettings settings = _settings.Current;

        var window = new CallReviewWindow(session, settings.EffectiveMyName, settings.KnownParticipants);
        _reviewCards[session.Directory] = window;

        window.Closed += (_, _) =>
        {
            _reviewCards.Remove(session.Directory);

            if (window.Outcome == CallReviewOutcome.Deleted)
            {
                DeleteCall(session.Directory);
                return;
            }

            if (window.Outcome == CallReviewOutcome.Opened)
            {
                OpenCalls(session.Directory);
            }

            // Отмеченные имена поднимаются в начало списка знакомых: на
            // следующем звонке с теми же людьми они будут первыми чипами.
            IReadOnlyList<string> selected = window.SelectedParticipants;
            if (selected.Count > 0)
            {
                _settings.Update(s => s with
                {
                    KnownParticipants = [.. KnownParticipants.Touch(s.KnownParticipants, selected)],
                });
            }

            // Если распознавание уже закончилось — возможно, раньше, чем
            // человек отметил всех, — приводим транскрипт к ответу.
            if (!IsCallBusy(session.Directory))
            {
                _ = ReconcileCallAsync(session.Directory);
            }
        };

        window.Show();
    }

    /// <summary>Идёт ли или ждёт очереди работа над этим звонком.</summary>
    private bool IsCallBusy(string directory) =>
        _queuedCalls.Contains(directory)
        || string.Equals(_transcribingCallDirectory, directory, StringComparison.OrdinalIgnoreCase);

    /// <summary>Убрать папку звонка целиком.</summary>
    /// <remarks>
    /// В корзину, а не мимо неё: «удалить» нажимают и по ошибке, а разговор
    /// заново не случится. Распознавание этого звонка, если оно идёт,
    /// отменяется: иначе оно дописало бы файлы в папку, которой уже нет.
    /// </remarks>
    private void DeleteCall(string directory)
    {
        _queuedCalls.Remove(directory);
        if (string.Equals(_transcribingCallDirectory, directory, StringComparison.OrdinalIgnoreCase))
        {
            _callCancellation?.Cancel();
        }

        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                directory,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось удалить папку звонка.", ex);
            _tray?.SetStatus(ex.Message);
        }
    }

    /// <summary>
    /// Распознать записанный звонок.
    /// </summary>
    /// <remarks>
    /// Запись уже на диске, поэтому сбой распознавания её не теряет: звонок
    /// можно будет разобрать позже, а не переживать разговор заново.
    /// </remarks>
    private Task TranscribeCallAsync(CallSession session) =>
        RunCallJobAsync(
            session.Directory,
            async (transcriber, progress, token) => await transcriber.TranscribeAsync(session, progress, token),
            announce: true);

    /// <summary>
    /// Привести транскрипт к тому, что человек сказал об участниках.
    /// </summary>
    /// <remarks>
    /// Обычно это перерисовка за миллисекунды. Разделение голосов заново —
    /// только если отмечено несколько человек, а искали под другое число.
    /// </remarks>
    private Task ReconcileCallAsync(string directory) =>
        RunCallJobAsync(
            directory,
            (transcriber, progress, token) => transcriber.ReconcileAsync(directory, progress, token),
            announce: false);

    /// <summary>
    /// Одна работа над звонком: в очереди, с прогрессом на пилюле и в трее.
    /// </summary>
    /// <param name="directory">Папка звонка.</param>
    /// <param name="work">Сама работа; возвращает путь к транскрипту.</param>
    /// <param name="announce">
    /// Сообщить уведомлением, когда готово. Для распознавания — да: оно
    /// идёт минутами, и человек уже занят другим. Для перерисовки после
    /// щелчка по имени — нет: он смотрит прямо на результат.
    /// </param>
    private async Task RunCallJobAsync(
        string directory,
        Func<CallTranscriber, IProgress<CallTranscriptionProgress>, CancellationToken, Task<string?>> work,
        bool announce)
    {
        if (_callTranscriber is null)
        {
            _tray!.SetStatus(L.S.StatusModelStillLoading);
            return;
        }

        _queuedCalls.Add(directory);
        await _callGate.WaitAsync().ConfigureAwait(true);

        // Пока ждали очереди, звонок могли удалить.
        if (!_queuedCalls.Remove(directory) || _callTranscriber is not { } transcriber)
        {
            _callGate.Release();
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _callCancellation = cancellation;
        _transcribingCallDirectory = directory;
        _tray!.SetCallTranscribing(true);

        try
        {
            var progress = new Progress<CallTranscriptionProgress>(OnCallProgress);
            string? path = await work(transcriber, progress, cancellation.Token).ConfigureAwait(true);

            if (path is not null)
            {
                LearnCertainVoices(directory);
            }

            if (path is not null && announce)
            {
                AnnounceCallReady(directory);
            }
        }
        catch (OperationCanceledException)
        {
            AppLog.Info($"Работа над звонком отменена: {directory}.");
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось собрать транскрипт звонка.", ex);
            _tray.SetStatus(ex.Message);
        }
        finally
        {
            Volatile.Write(ref _callCancellation, null);
            _transcribingCallDirectory = null;
            _callProgress = null;
            _tray.SetCallTranscribing(false);
            _mainWindow?.RefreshCalls();
            _callGate.Release();

            if (_overlay!.Mode == OverlayMode.CallTranscribing && _overlay.IsVisible)
            {
                _overlay.FlashAndHide(L.S.PillCallReady);
            }
        }
    }

    private void OnCallProgress(CallTranscriptionProgress progress)
    {
        // Прогресс приходит в очередь диспетчера и может опоздать: работа уже
        // кончилась, а сообщение о её середине ещё в пути.
        if (_transcribingCallDirectory is null)
        {
            return;
        }

        _callProgress = progress;
        _tray!.SetStatus(L.S.Describe(progress));

        if (_reviewCards.TryGetValue(_transcribingCallDirectory, out CallReviewWindow? card))
        {
            card.SetProgress(progress);
        }

        ShowCallOverlay();
    }

    /// <summary>
    /// Сообщить, что транскрипт готов, — или что осталось назвать голоса.
    /// </summary>
    /// <remarks>
    /// Щелчок по уведомлению открывает этот звонок. Прежде он открывал
    /// журнал: уведомления не различали, о чём они.
    /// </remarks>
    private void AnnounceCallReady(string directory)
    {
        _tray!.SetStatus(string.Format(
            CultureInfo.CurrentCulture, L.S.StatusCallSaved, Path.GetFileName(directory)));

        CallSession? session = CallMeta.Load(directory);
        bool needsNames = session is not null && CallSpeakers.NeedsNames(session);

        // Карточка звонка ещё открыта — сообщаем в ней, а не уведомлением
        // поверх: человек и так смотрит на этот звонок.
        if (_reviewCards.TryGetValue(directory, out CallReviewWindow? card))
        {
            card.SetFinished(needsNames);
            return;
        }

        if (session is not null && needsNames)
        {
            _tray.ShowBalloon(
                L.S.NotifyCallNamesTitle,
                string.Format(CultureInfo.CurrentCulture, L.S.NotifyCallNamesBody, session.Voices.Count),
                onClick: () => OpenCalls(directory));
            return;
        }

        _tray.ShowBalloon(
            L.S.NotifyCallReadyTitle,
            L.S.NotifyCallReadyBody,
            onClick: () => OpenCalls(directory));
    }

    /// <summary>
    /// Показать на пилюле то, что сейчас происходит со звонками.
    /// </summary>
    /// <remarks>
    /// Диктовка главнее: пока она идёт, пилюля её. Среди звонков запись
    /// главнее распознавания — идущая запись требует внимания, а распознавание
    /// закончится само.
    /// </remarks>
    private void ShowCallOverlay()
    {
        if (!_settings.Current.ShowOverlay
            || _controller is { State: not DictationState.Idle }
            || _callOverlayDelay?.IsEnabled == true)
        {
            return;
        }

        if (_callRecorder.IsRecording)
        {
            if (_overlay!.Mode != OverlayMode.CallRecording || !_overlay.IsVisible)
            {
                _overlay.ShowCallRecording(_settings.Current.CallHotkey.ToString());
            }

            return;
        }

        if (_callProgress is { } progress)
        {
            _overlay!.ShowCallTranscribing(
                string.Format(CultureInfo.CurrentCulture, L.S.PillTranscribingCall, progress.Percent));
        }
    }

    /// <summary>Вернуть пилюлю звонка после того, как погаснет вспышка диктовки.</summary>
    private void ShowCallOverlaySoon()
    {
        if (!_callRecorder.IsRecording && _callProgress is null)
        {
            return;
        }

        if (_callOverlayDelay is null)
        {
            _callOverlayDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
            _callOverlayDelay.Tick += (_, _) =>
            {
                _callOverlayDelay.Stop();
                ShowCallOverlay();
            };
        }

        _callOverlayDelay.Stop();
        _callOverlayDelay.Start();
    }

    private string CallsDirectory() => _settings.Current.CallsDirectory ?? AppPaths.DefaultCallsDirectory;

    // --- таймер и оверлей ---------------------------------------------------

    private void OnElapsedTick()
    {
        if (_controller is { State: DictationState.Recording } controller)
        {
            _overlay!.UpdateElapsed(controller.Elapsed);
            return;
        }

        if (_callRecorder.IsRecording)
        {
            TimeSpan elapsed = _callRecorder.Elapsed;
            _overlay!.UpdateElapsed(elapsed);
            _tray!.SetStatus(string.Format(
                CultureInfo.CurrentCulture, L.S.StatusRecordingCall, Stamp(elapsed)));
        }
    }

    /// <summary>
    /// Таймер тикает, пока идёт хоть что-то с секундомером.
    /// </summary>
    /// <remarks>
    /// Диктовка и запись звонка независимы и могут идти одновременно. Пусть
    /// решение о таймере принимается в одном месте: иначе конец диктовки
    /// останавливал бы счётчик идущей записи звонка.
    /// </remarks>
    private void UpdateElapsedTimer()
    {
        bool needed = _callRecorder.IsRecording
                      || _controller is { State: DictationState.Recording };

        if (needed)
        {
            _elapsedTimer!.Start();
        }
        else
        {
            _elapsedTimer!.Stop();
        }
    }

    private void ApplyOverlayPosition()
    {
        AppSettings settings = _settings.Current;
        _overlay!.PinnedPosition = settings.OverlayLeft is { } left && settings.OverlayTop is { } top
            ? (left, top)
            : null;
    }

    private static string Stamp(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";

    // --- окно настроек -----------------------------------------------------

    private void ShowWelcome()
    {
        if (_welcomeWindow is { } existing)
        {
            existing.Activate();
            return;
        }

        var window = new WelcomeWindow(
            _settings,
            AvailableModels,
            OnModelsChanged,
            () => OpenSettings(SettingsSection.Models));
        window.Closed += (_, _) => _welcomeWindow = null;

        _welcomeWindow = window;
        window.Show();
    }

    /// <summary>Показать раздел звонков и выбрать в нём звонок.</summary>
    /// <param name="select">Папка звонка, или <c>null</c> — оставить выбор как есть.</param>
    private void OpenCalls(string? select)
    {
        MainWindow window = OpenMain(MainPage.Calls);
        if (select is not null)
        {
            window.SelectCall(select);
        }
    }

    /// <summary>
    /// Показать главное окно.
    /// </summary>
    /// <param name="page">Какой раздел открыть; <c>null</c> — тот, что был открыт.</param>
    private MainWindow OpenMain(MainPage? page = null)
    {
        MainWindow window = _mainWindow ?? CreateMainWindow();

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();

        if (page is { } wanted)
        {
            window.Show(wanted);
        }

        return window;
    }

    private MainWindow CreateMainWindow()
    {
        var window = new MainWindow(
            new CallsServices(
            _settings,
            CallsDirectory,
            LiveCallState,
            LiveCallPercent,
            TranscribeCallAsync,
            ReconcileCallAsync,
            ResplitCallAsync,
            RenderCall,
            DeleteCall,
            () => _ = ToggleCallRecordingAsync(),
            CanSplitVoices,
            _voiceBook),
            _journal,
            () => OpenSettings(SettingsSection.General));

        window.Closed += (_, _) => _mainWindow = null;
        _mainWindow = window;
        return window;
    }

    /// <summary>
    /// Что приложение прямо сейчас делает с этой папкой.
    /// </summary>
    /// <remarks>
    /// По файлам на диске «пишется» и «распознаётся» не отличить от «лежит»:
    /// это состояние живёт только в памяти. Список звонков спрашивает о нём
    /// здесь, а не заводит собственный реестр, который пришлось бы чинить
    /// после каждого падения.
    /// </remarks>
    private CallState? LiveCallState(string directory)
    {
        if (_queuedCalls.Contains(directory))
        {
            return CallState.Transcribing;
        }

        if (_callRecorder.IsRecording
            && string.Equals(_callRecorder.CurrentDirectory, directory, StringComparison.OrdinalIgnoreCase))
        {
            return CallState.Recording;
        }

        return string.Equals(_transcribingCallDirectory, directory, StringComparison.OrdinalIgnoreCase)
            ? CallState.Transcribing
            : null;
    }

    /// <summary>Разделить голоса звонка заново, по уже распознанному.</summary>
    private Task ResplitCallAsync(string directory, int voices) =>
        RunCallJobAsync(
            directory,
            (transcriber, progress, token) => transcriber.ResplitAsync(directory, voices, progress, token),
            announce: false);

    /// <summary>
    /// Перерисовать transcript.md после правки имени.
    /// </summary>
    /// <remarks>
    /// Без распознавателя и без очереди: это миллисекунды, и ждать ради них
    /// конца распознавания другого звонка — значит показать человеку, что
    /// имя, которое он только что выбрал, не применилось.
    /// </remarks>
    private void RenderCall(string directory)
    {
        AppSettings settings = _settings.Current;
        try
        {
            CallTranscriptRenderer.Write(
                directory,
                settings.EffectiveMyName,
                CallsPage.OtherSideLabel(settings),
                L.S.TranscriptLabels);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Не удалось перерисовать транскрипт.", ex);
        }
    }

    /// <summary>Есть ли чем разделять голоса: включено и обе модели на месте.</summary>
    private bool CanSplitVoices()
    {
        AppSettings settings = _settings.Current;
        return settings.SplitVoices
               && ModelLocator.Resolve(settings.VoiceSegmentationModelFileName, settings.ModelsDirectory) is not null
               && ModelLocator.Resolve(settings.VoiceEmbeddingModelFileName, settings.ModelsDirectory) is not null;
    }

    /// <summary>Сколько процентов распознано у звонка, если он распознаётся сейчас.</summary>
    private int? LiveCallPercent(string directory) =>
        string.Equals(_transcribingCallDirectory, directory, StringComparison.OrdinalIgnoreCase)
            ? _callProgress?.Percent
            : null;

    private void OpenSettings() => OpenSettings(SettingsSection.General);

    private void OpenSettings(SettingsSection section)
    {
        if (_settingsWindow is { } existing)
        {
            existing.Activate();
            existing.GoTo(section);
            return;
        }

        var window = new SettingsWindow(_settings, AvailableModels, DeleteModelAsync, _journal, _voiceBook);
        window.HotkeyCaptureChanged += OnHotkeyCaptureChanged;
        window.ModelsChanged += OnModelsChanged;
        window.Closed += (_, _) => _settingsWindow = null;

        _settingsWindow = window;
        window.Show();
        window.GoTo(section);
    }

    /// <summary>
    /// Настройка изменилась. Пересобираем ровно то, на что она влияет.
    /// </summary>
    /// <remarks>
    /// Сравнение со старым значением здесь не формальность: перезагрузка
    /// модели занимает секунды, и делать её на каждое переключение галочки
    /// «вставлять автоматически» было бы издевательством.
    /// </remarks>
    private void OnSettingsChanged(SettingsChange change)
    {
        if (change.AffectsUiLanguage)
        {
            L.Use(change.Current.UiLanguage);
            _tray!.ApplyLanguage();
            _settingsWindow?.ApplyLanguage();
            _mainWindow?.ApplyLanguage();

            foreach (CallReviewWindow card in _reviewCards.Values)
            {
                card.ApplyLanguage();
            }
        }

        if (change.AffectsTheme)
        {
            SystemTheme.Apply(change.Current.Theme);
        }

        if (change.AffectsHotkey)
        {
            StartHotkey();
        }

        if (change.AffectsCallHotkey)
        {
            StartCallHotkey();
        }

        if (change.AffectsOverlay)
        {
            ApplyOverlayPosition();
            if (!change.Current.ShowOverlay)
            {
                _overlay!.HideNow();
            }
        }

        if (change.Previous.ModelsDirectory != change.Current.ModelsDirectory)
        {
            StartModelsWatcher();
        }


        if (change.AffectsEngine)
        {
            _ = RebuildEngineAsync();
        }
    }

    /// <summary>
    /// На диске появилась новая модель.
    /// </summary>
    /// <remarks>
    /// Настройки при этом могли не измениться ни на байт: скачать модель,
    /// имя которой уже стоит в настройках, — самый обычный первый запуск.
    /// Пересобираем движок, только если он и правда чего-то ждал: перечитывать
    /// полтора гигабайта ради появления второй модели в списке незачем.
    /// </remarks>
    private void OnModelsChanged()
    {
        _settingsWindow?.RefreshFromDisk();

        AppSettings settings = _settings.Current;

        bool engineWasMissing = _engine is null;
        bool detectorWasMissing = _detector is null
                                  && settings.UseVoiceActivityDetection
                                  && ModelLocator.ResolveVadModel(settings.VadModelFileName, settings.ModelsDirectory) is not null;

        if (engineWasMissing || detectorWasMissing)
        {
            AppLog.Info("Появилась новая модель — пересобираю движок.");
            _ = RebuildEngineAsync();
        }
    }

    private void OnSystemThemeChanged() => OnUi(() =>
    {
        if (_settings.Current.Theme == AppTheme.System)
        {
            SystemTheme.Apply(AppTheme.System);
        }

        _tray?.ApplyTheme();
    });

    /// <summary>
    /// На время записи нового сочетания глобальный хоткей снимается.
    /// </summary>
    /// <remarks>
    /// Иначе нажатие текущего сочетания перехватила бы система и запустила
    /// диктовку вместо того, чтобы дать окну настроек его записать.
    /// </remarks>
    private void OnHotkeyCaptureChanged(bool capturing)
    {
        // Снимаем оба сочетания: иначе, записывая новое сочетание диктовки,
        // нельзя было бы выбрать сочетание звонка, и наоборот, — его
        // перехватила бы система.
        if (capturing)
        {
            _hotkey?.Dispose();
            _hotkey = null;
            _callHotkey?.Dispose();
            _callHotkey = null;
        }
        else
        {
            StartHotkey();
            StartCallHotkey();
        }
    }

    private IReadOnlyList<string> AvailableModels()
    {
        string? directory = ModelLocator.FindModelsDirectory(_settings.Current.ModelsDirectory);
        if (directory is null)
        {
            return [];
        }

        try
        {
            return
            [
                .. Directory.EnumerateFiles(directory, "ggml-*.bin")
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Where(name => !name.Contains("silero", StringComparison.OrdinalIgnoreCase))
                    .Order(),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Не удалось перечислить модели.", ex);
            return [];
        }
    }

    // --- служебное ---------------------------------------------------------

    /// <summary>Выполнить действие в потоке интерфейса.</summary>
    private void OnUi(Action action) => Dispatcher.BeginInvoke(action);

    private static string Preview(string text)
    {
        string flat = text.ReplaceLineEndings(" ");
        return flat.Length <= 50 ? flat : flat[..50] + "…";
    }

    /// <summary>
    /// Освободить движок и всё, что на нём держится.
    /// </summary>
    /// <remarks>
    /// <c>ConfigureAwait(false)</c> здесь принципиален. С захватом контекста
    /// продолжения ставились в очередь диспетчера — того самого потока, который
    /// при выходе блокируется в ожидании этой задачи. Получался классический
    /// дедлок: выход посреди распознавания замирал на весь таймаут и уходил,
    /// так и не освободив контекст whisper.
    /// </remarks>
    private async Task DisposeEngineAndControllerAsync()
    {
        if (_controller is { } controller)
        {
            _controller = null;
            await controller.DisposeAsync().ConfigureAwait(false);
        }

        _transcriber = null;
        _callTranscriber = null;

        if (_engine is { } engine)
        {
            _engine = null;
            await engine.DisposeAsync().ConfigureAwait(false);
        }

        if (_detector is { } detector)
        {
            _detector = null;
            await detector.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Освобождение движка при выходе.
    /// </summary>
    /// <remarks>
    /// Здесь блокировка допустима — приложение уже закрывается, — но с
    /// ограничением по времени: если распознавание зависло, выход не должен
    /// висеть вместе с ним. Незакрытый контекст на выходе процесса
    /// операционная система уберёт сама.
    /// </remarks>
    private void DisposeEngineAndControllerOnExit() =>
        DisposeEngineAndControllerAsync().Wait(TimeSpan.FromSeconds(3));

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Освобождение всего, чем владеет приложение.
    /// </summary>
    /// <remarks>
    /// Отдельный <c>Dispose</c>, а не просто код в <c>OnExit</c>: тип владеет
    /// освобождаемыми полями (движок, хоткей, трей, мьютекс), и анализатор
    /// справедливо требует, чтобы это было выражено в контракте типа.
    /// </remarks>
    public void Dispose()
    {
        AppLog.Info("Выход.");

        _elapsedTimer?.Stop();
        _elapsedTimer = null;

        StopModelsWatcher();

        _hotkey?.Dispose();
        _hotkey = null;
        _callHotkey?.Dispose();
        _callHotkey = null;

        StopCancelWatcher();

        // Запись звонка на выходе не бросаем: файлы дописываются и закрываются.
        _callRecorder.Dispose();
        _diarizer?.Dispose();
        _voiceprints?.Dispose();

        DisposeEngineAndControllerOnExit();

        _wakeRegistration?.Unregister(null);
        _wakeRegistration = null;
        _wakeEvent?.Dispose();
        _wakeEvent = null;

        _tray?.Dispose();
        _tray = null;

        // Настройки записываем последними и синхронно: отложенная запись
        // могла не успеть сработать до выхода.
        _settings?.Dispose();

        _rebuildGate.Dispose();
        _callGate.Dispose();

        _instanceMutex?.Dispose();
        _instanceMutex = null;

        GC.SuppressFinalize(this);
    }
}
