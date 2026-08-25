using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using TapRecorder.Core.Dictation;
using TapRecorder.Core.Settings;
using TapRecorder.Core.Speech;
using TapRecorder.Core.Windows;

// WinForms и WPF определяют типы с одинаковыми именами, а ImplicitUsings
// подключает пространства имён обоих. Явные псевдонимы снимают
// неоднозначность один раз на файл — иначе пришлось бы писать полное имя
// при каждом употреблении.
using Application = System.Windows.Application;

namespace TapRecorder.App;

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
    private const string InstanceMutexName = @"Local\TapRecorder.SingleInstance";

    private Mutex? _instanceMutex;
    private AppSettings _settings = new();
    private WhisperEngine? _engine;
    private DictationController? _controller;
    private HotkeyListener? _hotkey;
    private TrayIconHost? _tray;
    private OverlayWindow? _overlay;
    private DispatcherTimer? _elapsedTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _settings = SettingsStore.Load();

        _overlay = new OverlayWindow();
        _overlay.Clicked += () => _ = ToggleAsync();

        _tray = new TrayIconHost();
        _tray.ToggleRequested += () => _ = ToggleAsync();
        _tray.CopyLastRequested += CopyLastText;
        _tray.ExitRequested += Shutdown;
        _tray.OpenModelsFolderRequested += OpenModelsFolder;
        _tray.AutoPasteToggled += OnAutoPasteToggled;
        _tray.AutoStartToggled += OnAutoStartToggled;
        _tray.ModelSelected += OnModelSelected;
        _tray.RetryHotkeyRequested += StartHotkey;

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _elapsedTimer.Tick += (_, _) =>
        {
            if (_controller is { State: DictationState.Recording } controller)
            {
                _overlay!.UpdateElapsed(controller.Elapsed);
            }
        };

        RefreshTrayFromSettings();
        BuildEngineAndController();
        StartHotkey();
    }

    // --- сборка движка -----------------------------------------------------

    /// <summary>
    /// Создать движок и контроллер под текущие настройки.
    /// </summary>
    /// <remarks>
    /// Вызывается заново при смене модели: движок держит загруженную модель,
    /// и подменить её у живого объекта нельзя — проще пересобрать.
    /// </remarks>
    private void BuildEngineAndController()
    {
        DisposeEngineAndController();

        string? modelPath = ModelLocator.Resolve(_settings.ModelFileName)
                            ?? ModelLocator.ResolveAnyAvailable();

        if (modelPath is null)
        {
            _tray!.SetStatus("Модель не найдена");
            _tray.ShowBalloon(
                "Нет модели распознавания",
                $"Положи ggml-модель в {SettingsStore.ModelsDirectory} и выбери её в меню.");
            return;
        }

        // Если настроенной модели нет, а нашлась другая — честно запоминаем ту,
        // что реально используется, иначе меню показывало бы отмеченной не ту.
        string actualFileName = Path.GetFileName(modelPath);
        if (!string.Equals(actualFileName, _settings.ModelFileName, StringComparison.OrdinalIgnoreCase))
        {
            UpdateSettings(_settings with { ModelFileName = actualFileName });
        }

        _engine = new WhisperEngine(new WhisperEngineOptions
        {
            ModelPath = modelPath,
            Language = _settings.Language,
            Prompt = _settings.Prompt,
            IdleUnloadAfter = TimeSpan.FromMinutes(_settings.IdleUnloadMinutes),
        });

        _controller = new DictationController(_engine, () => _settings);
        _controller.StateChanged += state => OnUi(() => OnStateChanged(state));
        _controller.LevelChanged += level => OnUi(() => _overlay!.PushLevel(level));
        _controller.ProgressChanged += percent => OnUi(() => _overlay!.ShowTranscribing(percent));
        _controller.TextReady += text => OnUi(() => OnTextReady(text));
        _controller.Status += status => OnUi(() => _tray!.SetStatus(status));

        _tray!.SetStatus("Прогреваю модель…");
        WarmUpAsync();
    }

    /// <summary>Прогрев в фоне: к первой диктовке движок уже готов.</summary>
    private async void WarmUpAsync()
    {
        WhisperEngine engine = _engine!;
        try
        {
            var timer = Stopwatch.StartNew();
            await engine.LoadAsync().ConfigureAwait(true);
            _tray!.SetStatus($"Готов · {WhisperEngine.LoadedRuntime} · {timer.Elapsed.TotalSeconds:F1} с");
        }
        catch (Exception ex)
        {
            _tray!.SetStatus($"Модель не загрузилась: {ex.Message}");
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

        try
        {
            var listener = new HotkeyListener(_settings.Hotkey);

            // Событие приходит из потока listener'а — сразу уводим его в UI.
            listener.Pressed += () => OnUi(() => _ = ToggleAsync());
            listener.Start();
            _hotkey = listener;
            _tray!.SetHotkeyFailed(false);
        }
        catch (Exception ex)
        {
            _tray!.SetHotkeyFailed(true);
            _tray.SetStatus($"Хоткей {_settings.Hotkey} занят другим приложением");
            _tray.ShowBalloon(
                "Горячая клавиша занята",
                ex.Message + Environment.NewLine
                + "Освободи её и выбери в меню «Занять горячую клавишу заново».");
        }
    }

    // --- работа диктовки ---------------------------------------------------

    private async Task ToggleAsync()
    {
        if (_controller is null)
        {
            _tray!.ShowBalloon("Нечем распознавать", "Сначала выбери модель в меню.");
            return;
        }

        await _controller.ToggleAsync();
    }

    private void OnStateChanged(DictationState state)
    {
        _tray!.UpdateState(state);

        switch (state)
        {
            case DictationState.Recording:
                _overlay!.ShowRecording(_settings.Hotkey.ToString());
                _elapsedTimer!.Start();
                break;

            case DictationState.Transcribing:
                _elapsedTimer!.Stop();
                _overlay!.ShowTranscribing(0);
                break;

            case DictationState.Idle:
                _elapsedTimer!.Stop();

                // Если текст уже готов, вспышку показал OnTextReady — не
                // затираем её. Прячем только когда показывать нечего.
                if (_overlay!.IsVisible)
                {
                    _overlay.FlashAndHide("Готово");
                }

                break;
        }
    }

    private void OnTextReady(string text)
    {
        _tray!.SetLastTextAvailable(true);

        try
        {
            if (_settings.AutoPaste)
            {
                TextInserter.PasteViaClipboard(text, _settings.ExcludeFromClipboardHistory);
                _overlay!.FlashAndHide("✓ Вставлено");
            }
            else
            {
                ClipboardWriter.SetText(text, _settings.ExcludeFromClipboardHistory);
                _overlay!.FlashAndHide("✓ В буфере — Ctrl+V");
            }

            _tray.SetStatus(Preview(text));
        }
        catch (Exception ex)
        {
            // Текст уже в буфере: вставка могла не пройти из-за окна с правами
            // администратора. Пользователь не должен потерять надиктованное.
            _overlay!.FlashAndHide("В буфере — Ctrl+V");
            _tray.SetStatus(ex.Message);
        }
    }

    private void CopyLastText()
    {
        if (_controller?.LastText is not { } text)
        {
            return;
        }

        ClipboardWriter.SetText(text, _settings.ExcludeFromClipboardHistory);
        _tray!.SetStatus($"В буфере: {Preview(text)}");
    }

    // --- настройки ---------------------------------------------------------

    private void OnAutoPasteToggled(bool enabled)
    {
        UpdateSettings(_settings with { AutoPaste = enabled });
        _tray!.SetAutoPaste(enabled);
    }

    private void OnAutoStartToggled(bool enabled)
    {
        AutoStart.SetEnabled(enabled);
        _tray!.SetAutoStart(AutoStart.IsEnabled);
    }

    private void OnModelSelected(string fileName)
    {
        UpdateSettings(_settings with { ModelFileName = fileName });
        RefreshTrayFromSettings();
        BuildEngineAndController();
    }

    private void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        SettingsStore.Save(settings);
    }

    private void RefreshTrayFromSettings()
    {
        string? modelsDirectory = ModelLocator.FindModelsDirectory();
        IEnumerable<string> models = modelsDirectory is null
            ? []
            : Directory.EnumerateFiles(modelsDirectory, "ggml-*.bin")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !name.Contains("silero", StringComparison.OrdinalIgnoreCase))
                .Order();

        _tray!.SetModels(models, _settings.ModelFileName);
        _tray.SetAutoPaste(_settings.AutoPaste);
        _tray.SetAutoStart(AutoStart.IsEnabled);
    }

    private void OpenModelsFolder()
    {
        string directory = ModelLocator.FindModelsDirectory() ?? SettingsStore.ModelsDirectory;
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    // --- служебное ---------------------------------------------------------

    /// <summary>Выполнить действие в потоке интерфейса.</summary>
    private void OnUi(Action action) => Dispatcher.BeginInvoke(action);

    private static string Preview(string text)
    {
        string flat = text.ReplaceLineEndings(" ");
        return flat.Length <= 50 ? flat : flat[..50] + "…";
    }

    private void DisposeEngineAndController()
    {
        _controller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _controller = null;

        _engine?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _engine = null;
    }

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
        _elapsedTimer?.Stop();
        _elapsedTimer = null;

        _hotkey?.Dispose();
        _hotkey = null;

        DisposeEngineAndController();

        _tray?.Dispose();
        _tray = null;

        _instanceMutex?.Dispose();
        _instanceMutex = null;

        GC.SuppressFinalize(this);
    }
}
