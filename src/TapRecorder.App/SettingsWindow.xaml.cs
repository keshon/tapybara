using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TapRecorder.App.Localization;
using TapRecorder.Core.Settings;
using TapRecorder.Core.Windows;

// Те же коллизии имён между WinForms и WPF, что и в остальном приложении.
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace TapRecorder.App;

/// <summary>Окно настроек.</summary>
/// <remarks>
/// Строки собираются кодом, а не размечаются в XAML: полей много, они
/// однотипные, и один построитель строки честнее, чем две сотни строк
/// копипасты с расходящимися отступами.
/// </remarks>
public partial class SettingsWindow : Window
{
    private readonly IReadOnlyList<string> _models;
    private AppSettings _settings;

    private Button _hotkeyButton = null!;
    private TextBox _promptBox = null!;
    private TextBox _modelsFolderBox = null!;
    private bool _capturingHotkey;

    public SettingsWindow(AppSettings settings, IReadOnlyList<string> models)
    {
        _settings = settings;
        _models = models;

        InitializeComponent();

        Title = L.S.SettingsTitle;
        CloseButton.Content = L.S.ButtonClose;
        FooterHint.Text = L.S.FieldHotkeyHint;

        BuildRecognitionSection();
        BuildInputSection();
        BuildTextSection();
        BuildStorageSection();
        BuildInterfaceSection();
    }

    /// <summary>Настройки изменились — вызывающий код решает, что пересобирать.</summary>
    public event Action<AppSettings>? SettingsChanged;

    /// <summary>
    /// Идёт захват нового сочетания клавиш.
    /// </summary>
    /// <remarks>
    /// На это время глобальный хоткей надо снять: иначе нажатие текущего
    /// сочетания перехватит система и запустит диктовку вместо того, чтобы
    /// дать нам его записать.
    /// </remarks>
    public event Action<bool>? HotkeyCaptureChanged;

    /// <summary>Сменился язык интерфейса — окно и меню надо пересоздать.</summary>
    public event Action? LanguageChanged;

    // --- разделы -----------------------------------------------------------

