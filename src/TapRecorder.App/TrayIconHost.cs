using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
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
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _autoPasteItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _copyLastItem;
    private readonly ToolStripMenuItem _modelsItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _retryHotkeyItem;
    private readonly Icon _idleIcon;
    private readonly Icon _activeIcon;

    public TrayIconHost()
    {
        _idleIcon = CreateDotIcon(Color.FromArgb(210, 210, 215));
        _activeIcon = CreateDotIcon(Color.FromArgb(255, 77, 77));

        _statusItem = new ToolStripMenuItem("Готов") { Enabled = false };
        _toggleItem = new ToolStripMenuItem("Начать диктовку", null, (_, _) => ToggleRequested?.Invoke());
        _copyLastItem = new ToolStripMenuItem("Скопировать последний текст", null, (_, _) => CopyLastRequested?.Invoke())
        {
            Enabled = false,
        };

        // Обработчик вешаем ОТДЕЛЬНО от создания: лямбда читает состояние
        // самого пункта, а внутри инициализатора поле ещё не присвоено —
        // анализатор nullable справедливо на это ругается.
        // CheckOnClick не включаем: галочку ставит настройка, а не сам клик,
        // иначе при отказе сохранить настройку меню разошлось бы с реальностью.
        _autoPasteItem = new ToolStripMenuItem("Вставлять автоматически");
        _autoPasteItem.Click += (_, _) => AutoPasteToggled?.Invoke(!_autoPasteItem.Checked);

        _autoStartItem = new ToolStripMenuItem("Запускать при входе в систему");
        _autoStartItem.Click += (_, _) => AutoStartToggled?.Invoke(!_autoStartItem.Checked);

        _modelsItem = new ToolStripMenuItem("Модель");

        // Хоткей мог быть занят чужим процессом в момент запуска. Требовать
        // ради этого перезапуск приложения — плохо: даём переиграть на месте.
        _retryHotkeyItem = new ToolStripMenuItem("Занять горячую клавишу заново", null,
            (_, _) => RetryHotkeyRequested?.Invoke())
        {
            Visible = false,
        };

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
            new ToolStripMenuItem("Папка моделей…", null, (_, _) => OpenModelsFolderRequested?.Invoke()),
            new ToolStripMenuItem("Выход", null, (_, _) => ExitRequested?.Invoke()),
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
    }

    public event Action? ToggleRequested;
    public event Action? CopyLastRequested;
    public event Action? ExitRequested;
    public event Action? OpenModelsFolderRequested;
    public event Action<bool>? AutoPasteToggled;
    public event Action<bool>? AutoStartToggled;
    public event Action<string>? ModelSelected;
    public event Action? RetryHotkeyRequested;

    /// <summary>Показать пункт «занять заново» — только когда хоткей не наш.</summary>
    public void SetHotkeyFailed(bool failed) => _retryHotkeyItem.Visible = failed;

    /// <summary>Отразить состояние диктовки в иконке и пункте меню.</summary>
    public void UpdateState(DictationState state)
    {
        _icon.Icon = state == DictationState.Recording ? _activeIcon : _idleIcon;
        _toggleItem.Text = state switch
        {
            DictationState.Recording => "Закончить диктовку",
            DictationState.Transcribing => "Распознаю…",
            _ => "Начать диктовку",
        };

        _toggleItem.Enabled = state != DictationState.Transcribing;
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
            _modelsItem.DropDownItems.Add(new ToolStripMenuItem("Моделей не найдено") { Enabled = false });
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
    /// Иконка рисуется, а не лежит файлом: их всего две (покой и запись), и
    /// генерация избавляет от бинарников в репозитории. HICON, который отдаёт
    /// <c>GetHicon</c>, не принадлежит .NET — делаем управляемую копию через
    /// <c>Clone</c> и сразу освобождаем дескриптор, иначе он утекает.
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
    }
}
