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
    private HotkeyListener? _hotkey;
    private TrayIconHost? _tray;
    private OverlayWindow? _overlay;
    private DispatcherTimer? _elapsedTimer;

    /// <summary>Наблюдение за Escape. Живёт только пока идёт диктовка.</summary>
    private KeyWatcher? _cancelWatcher;

    /// <summary>Окно настроек. Оно одно: второе рассинхронизировалось бы с первым.</summary>
    private SettingsWindow? _settingsWindow;

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
        _overlay.Moved += (left, top) =>
            _settings.Update(s => s with { OverlayLeft = left, OverlayTop = top });
        ApplyOverlayPosition();

        _tray = new TrayIconHost();
        _tray.ToggleRequested += () => _ = ToggleAsync();
        _tray.CancelRequested += () => _ = CancelAsync();
        _tray.HistoryItemSelected += CopyText;
        _tray.ExitRequested += Shutdown;
        _tray.OpenModelsFolderRequested += OpenModelsFolder;
        _tray.AutoPasteToggled += enabled => _settings.Update(s => s with { AutoPaste = enabled });
        _tray.AutoStartToggled += OnAutoStartToggled;
        _tray.ModelSelected += fileName => _settings.Update(s => s with { ModelFileName = fileName });
        _tray.RetryHotkeyRequested += StartHotkey;
        _tray.SettingsRequested += OpenSettings;
        _tray.RecordCallRequested += () => _ = ToggleCallRecordingAsync();
        _tray.OpenCallsFolderRequested += OpenCallsFolder;
        _tray.BalloonClicked += OnBalloonClicked;
        _tray.MenuOpening += RefreshTrayFromSettings;

        _callRecorder.Stopped += (reason, detail) => OnUi(() => OnCallStoppedItself(reason, detail));

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _elapsedTimer.Tick += (_, _) => OnElapsedTick();

        RefreshTrayFromSettings();
        StartHotkey();

        StartModelsWatcher();

        // Оборванные прошлым разом записи чиним в фоне: операция дисковая,
        // а старт приложения задерживать незачем.
        _ = Task.Run(() => CallRepair.RepairAll(CallsDirectory()));

        _ = RebuildEngineAsync();

        // Ярлык с этим аргументом открывает настройки сразу: искать иконку в
        // трее, чтобы поменять горячую клавишу, — лишний шаг.
        if (e.Args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
        {
            OpenSettings();
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

        RefreshTrayFromSettings();
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
                BalloonKind.Warning);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Необработанное исключение вне интерфейса.", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Warn("Необработанное исключение в фоновой задаче.", args.Exception);
            args.SetObserved();
        };
    }

    private void OnBalloonClicked()
    {
        // Уведомление о сбое ведёт в журнал, уведомление об отсутствии
        // модели — в раздел моделей. И то и другое лучше, чем тупик.
        if (AvailableModels().Count == 0)
        {
            OpenSettings(SettingsSection.Models);
            return;
        }

        OpenLog();
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

    private void ShowAlreadyRunning() =>
        _tray?.ShowBalloon(L.S.NotifyAlreadyRunningTitle, L.S.NotifyAlreadyRunningBody);

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
            _tray.ShowBalloon(L.S.NotifyNoModelTitle, L.S.NotifyNoModelBody, BalloonKind.Warning);
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
            () => L.S.TranscriptLabels);

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
                BalloonKind.Warning);
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
            _tray!.SetHotkeyFailed(false);
            _tray.SetHotkeyDisplay(combo.ToString());
            _settingsWindow?.ReportHotkeyResult(true);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось занять {combo}.", ex);
            _tray!.SetHotkeyFailed(true);
            _tray.SetStatus(string.Format(CultureInfo.CurrentCulture, L.S.StatusHotkeyBusy, combo));
            _tray.ShowBalloon(
                L.S.NotifyHotkeyBusyTitle,
                ex.Message + Environment.NewLine + L.S.NotifyHotkeyBusyHint,
                BalloonKind.Warning);
            _settingsWindow?.ReportHotkeyResult(false);
        }
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

                // Запись звонка могла идти всё это время — вернём её индикатор.
                if (_callRecorder.IsRecording && _settings.Current.ShowOverlay)
                {
                    _overlay!.ShowCallRecording();
                }

                break;
        }
    }

    private void OnTextReady(string text)
    {
        _tray!.SetHistory(_controller?.History ?? []);
        _resultFlashed = true;

        AppSettings settings = _settings.Current;

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

            _tray.SetStatus(DescribeResult(text, settings));
        }
        catch (Exception ex)
        {
            // Текст уже в буфере: вставка могла не пройти из-за окна с правами
            // администратора. Пользователь не должен потерять надиктованное.
            AppLog.Warn("Вставка не удалась — текст остался в буфере.", ex);
            _overlay!.FlashAndHide(L.S.PillInsertedViaClipboard);
            _tray.SetStatus(ex.Message);
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

    private void CopyText(string text)
    {
        try
        {
            ClipboardWriter.SetText(text, _settings.Current.ExcludeFromClipboardHistory);
            _tray!.SetStatus(L.S.StatusCopied);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось записать в буфер обмена.", ex);
            _tray!.SetStatus(ex.Message);
        }
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
                    _overlay!.ShowCallRecording();
                }

                UpdateElapsedTimer();
                return;
            }

            CallSession? session = await _callRecorder.StopAsync();
            _tray!.SetRecordingCall(false);
            _overlay!.HideNow();
            UpdateElapsedTimer();

            if (session is not null)
            {
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
            BalloonKind.Warning);
    }

    /// <summary>
    /// Распознать записанный звонок.
    /// </summary>
    /// <remarks>
    /// Запись уже на диске, поэтому сбой распознавания её не теряет: звонок
    /// можно будет разобрать позже, а не переживать разговор заново.
    /// </remarks>
    private async Task TranscribeCallAsync(CallSession session)
    {
        if (_callTranscriber is not { } transcriber)
        {
            _tray!.SetStatus(L.S.StatusModelStillLoading);
            return;
        }

        _tray!.SetStatus(L.S.StatusTranscribingCall);
        _tray.SetEngineBusy(true);
        try
        {
            var progress = new Progress<CallTranscriptionStage>(stage =>
                _tray.SetStatus(L.S.Describe(stage)));

            string path = await transcriber.TranscribeAsync(session, progress);
            _tray.SetStatus(string.Format(
                CultureInfo.CurrentCulture, L.S.StatusCallSaved, Path.GetFileName(session.Directory)));
            _tray.ShowBalloon(L.S.NotifyCallReadyTitle, path);
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось собрать транскрипт звонка.", ex);
            _tray.SetStatus(ex.Message);
        }
        finally
        {
            _tray.SetEngineBusy(false);
        }
    }

    private string CallsDirectory() => _settings.Current.CallsDirectory ?? AppPaths.DefaultCallsDirectory;

    private void OpenCallsFolder() => OpenFolder(CallsDirectory());

    private void OpenModelsFolder() => OpenFolder(
        ModelLocator.FindModelsDirectory(_settings.Current.ModelsDirectory) ?? AppPaths.DefaultModelsDirectory);

    /// <summary>
    /// Открыть папку в проводнике.
    /// </summary>
    /// <remarks>
    /// Путь приходит из settings.json, то есть из файла, который пользователь
    /// правит руками. Проверяем, что это действительно путь к папке: запуск
    /// оболочкой чего угодно из настроечного файла — не то поведение, которое
    /// стоит оставлять без присмотра.
    /// </remarks>
    private void OpenFolder(string directory)
    {
        try
        {
            if (!Path.IsPathFullyQualified(directory))
            {
                AppLog.Warn($"Отказ открыть «{directory}»: это не полный путь к папке.");
                return;
            }

            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directory}\"",
                UseShellExecute = false,
            });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Warn($"Не удалось открыть папку {directory}.", ex);
            _tray?.SetStatus(ex.Message);
        }
    }

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

    private void OpenSettings() => OpenSettings(SettingsSection.Dictation);

    private void OpenSettings(SettingsSection section)
    {
        if (_settingsWindow is { } existing)
        {
            existing.Activate();
            existing.GoTo(section);
            return;
        }

        var window = new SettingsWindow(_settings, AvailableModels, DeleteModelAsync);
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
        }

        if (change.AffectsTheme)
        {
            SystemTheme.Apply(change.Current.Theme);
        }

        if (change.AffectsHotkey)
        {
            StartHotkey();
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

        RefreshTrayFromSettings();

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
        RefreshTrayFromSettings();
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
        if (capturing)
        {
            _hotkey?.Dispose();
            _hotkey = null;
        }
        else
        {
            StartHotkey();
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

    private void OnAutoStartToggled(bool enabled)
    {
        if (!AutoStart.SetEnabled(enabled))
        {
            _tray!.SetStatus(L.S.FieldAutoStartUnavailable);
        }

        RefreshTrayFromSettings();
    }

    private void RefreshTrayFromSettings()
    {
        AppSettings settings = _settings.Current;
        _tray!.SetModels(AvailableModels(), settings.ModelFileName);
        _tray.SetAutoPaste(settings.AutoPaste);
        _tray.SetAutoStart(AutoStart.IsEnabled, AutoStart.IsAvailable);
        _tray.SetHistory(_controller?.History ?? []);
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

        StopCancelWatcher();

        // Запись звонка на выходе не бросаем: файлы дописываются и закрываются.
        _callRecorder.Dispose();

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

        _instanceMutex?.Dispose();
        _instanceMutex = null;

        GC.SuppressFinalize(this);
    }
}