    private void BuildRecognitionSection()
    {
        AddCaption(L.S.SectionRecognition);

        var modelBox = new ComboBox { MinWidth = 260 };
        foreach (string model in _models)
        {
            modelBox.Items.Add(PrettyModelName(model));
        }

        int selected = _models.ToList().FindIndex(
            m => string.Equals(m, _settings.ModelFileName, StringComparison.OrdinalIgnoreCase));
        modelBox.SelectedIndex = selected >= 0 ? selected : 0;
        modelBox.SelectionChanged += (_, _) =>
        {
            if (modelBox.SelectedIndex >= 0 && modelBox.SelectedIndex < _models.Count)
            {
                Apply(_settings with { ModelFileName = _models[modelBox.SelectedIndex] });
            }
        };

        AddRow(L.S.FieldModel, hint: null, modelBox);

        var languageBox = new TextBox { Text = _settings.Language, Width = 90 };
        languageBox.LostFocus += (_, _) =>
        {
            string value = languageBox.Text.Trim().ToLowerInvariant();
            if (value.Length > 0)
            {
                Apply(_settings with { Language = value });
            }
        };

        AddRow(L.S.FieldRecognitionLanguage, L.S.FieldRecognitionLanguageHint, languageBox);

        _promptBox = new TextBox
        {
            Text = _settings.Prompt ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 60,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        _promptBox.LostFocus += (_, _) =>
        {
            string value = _promptBox.Text.Trim();
            Apply(_settings with { Prompt = value.Length == 0 ? null : value });
        };

        var resetPrompt = new Button { Content = L.S.ButtonDefault, MinWidth = 96 };
        resetPrompt.Click += (_, _) =>
        {
            _promptBox.Text = LanguageDefaults.DefaultPrompt(_settings.Language);
            Apply(_settings with { Prompt = _promptBox.Text });
        };

        AddStackedRow(L.S.FieldPrompt, L.S.FieldPromptHint, _promptBox, resetPrompt);

        AddRow(
            L.S.FieldIdleUnload,
            hint: null,
            NumberBox(
                _settings.IdleUnloadMinutes,
                L.S.Minutes,
                value => Apply(_settings with { IdleUnloadMinutes = Math.Clamp((int)value, 1, 240) })));
    }

    private void BuildInputSection()
    {
        AddCaption(L.S.SectionInput);

        _hotkeyButton = new Button { Content = _settings.Hotkey.ToString(), MinWidth = 180 };
        _hotkeyButton.Click += (_, _) => BeginHotkeyCapture();
        AddRow(L.S.FieldHotkey, L.S.FieldHotkeyHint, _hotkeyButton);

        AddCheckRow(
            L.S.FieldAutoPaste,
            hint: null,
            _settings.AutoPaste,
            value => Apply(_settings with { AutoPaste = value }));

        AddCheckRow(
            L.S.FieldClipboardHistory,
            hint: null,
            _settings.ExcludeFromClipboardHistory,
            value => Apply(_settings with { ExcludeFromClipboardHistory = value }));
    }

    private void BuildTextSection()
    {
        AddCaption(L.S.SectionText);

        AddCheckRow(
            L.S.FieldSplitParagraphs,
            hint: null,
            _settings.SplitParagraphsByPauses,
            value => Apply(_settings with { SplitParagraphsByPauses = value }));

        AddRow(
            L.S.FieldParagraphPause,
            hint: null,
            NumberBox(
                _settings.ParagraphPauseSeconds,
                L.S.Seconds,
                value => Apply(_settings with { ParagraphPauseSeconds = Math.Clamp(value, 0.2, 10) })));

        var replacementsBox = new TextBox
        {
            Text = FormatReplacements(_settings.Replacements),
            AcceptsReturn = true,
            MinHeight = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
        };

        replacementsBox.LostFocus += (_, _) =>
            Apply(_settings with { Replacements = ParseReplacements(replacementsBox.Text) });

        AddStackedRow(L.S.FieldReplacements, L.S.FieldReplacementsHint, replacementsBox, trailing: null);
    }

    private void BuildStorageSection()
    {
        AddCaption(L.S.SectionStorage);

        _modelsFolderBox = new TextBox
        {
            Text = ModelLocator.FindModelsDirectory(_settings.ModelsDirectory) ?? AppPaths.DefaultModelsDirectory,
            IsReadOnly = true,
        };

        var browse = new Button { Content = L.S.ButtonBrowse, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = _modelsFolderBox.Text };
            if (dialog.ShowDialog(this) == true)
            {
                _modelsFolderBox.Text = dialog.FolderName;
                Apply(_settings with { ModelsDirectory = dialog.FolderName });
            }
        };

        var open = new Button { Content = L.S.ButtonOpen, MinWidth = 96 };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(_modelsFolderBox.Text);
            Process.Start(new ProcessStartInfo(_modelsFolderBox.Text) { UseShellExecute = true });
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(browse);
        buttons.Children.Add(open);

        AddStackedRow(L.S.FieldModelsFolder, hint: null, _modelsFolderBox, buttons);

        AddCheckRow(
            L.S.FieldPortable,
            L.S.FieldPortableHint + " " + L.S.RestartRequired + ".",
            AppPaths.IsPortable,
            AppPaths.SetPortable);
    }

    private void BuildInterfaceSection()
    {
        AddCaption(L.S.SectionInterface);

        var languageBox = new ComboBox { MinWidth = 200 };
        languageBox.Items.Add(L.S.FieldUiLanguageAuto);
        languageBox.Items.Add(L.S.LanguageEnglish);
        languageBox.Items.Add(L.S.LanguageRussian);
        languageBox.SelectedIndex = _settings.UiLanguage switch
        {
            "en" => 1,
            "ru" => 2,
            _ => 0,
        };

        languageBox.SelectionChanged += (_, _) =>
        {
            string? value = languageBox.SelectedIndex switch
            {
                1 => "en",
                2 => "ru",
                _ => null,
            };

            if (value == _settings.UiLanguage)
            {
                return;
            }

            Apply(_settings with { UiLanguage = value });
            L.Use(value);
            LanguageChanged?.Invoke();
        };

        AddRow(L.S.FieldUiLanguage, hint: null, languageBox);
    }

    // --- захват сочетания клавиш -------------------------------------------

    private void BeginHotkeyCapture()
    {
        _capturingHotkey = true;
        _hotkeyButton.Content = L.S.FieldHotkeyCapturing;
        HotkeyCaptureChanged?.Invoke(true);
    }

    private void EndHotkeyCapture()
    {
        _capturingHotkey = false;
        _hotkeyButton.Content = _settings.Hotkey.ToString();
        HotkeyCaptureChanged?.Invoke(false);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturingHotkey)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;

