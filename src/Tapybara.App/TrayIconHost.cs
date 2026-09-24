using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Tapybara.App.Localization;
using Tapybara.Core.Dictation;

namespace Tapybara.App;

/// <summary>Насколько срочно уведомление.</summary>
public enum BalloonKind
{
    Info,
    Warning,
}

/// <summary>
/// Иконка в системном трее и её меню.
/// </summary>
/// <remarks>
/// Используется <c>NotifyIcon</c> из WinForms: своего трея у WPF нет, и это
/// стандартный способ. Никакой другой части WinForms приложение не использует.
/// </remarks>
public sealed partial class TrayIconHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _cancelItem;
    private readonly ToolStripMenuItem _historyItem;
    private readonly ToolStripMenuItem _retryHotkeyItem;
    private readonly ToolStripMenuItem _modelsItem;
    private readonly ToolStripMenuItem _autoPasteItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _modelsFolderItem;
    private readonly ToolStripMenuItem _recordCallItem;
    private readonly ToolStripMenuItem _callsItem;
    private readonly ToolStripMenuItem _exitItem;

    private Icon _idleIcon;
    private Icon _activeIcon;
    private Icon _callIcon;
    private Icon _busyIcon;

    /// <summary>Куда ведёт щелчок по текущему уведомлению.</summary>
    /// <remarks>
    /// У каждого уведомления своё место назначения. Прежде щелчок по любому
    /// из них вёл в одно и то же место — журнал, если модели есть, — и
    /// «Транскрипт готов» открывал текстовый файл с отладкой вместо звонка.
    /// </remarks>
    private Action? _balloonAction;

    private DictationState _state = DictationState.Idle;
    private bool _engineBusy;
    private bool _recordingCall;
    private bool _transcribingCall;

    public TrayIconHost()
    {
        (_idleIcon, _activeIcon, _callIcon, _busyIcon) = BuildIcons();

        _statusItem = new ToolStripMenuItem { Enabled = false };

        _toggleItem = new ToolStripMenuItem();
        _toggleItem.Click += (_, _) => ToggleRequested?.Invoke();

        // Отмена отдельным пунктом. Раньше во время распознавания меню
        // предлагало только неактивный «Распознаю…», а прервать затянувшуюся
        // работу можно было исключительно клавишей Escape — и то, если её
        // удалось занять.
        _cancelItem = new ToolStripMenuItem { Visible = false };
        _cancelItem.Click += (_, _) => CancelRequested?.Invoke();

        _historyItem = new ToolStripMenuItem();

        // Хоткей мог быть занят чужим процессом в момент запуска. Требовать
        // ради этого перезапуск приложения — плохо: даём переиграть на месте.
        _retryHotkeyItem = new ToolStripMenuItem { Visible = false };
        _retryHotkeyItem.Click += (_, _) => RetryHotkeyRequested?.Invoke();

        _modelsItem = new ToolStripMenuItem();

        // Обработчик вешаем ОТДЕЛЬНО от создания: лямбда читает состояние
        // самого пункта, а внутри инициализатора поле ещё не присвоено.
        // CheckOnClick не включаем: галочку ставит настройка, а не сам клик.
        _autoPasteItem = new ToolStripMenuItem();
        _autoPasteItem.Click += (_, _) => AutoPasteToggled?.Invoke(!_autoPasteItem.Checked);

        _autoStartItem = new ToolStripMenuItem();
        _autoStartItem.Click += (_, _) => AutoStartToggled?.Invoke(!_autoStartItem.Checked);

        _settingsItem = new ToolStripMenuItem();
        _settingsItem.Click += (_, _) => SettingsRequested?.Invoke();

        _modelsFolderItem = new ToolStripMenuItem();
        _modelsFolderItem.Click += (_, _) => OpenModelsFolderRequested?.Invoke();

        _recordCallItem = new ToolStripMenuItem();
        _recordCallItem.Click += (_, _) => RecordCallRequested?.Invoke();

        _callsItem = new ToolStripMenuItem();
        _callsItem.Click += (_, _) => CallsRequested?.Invoke();

        _exitItem = new ToolStripMenuItem();
        _exitItem.Click += (_, _) => ExitRequested?.Invoke();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _statusItem,
            new ToolStripSeparator(),
            _toggleItem,
            _cancelItem,
            _historyItem,
            new ToolStripSeparator(),
            _recordCallItem,
            _callsItem,
            new ToolStripSeparator(),
            _retryHotkeyItem,
            _modelsItem,
            _autoPasteItem,
            _autoStartItem,
            new ToolStripSeparator(),
            _settingsItem,
            _modelsFolderItem,
            _exitItem,
        ]);

        _icon = new NotifyIcon
        {
            Icon = _idleIcon,
            Text = "Tapybara",
            Visible = true,
            ContextMenuStrip = menu,
        };

        // Щелчок по иконке открывает окно, а не микрофон. Двойной щелчок
        // раньше включал диктовку, и это худшее, что может сделать
        // случайный клик: запись, о которой человек не знает. Остановить
        // идущую запись можно хоткеем, пилюлей и пунктом меню.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenRequested?.Invoke();
            }
        };

        _icon.BalloonTipClicked += (_, _) =>
        {
            Action? action = _balloonAction;
            _balloonAction = null;
            action?.Invoke();
        };
        _icon.BalloonTipClosed += (_, _) => _balloonAction = null;
        menu.Opening += (_, _) => MenuOpening?.Invoke();

        ApplyLanguage();
    }

    public event Action? ToggleRequested;
    public event Action? CancelRequested;
    public event Action? ExitRequested;
    public event Action? OpenModelsFolderRequested;
    public event Action? CallsRequested;
    public event Action? RecordCallRequested;
    public event Action? SettingsRequested;
    public event Action? RetryHotkeyRequested;

    /// <summary>Щелчок левой кнопкой по иконке.</summary>
    public event Action? OpenRequested;

    /// <summary>
    /// Меню вот-вот откроется — самое время перечитать, что на диске.
    /// </summary>
    /// <remarks>
    /// Страховка к наблюдению за папкой: событие файловой системы может не
    /// прийти (сетевой диск, права), а меню человек открывает всегда. Стоит
    /// это одного перечисления каталога.
    /// </remarks>
    public event Action? MenuOpening;
    public event Action<bool>? AutoPasteToggled;
    public event Action<bool>? AutoStartToggled;
    public event Action<string>? ModelSelected;
    public event Action<string>? HistoryItemSelected;

    /// <summary>Перечитать все надписи из текущего языка.</summary>
    public void ApplyLanguage()
    {
        _cancelItem.Text = L.S.TrayCancel;
        _historyItem.Text = L.S.TrayHistory;
        _retryHotkeyItem.Text = L.S.TrayRetryHotkey;
        _modelsItem.Text = L.S.TrayModel;
        _autoPasteItem.Text = L.S.TrayAutoPaste;
        _autoStartItem.Text = L.S.TrayAutoStart;
        _settingsItem.Text = L.S.TraySettings;
        _modelsFolderItem.Text = L.S.TrayModelsFolder;
        _callsItem.Text = L.S.TrayCalls;
        _exitItem.Text = L.S.TrayExit;

        if (string.IsNullOrEmpty(_statusItem.Text))
        {
            SetStatus(L.S.StatusReady);
        }

        ApplyVisualState();
    }

    /// <summary>Перерисовать иконки под сменившуюся тему системы.</summary>
    public void ApplyTheme()
    {
        Icon oldIdle = _idleIcon;
        Icon oldActive = _activeIcon;
        Icon oldCall = _callIcon;
        Icon oldBusy = _busyIcon;

        (_idleIcon, _activeIcon, _callIcon, _busyIcon) = BuildIcons();
        ApplyVisualState();

        oldCall.Dispose();

        oldIdle.Dispose();
        oldActive.Dispose();
        oldBusy.Dispose();
    }

    /// <summary>Отразить состояние диктовки.</summary>
    public void UpdateState(DictationState state)
    {
        _state = state;
        ApplyVisualState();
    }

    /// <summary>Идёт ли запись звонка.</summary>
    public void SetRecordingCall(bool recording)
    {
        _recordingCall = recording;
        ApplyVisualState();
    }

    /// <summary>Движок занят: грузится или переключается модель.</summary>
    public void SetEngineBusy(bool busy)
    {
        _engineBusy = busy;
        ApplyVisualState();
    }

    /// <summary>
    /// Распознаётся записанный звонок.
    /// </summary>
    /// <remarks>
    /// Не то же самое, что <see cref="SetEngineBusy"/>. Раньше распознавание
    /// звонка выдавало себя за загрузку модели: на несколько минут пункт
    /// «Начать диктовку» становился серым «Загружаю модель…», хотя диктовать
    /// можно — движок берёт диктовку между кусками звонка.
    /// </remarks>
    public void SetCallTranscribing(bool transcribing)
    {
        _transcribingCall = transcribing;
        ApplyVisualState();
    }

    /// <summary>
    /// Свести состояние диктовки и занятость движка к одной картинке.
    /// </summary>
    /// <remarks>
    /// Единая точка вычисления вида: два независимых флага, каждый из которых
    /// правит иконку сам по себе, неизбежно разъезжаются — второй затирает
    /// первый в зависимости от порядка вызовов.
    /// </remarks>
    private void ApplyVisualState()
    {
        _icon.Icon = _state switch
        {
            DictationState.Recording => _activeIcon,
            _ when _recordingCall => _callIcon,
            DictationState.Transcribing => _busyIcon,
            _ => _engineBusy || _transcribingCall ? _busyIcon : _idleIcon,
        };

        _recordCallItem.Text = _recordingCall ? L.S.TrayStopRecording : L.S.TrayStartRecording;

        _toggleItem.Text = _state switch
        {
            DictationState.Recording => L.S.TrayStop,
            DictationState.Transcribing => L.S.TrayTranscribing,
            _ => _engineBusy ? L.S.TrayLoadingModel : L.S.TrayStart,
        };

        _toggleItem.Enabled = _state != DictationState.Transcribing && !_engineBusy;
        _cancelItem.Visible = _state is DictationState.Recording or DictationState.Transcribing;

        // Меню моделей во время переключения тоже блокируем: второй выбор,
        // пришедший поверх незавершённого первого, оставил бы настройку и
        // реально загруженную модель разными.
        _modelsItem.Enabled = !_engineBusy && _state == DictationState.Idle;
    }

    /// <summary>Строка состояния вверху меню и всплывающая подсказка иконки.</summary>
    /// <remarks>
    /// Статус обрезается жёстко. Меню растягивается по самому длинному пункту:
    /// одна длинная строка раздувала его на пол-экрана и делала неудобным
    /// всё остальное.
    /// </remarks>
    public void SetStatus(string status)
    {
        _statusItem.Text = Shorten(status, 46);

        // NotifyIcon.Text ограничен 63 символами — более длинное значение
        // роняет установку свойства исключением.
        _icon.Text = Shorten(status, 62);
    }

    /// <summary>Показать сочетание клавиш справа от пункта запуска.</summary>
    /// <remarks>
    /// Задаём только текст, но НЕ <c>ShortcutKeys</c>: тот заставил бы WinForms
    /// самому перехватывать сочетание, а глобальный хоткей у нас уже занят
    /// через RegisterHotKey — получили бы двойную обработку одного нажатия.
    /// </remarks>
    public void SetHotkeyDisplay(string hotkey)
    {
        _toggleItem.ShortcutKeyDisplayString = hotkey;
        _toggleItem.ShowShortcutKeys = true;
    }

    public void SetCallHotkeyDisplay(string hotkey)
    {
        _recordCallItem.ShortcutKeyDisplayString = hotkey;
        _recordCallItem.ShowShortcutKeys = true;
    }

    private static string Shorten(string text, int limit)
    {
        string flat = text.ReplaceLineEndings(" ");
        return flat.Length <= limit ? flat : flat[..(limit - 1)] + "…";
    }

    /// <summary>Наполнить подменю последних диктовок.</summary>
    public void SetHistory(IReadOnlyList<string> history)
    {
        _historyItem.DropDownItems.Clear();

        if (history.Count == 0)
        {
            _historyItem.DropDownItems.Add(new ToolStripMenuItem(L.S.TrayHistoryEmpty) { Enabled = false });
            _historyItem.Enabled = false;
            return;
        }

        _historyItem.Enabled = true;
        foreach (string text in history)
        {
            string captured = text;
            _historyItem.DropDownItems.Add(new ToolStripMenuItem(
                Shorten(text, 60),
                null,
                (_, _) => HistoryItemSelected?.Invoke(captured)));
        }
    }

    public void SetAutoPaste(bool enabled) => _autoPasteItem.Checked = enabled;

    public void SetAutoStart(bool enabled, bool available)
    {
        _autoStartItem.Checked = enabled;
        _autoStartItem.Enabled = available;
    }

    public void SetHotkeyFailed(bool failed) => _retryHotkeyItem.Visible = failed;

    /// <summary>Заполнить подменю выбора модели.</summary>
    public void SetModels(IEnumerable<string> fileNames, string selected)
    {
        _modelsItem.DropDownItems.Clear();

        foreach (string fileName in fileNames)
        {
            string captured = fileName;
            _modelsItem.DropDownItems.Add(new ToolStripMenuItem(
                PrettyModelName(fileName),
                null,
                (_, _) => ModelSelected?.Invoke(captured))
            {
                Checked = string.Equals(fileName, selected, StringComparison.OrdinalIgnoreCase),
            });
        }

        if (_modelsItem.DropDownItems.Count == 0)
        {
            _modelsItem.DropDownItems.Add(new ToolStripMenuItem(L.S.TrayNoModels) { Enabled = false });
        }
    }

    /// <summary>Всплывающее уведомление.</summary>
    /// <remarks>
    /// Значок — параметр, а не константа. Раньше всё показывалось с жёлтым
    /// треугольником, включая «Транскрипт готов»: успех выглядел так же
    /// тревожно, как отказ.
    /// </remarks>
    /// <summary>Показать уведомление.</summary>
    /// <param name="title">Заголовок.</param>
    /// <param name="message">Текст.</param>
    /// <param name="kind">Насколько срочно.</param>
    /// <param name="onClick">
    /// Что открыть по щелчку. <c>null</c> — щелчок просто закрывает
    /// уведомление: лучше ничего, чем место, не связанное с тем, о чём оно.
    /// </param>
    public void ShowBalloon(
        string title,
        string message,
        BalloonKind kind = BalloonKind.Info,
        Action? onClick = null)
    {
        _balloonAction = onClick;
        _icon.ShowBalloonTip(
            5000,
            title,
            message,
            kind == BalloonKind.Warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    /// <summary>«ggml-large-v3-turbo-q5_0.bin» → «large-v3-turbo-q5_0».</summary>
    private static string PrettyModelName(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        return name.StartsWith("ggml-", StringComparison.OrdinalIgnoreCase) ? name[5..] : name;
    }

    /// <summary>
    /// Иконки состояний под текущую тему панели задач.
    /// </summary>
    /// <remarks>
    /// Цвет покоя раньше был почти белым — на светлой панели задач иконка
    /// становилась невидимой, и приложение выглядело незапущенным. Активные
    /// состояния цветные и читаются на любом фоне, а вот нейтральное
    /// приходится выбирать под тему.
    /// <para>
    /// Красный — идёт запись, и ничего больше; янтарный — идёт работа.
    /// Звонок раньше горел янтарным, тем же, что «грузится модель», и
    /// часовая запись разговора выглядела в трее как затянувшаяся загрузка.
    /// Звонок — красное кольцо, диктовка — сплошной круг: во время звонка
    /// можно диктовать, и сплошной круг поверх кольца говорит, что сейчас
    /// слушают ещё и ради текста.
    /// </para>
    /// </remarks>
    private static (Icon Idle, Icon Active, Icon Call, Icon Busy) BuildIcons()
    {
        Color idle = SystemTheme.IsTaskbarLight
            ? Color.FromArgb(64, 64, 70)
            : Color.FromArgb(210, 210, 215);

        Color recording = Color.FromArgb(255, 77, 77);

        return (
            CreateDotIcon(idle),
            CreateDotIcon(recording),
            CreateRingIcon(recording),
            // Янтарный нужен не для красоты: загрузка модели занимает секунды,
            // и без видимого признака «работаю» это выглядит как зависание.
            CreateDotIcon(Color.FromArgb(255, 176, 32)));
    }

    /// <summary>
    /// Нарисовать иконку-кружок нужного цвета.
    /// </summary>
    /// <remarks>
    /// Иконки рисуются, а не лежат файлами: их всего четыре, и генерация избавляет
    /// от бинарников в репозитории. HICON, который отдаёт <c>GetHicon</c>, не
    /// принадлежит .NET — делаем управляемую копию через <c>Clone</c> и сразу
    /// освобождаем дескриптор, иначе он утекает.
    /// </remarks>
    private static Icon CreateDotIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 5, 5, 22, 22);
        }

        return ToIcon(bitmap);
    }

    /// <summary>Кольцо с точкой в центре — запись звонка.</summary>
    /// <remarks>
    /// Толщина кольца подобрана под 16 пикселей, до которых Windows ужимает
    /// иконку при масштабе 100%: тоньше — и кольцо на панели задач
    /// читается как серый кружок.
    /// </remarks>
    private static Icon CreateRingIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var pen = new Pen(color, 4.5f);
            graphics.DrawEllipse(pen, 5.5f, 5.5f, 21, 21);
            using var brush = new SolidBrush(color);
            graphics.FillEllipse(brush, 11.5f, 11.5f, 9, 9);
        }

        return ToIcon(bitmap);
    }

    private static Icon ToIcon(Bitmap bitmap)
    {
        nint handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _idleIcon.Dispose();
        _activeIcon.Dispose();
        _callIcon.Dispose();
        _busyIcon.Dispose();
    }
}
