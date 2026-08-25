using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TapRecorder.App.Localization;
using TapRecorder.Core.Dictation;

namespace TapRecorder.App;

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
    private readonly ToolStripMenuItem _copyLastItem;
    private readonly ToolStripMenuItem _retryHotkeyItem;
    private readonly ToolStripMenuItem _modelsItem;
    private readonly ToolStripMenuItem _autoPasteItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _modelsFolderItem;
    private readonly ToolStripMenuItem _exitItem;

    private readonly Icon _idleIcon;
    private readonly Icon _activeIcon;
    private readonly Icon _busyIcon;

    private DictationState _state = DictationState.Idle;
    private bool _engineBusy;

    public TrayIconHost()
    {
        _idleIcon = CreateDotIcon(Color.FromArgb(210, 210, 215));
        _activeIcon = CreateDotIcon(Color.FromArgb(255, 77, 77));

        // Третий цвет нужен не для красоты: загрузка модели занимает секунды,
        // и без видимого признака «работаю» это выглядит как зависание.
        _busyIcon = CreateDotIcon(Color.FromArgb(255, 176, 32));

        _statusItem = new ToolStripMenuItem { Enabled = false };

        _toggleItem = new ToolStripMenuItem();
        _toggleItem.Click += (_, _) => ToggleRequested?.Invoke();

        _copyLastItem = new ToolStripMenuItem { Enabled = false };
        _copyLastItem.Click += (_, _) => CopyLastRequested?.Invoke();

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

        _exitItem = new ToolStripMenuItem();
        _exitItem.Click += (_, _) => ExitRequested?.Invoke();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            _statusItem,
            new ToolStripSeparator(),
            _toggleItem,
            _copyLastItem,
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
            Text = "TapRecorder",
            Visible = true,
            ContextMenuStrip = menu,
        };

        // Двойной клик по иконке — то же, что хоткей: удобно, когда рук на
        // клавиатуре нет, а надо остановить запись.
        _icon.DoubleClick += (_, _) => ToggleRequested?.Invoke();

        ApplyLanguage();
    }

    public event Action? ToggleRequested;
    public event Action? CopyLastRequested;
    public event Action? ExitRequested;
    public event Action? OpenModelsFolderRequested;
    public event Action? SettingsRequested;
    public event Action? RetryHotkeyRequested;
    public event Action<bool>? AutoPasteToggled;
    public event Action<bool>? AutoStartToggled;
    public event Action<string>? ModelSelected;

    /// <summary>Перечитать все надписи из текущего языка.</summary>
    public void ApplyLanguage()
    {
        _copyLastItem.Text = L.S.TrayCopyLast;
        _retryHotkeyItem.Text = L.S.TrayRetryHotkey;
        _modelsItem.Text = L.S.TrayModel;
        _autoPasteItem.Text = L.S.TrayAutoPaste;
        _autoStartItem.Text = L.S.TrayAutoStart;
        _settingsItem.Text = L.S.TraySettings;
        _modelsFolderItem.Text = L.S.TrayModelsFolder;
        _exitItem.Text = L.S.TrayExit;

        if (string.IsNullOrEmpty(_statusItem.Text))
        {
            SetStatus(L.S.StatusReady);
        }

        ApplyVisualState();
    }

    /// <summary>Отразить состояние диктовки.</summary>
    public void UpdateState(DictationState state)
    {
        _state = state;
        ApplyVisualState();
    }

    /// <summary>Движок занят: грузится или переключается модель.</summary>
    public void SetEngineBusy(bool busy)
    {
        _engineBusy = busy;
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
            DictationState.Transcribing => _busyIcon,
            _ => _engineBusy ? _busyIcon : _idleIcon,
        };

        _toggleItem.Text = _state switch
        {
            DictationState.Recording => L.S.TrayStop,
            DictationState.Transcribing => L.S.TrayTranscribing,
            _ => _engineBusy ? L.S.TrayLoadingModel : L.S.TrayStart,
        };

        _toggleItem.Enabled = _state != DictationState.Transcribing && !_engineBusy;

        // Меню моделей во время переключения тоже блокируем: второй выбор,
        // пришедший поверх незавершённого первого, оставил бы настройку и
        // реально загруженную модель разными.
        _modelsItem.Enabled = !_engineBusy && _state == DictationState.Idle;
    }

    /// <summary>Строка состояния вверху меню и всплывающая подсказка иконки.</summary>
    public void SetStatus(string status)
    {
        _statusItem.Text = status;

        // NotifyIcon.Text ограничен 63 символами — более длинное значение
        // роняет установку свойства исключением.
        _icon.Text = status.Length <= 63 ? status : status[..60] + "…";
    }

    public void SetLastTextAvailable(bool available) => _copyLastItem.Enabled = available;

    public void SetAutoPaste(bool enabled) => _autoPasteItem.Checked = enabled;

    public void SetAutoStart(bool enabled) => _autoStartItem.Checked = enabled;

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

    public void ShowBalloon(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, ToolTipIcon.Warning);

    /// <summary>«ggml-podlodka-turbo-q8_0.bin» → «podlodka-turbo-q8_0».</summary>
    private static string PrettyModelName(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        return name.StartsWith("ggml-", StringComparison.OrdinalIgnoreCase) ? name[5..] : name;
    }

    /// <summary>
    /// Нарисовать иконку-кружок нужного цвета.
    /// </summary>
    /// <remarks>
    /// Иконка рисуется, а не лежит файлом: их всего три, и генерация избавляет
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
        _busyIcon.Dispose();
    }
}