        // Alt-сочетания приходят как Key.System — настоящая клавиша в SystemKey.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            EndHotkeyCapture();
            return;
        }

        // Сами модификаторы сочетанием не являются — ждём основную клавишу.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        var combo = new HotkeyCombo(modifiers, (ushort)KeyInterop.VirtualKeyFromKey(key));

        // Один Shift не годится: глобальный хоткей на ⇧D отнял бы у системы
        // ввод заглавной D во всех приложениях сразу.
        if (!combo.IsUsableAsGlobal)
        {
            _hotkeyButton.Content = L.S.FieldHotkeyCapturing;
            return;
        }

        Apply(_settings with { Hotkey = combo });
        EndHotkeyCapture();
    }

    // --- построители строк -------------------------------------------------

    private void AddCaption(string text) => Sections.Children.Add(new TextBlock
    {
        Text = text,
        FontSize = 10.5,
        FontWeight = FontWeights.SemiBold,
        Opacity = 0.6,
        Margin = new Thickness(0, 18, 0, 8),
    });

    /// <summary>Строка «подпись слева — контрол справа».</summary>
    private void AddRow(string label, string? hint, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        if (hint is not null)
        {
            labels.Children.Add(HintBlock(hint));
        }

        Grid.SetColumn(labels, 0);
        grid.Children.Add(labels);

        if (control is FrameworkElement element)
        {
            element.VerticalAlignment = VerticalAlignment.Center;
            element.Margin = new Thickness(16, 0, 0, 0);
        }

        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        Sections.Children.Add(grid);
    }

    /// <summary>Строка, где контрол занимает всю ширину под подписью.</summary>
    private void AddStackedRow(string label, string? hint, UIElement control, UIElement? trailing)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        if (hint is not null)
        {
            panel.Children.Add(HintBlock(hint));
        }

        panel.Children.Add(control);
        if (trailing is not null)
        {
            if (trailing is FrameworkElement element && element.Margin.Top == 0)
            {
                element.Margin = new Thickness(0, 6, 0, 0);
                element.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            }

            panel.Children.Add(trailing);
        }

        Sections.Children.Add(panel);
    }

    private void AddCheckRow(string label, string? hint, bool value, Action<bool> onChange)
    {
        var check = new CheckBox { IsChecked = value, Content = label, Margin = new Thickness(0, 0, 0, 4) };
        check.Checked += (_, _) => onChange(true);
        check.Unchecked += (_, _) => onChange(false);

        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(check);
        if (hint is not null)
        {
            panel.Children.Add(HintBlock(hint));
        }

        Sections.Children.Add(panel);
    }

    private static TextBlock HintBlock(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Opacity = 0.65,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 0),
    };

    /// <summary>Поле для числа с подписью единиц измерения.</summary>
    private static StackPanel NumberBox(double initial, string unit, Action<double> onChange)
    {
        var box = new TextBox
        {
            Text = initial.ToString(CultureInfo.CurrentCulture),
            Width = 70,
            TextAlignment = TextAlignment.Right,
        };

        box.LostFocus += (_, _) =>
        {
            // Принимаем и запятую, и точку: раскладка и локаль пользователя
            // не должны решать, применится ли настройка.
            string normalized = box.Text.Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                onChange(value);
            }
            else
            {
                box.Text = initial.ToString(CultureInfo.CurrentCulture);
            }
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(box);
        panel.Children.Add(new TextBlock
        {
            Text = unit,
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        });

        return panel;
    }

    // --- словарь замен -----------------------------------------------------

    private static string FormatReplacements(IReadOnlyDictionary<string, string> replacements)
    {
        var builder = new StringBuilder();
        foreach ((string from, string to) in replacements)
        {
            builder.Append(from).Append(" = ").AppendLine(to);
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Разобрать строки вида «услышано = правильно».</summary>
    private static Dictionary<string, string> ParseReplacements(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in text.Split('\n'))
        {
            // Делим по ПЕРВОМУ знаку равенства: в правой части он вполне
            // может встретиться как часть текста замены.
            int separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            string from = line[..separator].Trim();
            string to = line[(separator + 1)..].Trim();
            if (from.Length > 0)
            {
                result[from] = to;
            }
        }

        return result;
    }

    private static string PrettyModelName(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        return name.StartsWith("ggml-", StringComparison.OrdinalIgnoreCase) ? name[5..] : name;
    }

    private void Apply(AppSettings settings)
    {
        _settings = settings;
        SettingsChanged?.Invoke(settings);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (_capturingHotkey)
        {
            HotkeyCaptureChanged?.Invoke(false); // не оставить хоткей снятым
        }

        base.OnClosed(e);
    }
}
