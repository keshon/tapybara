using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TapRecorder.App.Localization;
using TapRecorder.Core.Settings;
using TapRecorder.Core.Speech;
using TapRecorder.Core.Windows;
using Wpf.Ui.Controls;

// Коллизии имён между WinForms, WPF и WPF-UI: часть типов определена во всех
// трёх, а ImplicitUsings подключает пространства имён WinForms и WPF.
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBox = System.Windows.Controls.ComboBox;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;

namespace TapRecorder.App;

/// <summary>Окно настроек.</summary>
/// <remarks>
/// Карточки собираются кодом, а не размечаются в XAML: полей много, они
/// однотипные, и один построитель честнее, чем две сотни строк копипасты
/// с расходящимися отступами.
/// </remarks>
public partial class SettingsWindow : FluentWindow
{
    /// <summary>
    /// Ширина колонки контролов.
    /// </summary>
    /// <remarks>
    /// Одна на все карточки. Поля разной ширины давали рваный правый край —
    /// в настройках Windows все контролы выровнены по единой колонке, и
    /// именно это создаёт ощущение аккуратности.
    /// </remarks>
    private const double ControlColumnWidth = 220;

    private readonly IReadOnlyList<string> _models;
    private AppSettings _settings;

    private Button _hotkeyButton = null!;
    private TextBox _promptBox = null!;
    private TextBox _modelsFolderBox = null!;
    private bool _capturingHotkey;

    /// <summary>Страница, в которую построители складывают карточки.</summary>
    private StackPanel _page = new();

    public SettingsWindow(AppSettings settings, IReadOnlyList<string> models)
    {
        _settings = settings;
        _models = models;

        InitializeComponent();

        Title = L.S.SettingsTitle;
        WindowTitleBar.Title = L.S.SettingsTitle;
        CloseButton.Content = L.S.ButtonClose;
        FooterHint.Text = string.Format(
            CultureInfo.CurrentCulture, L.S.FooterStoragePath, AppPaths.DataDirectory);

        AddSection(L.S.SectionRecognition, SymbolRegular.BrainCircuit24, "#4FC3F7", BuildRecognitionSection);
        AddSection(L.S.SectionInput, SymbolRegular.Options24, "#B388FF", BuildInputSection);
        AddSection(L.S.SectionText, SymbolRegular.TextParagraph24, "#4DD0C4", BuildTextSection);
        AddSection(L.S.SectionStorage, SymbolRegular.FolderOpen24, "#FFB74D", BuildStorageSection);
        AddSection(L.S.SectionInterface, SymbolRegular.LocalLanguage24, "#81C784", BuildInterfaceSection);
        AddSection(L.S.SectionCalls, SymbolRegular.Mic24, "#F06292", BuildCallsSection);
        AddSection(L.S.SectionAbout, SymbolRegular.Options24, "#90A4AE", BuildAboutSection);

        SectionList.SelectedIndex = 0;
    }

    /// <summary>Одна страница настроек и пункт бокового меню для неё.</summary>
    /// <param name="title">Заголовок: и в меню, и над содержимым страницы.</param>
    /// <param name="icon">Значок пункта.</param>
    /// <param name="accent">
    /// Цвет значка. В системных настройках каждый раздел окрашен по-своему —
    /// это не украшение, а способ узнавать нужный пункт периферийным зрением,
    /// не вчитываясь в подписи.
    /// </param>
    /// <param name="build">Построитель карточек страницы.</param>
    private void AddSection(string title, SymbolRegular icon, string accent, Action build)
    {
        _page = new StackPanel();
        build();

        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new SymbolIcon
        {
            Symbol = icon,
            FontSize = 18,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent)),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        });

        label.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        });

        SectionList.Items.Add(new ListBoxItem { Content = label, Tag = new SectionPage(title, _page) });
    }

    /// <summary>Показать страницу выбранного раздела.</summary>
    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is ListBoxItem { Tag: SectionPage page })
        {
            PageTitle.Text = page.Title;
            PageHost.Content = page.Content;
        }
    }

    private sealed record SectionPage(string Title, StackPanel Content);

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
        var modelBox = new ComboBox { Width = ControlColumnWidth };
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

        AddCard(SymbolRegular.BrainCircuit24, L.S.FieldModel, description: null, modelBox);

        var languageBox = new TextBox { Width = ControlColumnWidth, Text = _settings.Language };
        languageBox.LostFocus += (_, _) =>
        {
            string value = languageBox.Text.Trim().ToLowerInvariant();
            if (value.Length > 0)
            {
                Apply(_settings with { Language = value });
            }
        };

        AddCard(
            SymbolRegular.LocalLanguage24,
            L.S.FieldRecognitionLanguage,
            L.S.FieldRecognitionLanguageHint,
            languageBox);

        _promptBox = new TextBox
        {
            Text = _settings.Prompt ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        _promptBox.LostFocus += (_, _) =>
        {
            string value = _promptBox.Text.Trim();
            Apply(_settings with { Prompt = value.Length == 0 ? null : value });
        };

        var resetPrompt = new Button
        {
            Content = L.S.ButtonDefault,
            MinWidth = 110,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };

        resetPrompt.Click += (_, _) =>
        {
            _promptBox.Text = LanguageDefaults.DefaultPrompt(_settings.Language);
            Apply(_settings with { Prompt = _promptBox.Text });
        };

        AddStackedCard(SymbolRegular.TextAlignLeft24, L.S.FieldPrompt, L.S.FieldPromptHint, _promptBox, resetPrompt);

        AddCard(
            SymbolRegular.Timer24,
            L.S.FieldIdleUnload,
            description: null,
            NumberField(
                _settings.IdleUnloadMinutes,
                L.S.Minutes,
                value => Apply(_settings with { IdleUnloadMinutes = Math.Clamp((int)value, 1, 240) })));

        AddToggleCard(
            SymbolRegular.Mic24,
            L.S.FieldUseVad,
            L.S.FieldUseVadHint,
            _settings.UseVoiceActivityDetection,
            value => Apply(_settings with { UseVoiceActivityDetection = value }));

        IReadOnlyList<string> vadModels = ModelLocator.ListVadModels(_settings.ModelsDirectory);
        if (vadModels.Count > 0)
        {
            var vadBox = new ComboBox { Width = ControlColumnWidth };
            foreach (string model in vadModels)
            {
                vadBox.Items.Add(PrettyModelName(model));
            }

            int selectedVad = vadModels.ToList().FindIndex(
                m => string.Equals(m, _settings.VadModelFileName, StringComparison.OrdinalIgnoreCase));
            vadBox.SelectedIndex = selectedVad >= 0 ? selectedVad : 0;
            vadBox.SelectionChanged += (_, _) =>
            {
                if (vadBox.SelectedIndex >= 0 && vadBox.SelectedIndex < vadModels.Count)
                {
                    Apply(_settings with { VadModelFileName = vadModels[vadBox.SelectedIndex] });
                }
            };

            AddCard(SymbolRegular.BrainCircuit24, L.S.FieldVadModel, description: null, vadBox);
        }

        AddCard(
            SymbolRegular.Options24,
            L.S.FieldVadThreshold,
            L.S.FieldVadThresholdHint,
            NumberField(
                _settings.VadThreshold,
                string.Empty,
                value => Apply(_settings with { VadThreshold = Math.Clamp(value, 0.05, 0.95) })));

        AddToggleCard(
            SymbolRegular.TextAlignLeft24,
            L.S.FieldNormalize,
            L.S.FieldNormalizeHint,
            _settings.NormalizeAudio,
            value => Apply(_settings with { NormalizeAudio = value }));
    }

    private void BuildCallsSection()
    {
        var callsFolderBox = new TextBox
        {
            Text = _settings.CallsDirectory ?? AppPaths.DefaultCallsDirectory,
            IsReadOnly = true,
        };

        var browse = new Button { Content = L.S.ButtonBrowse, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = callsFolderBox.Text };
            if (dialog.ShowDialog(this) == true)
            {
                callsFolderBox.Text = dialog.FolderName;
                Apply(_settings with { CallsDirectory = dialog.FolderName });
            }
        };

        var open = new Button { Content = L.S.ButtonOpen, MinWidth = 110 };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(callsFolderBox.Text);
            Process.Start(new ProcessStartInfo(callsFolderBox.Text) { UseShellExecute = true });
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };

        buttons.Children.Add(browse);
        buttons.Children.Add(open);

        AddStackedCard(SymbolRegular.FolderOpen24, L.S.FieldCallsFolder, description: null, callsFolderBox, buttons);

        AddCard(
            SymbolRegular.Mic24,
            L.S.FieldMyName,
            L.S.FieldMyNameHint,
            TextField(_settings.MyName, value => Apply(_settings with { MyName = value })));

        AddCard(
            SymbolRegular.ClipboardPaste24,
            L.S.FieldOtherSideName,
            description: null,
            TextField(_settings.OtherSideName, value => Apply(_settings with { OtherSideName = value })));

        AddCard(
            SymbolRegular.LocalLanguage24,
            L.S.FieldOtherSideLanguage,
            L.S.FieldOtherSideLanguageHint,
            TextField(
                _settings.OtherSideLanguage,
                value => Apply(_settings with { OtherSideLanguage = value.ToLowerInvariant() })));
    }

    private void BuildInputSection()
    {
        _hotkeyButton = new Button { Content = _settings.Hotkey.ToString(), Width = ControlColumnWidth };
        _hotkeyButton.Click += (_, _) => BeginHotkeyCapture();
        AddCard(SymbolRegular.Options24, L.S.FieldHotkey, L.S.FieldHotkeyHint, _hotkeyButton);

        AddToggleCard(
            SymbolRegular.ClipboardPaste24,
            L.S.FieldAutoPaste,
            description: null,
            _settings.AutoPaste,
            value => Apply(_settings with { AutoPaste = value }));

        AddToggleCard(
            SymbolRegular.ClipboardPaste24,
            L.S.FieldClipboardHistory,
            description: null,
            _settings.ExcludeFromClipboardHistory,
            value => Apply(_settings with { ExcludeFromClipboardHistory = value }));
    }

    private void BuildTextSection()
    {
        AddToggleCard(
            SymbolRegular.TextParagraph24,
            L.S.FieldSplitParagraphs,
            description: null,
            _settings.SplitParagraphsByPauses,
            value => Apply(_settings with { SplitParagraphsByPauses = value }));

        AddCard(
            SymbolRegular.Timer24,
            L.S.FieldParagraphPause,
            description: null,
            NumberField(
                _settings.ParagraphPauseSeconds,
                L.S.Seconds,
                value => Apply(_settings with { ParagraphPauseSeconds = Math.Clamp(value, 0.2, 10) })));

        var replacementsBox = new TextBox
        {
            Text = FormatReplacements(_settings.Replacements),
            AcceptsReturn = true,
            MinHeight = 92,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
        };

        replacementsBox.LostFocus += (_, _) =>
            Apply(_settings with { Replacements = ParseReplacements(replacementsBox.Text) });

        AddStackedCard(
            SymbolRegular.ArrowReset24,
            L.S.FieldReplacements,
            L.S.FieldReplacementsHint,
            replacementsBox,
            trailing: null);
    }

    private void BuildStorageSection()
    {
        _modelsFolderBox = new TextBox
        {
            Text = ModelLocator.FindModelsDirectory(_settings.ModelsDirectory) ?? AppPaths.DefaultModelsDirectory,
            IsReadOnly = true,
        };

        var browse = new Button { Content = L.S.ButtonBrowse, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = _modelsFolderBox.Text };
            if (dialog.ShowDialog(this) == true)
            {
                _modelsFolderBox.Text = dialog.FolderName;
                Apply(_settings with { ModelsDirectory = dialog.FolderName });
            }
        };

        var open = new Button { Content = L.S.ButtonOpen, MinWidth = 110 };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(_modelsFolderBox.Text);
            Process.Start(new ProcessStartInfo(_modelsFolderBox.Text) { UseShellExecute = true });
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };

        buttons.Children.Add(browse);
        buttons.Children.Add(open);

        AddStackedCard(SymbolRegular.FolderOpen24, L.S.FieldModelsFolder, description: null, _modelsFolderBox, buttons);

        AddToggleCard(
            SymbolRegular.Options24,
            L.S.FieldPortable,
            L.S.FieldPortableHint + " " + L.S.RestartRequired + ".",
            AppPaths.IsPortable,
            AppPaths.SetPortable);
    }

    private void BuildInterfaceSection()
    {
        var languageBox = new ComboBox { Width = ControlColumnWidth };
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

        AddCard(SymbolRegular.LocalLanguage24, L.S.FieldUiLanguage, description: null, languageBox);
    }

    private void BuildAboutSection()
    {
        var tagline = new TextBlock
        {
            Text = L.S.AboutTagline,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Margin = new Thickness(2, 0, 0, 14),
        };

        _page.Children.Add(tagline);

        AddCard(SymbolRegular.Options24, L.S.AboutVersion, description: null, ValueText(AppVersion));

        AddCard(SymbolRegular.Options24, L.S.AboutAuthors, description: null, ValueText(L.S.AboutAuthorsValue));

        // Бэкенд показываем именно здесь: библиотека молча откатывается с GPU
        // на CPU, если нативную сборку не удалось загрузить, и без этой строки
        // разница в скорости в полсотни раз выглядит необъяснимой.
        AddCard(
            SymbolRegular.BrainCircuit24,
            L.S.AboutRuntime,
            L.S.AboutRuntimeHint,
            ValueText(WhisperEngine.LoadedRuntime));

        AddCard(SymbolRegular.Options24, L.S.AboutLicense, description: null, ValueText("MIT"));

        var copy = new Button
        {
            Content = L.S.ButtonCopyDiagnostics,
            MinWidth = 180,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };

        copy.Click += (_, _) =>
        {
            ClipboardWriter.SetText(BuildDiagnostics(), excludeFromHistory: false);
            copy.Content = L.S.DiagnosticsCopied;
        };

        AddStackedCard(
            SymbolRegular.ArrowReset24,
            "TapRecorder",
            L.S.AboutComponents,
            copy,
            trailing: null);
    }

    /// <summary>Сводка окружения — чтобы её можно было приложить к отчёту об ошибке.</summary>
    private string BuildDiagnostics()
    {
        var builder = new StringBuilder();
        builder.Append("TapRecorder ").AppendLine(AppVersion);
        builder.Append("OS: ").AppendLine(Environment.OSVersion.VersionString);
        builder.Append("Runtime: ").AppendLine(Environment.Version.ToString());
        builder.Append("Backend: ").AppendLine(WhisperEngine.LoadedRuntime);
        builder.Append("Model: ").AppendLine(_settings.ModelFileName);
        builder.Append("Language: ").AppendLine(_settings.Language);
        builder.Append("Data: ").AppendLine(AppPaths.DataDirectory);
        builder.Append("Portable: ").AppendLine(AppPaths.IsPortable ? "yes" : "no");
        return builder.ToString();
    }

    private static string AppVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    private static TextBlock ValueText(string text) => new()
    {
        Text = text,
        Opacity = 0.75,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 320,
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = System.Windows.VerticalAlignment.Center,
    };

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

    // --- построители карточек ----------------------------------------------

    /// <summary>Карточка «иконка, заголовок с описанием — контрол справа».</summary>
    private void AddCard(SymbolRegular icon, string title, string? description, UIElement control)
    {
        _page.Children.Add(new CardControl
        {
            Icon = new SymbolIcon { Symbol = icon },
            Header = BuildHeader(title, description),
            Content = control,
            Margin = new Thickness(0, 0, 0, 4),
        });
    }

    private void AddToggleCard(
        SymbolRegular icon,
        string title,
        string? description,
        bool value,
        Action<bool> onChange)
    {
        var toggle = new ToggleSwitch { IsChecked = value };
        toggle.Checked += (_, _) => onChange(true);
        toggle.Unchecked += (_, _) => onChange(false);

        AddCard(icon, title, description, toggle);
    }

    /// <summary>
    /// Карточка, где контрол занимает всю ширину под заголовком.
    /// </summary>
    /// <remarks>
    /// Для многострочных полей: <see cref="CardControl"/> кладёт содержимое
    /// справа от заголовка, и текстовая область там оказалась бы шириной
    /// в треть окна.
    /// </remarks>
    private void AddStackedCard(
        SymbolRegular icon,
        string title,
        string? description,
        UIElement control,
        UIElement? trailing)
    {
        var content = new StackPanel();

        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(new SymbolIcon
        {
            Symbol = icon,
            FontSize = 20,
            Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
        });

        head.Children.Add(BuildHeader(title, description, maxWidth: 470));
        content.Children.Add(head);
        content.Children.Add(control);

        if (trailing is not null)
        {
            content.Children.Add(trailing);
        }

        _page.Children.Add(new Border
        {
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush", Colors.Transparent),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 4),
            Child = content,
        });
    }

    private static StackPanel BuildHeader(string title, string? description, double maxWidth = 320)
    {
        // Прижимаем влево явно: без этого CardControl центрирует заголовок в
        // отведённой ему ширине, и карточки без описания выглядят съехавшими
        // относительно карточек с описанием — колонка заголовков «пляшет».
        var panel = new StackPanel
        {
            MaxWidth = maxWidth,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        };
        panel.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });

        if (description is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                FontSize = 12,
                Opacity = 0.65,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
            });
        }

        return panel;
    }

    /// <summary>Однострочное поле, применяющее значение по потере фокуса.</summary>
    /// <remarks>
    /// Именно по потере фокуса, а не по каждому нажатию клавиши: иначе
    /// настройка сохранялась бы на каждую букву, а промежуточные обрывки
    /// вроде «Со» успели бы попасть в транскрипт.
    /// </remarks>
    private static TextBox TextField(string initial, Action<string> onChange)
    {
        var box = new TextBox { Width = ControlColumnWidth, Text = initial };
        box.LostFocus += (_, _) =>
        {
            string value = box.Text.Trim();
            if (value.Length > 0)
            {
                onChange(value);
            }
            else
            {
                box.Text = initial;
            }
        };

        return box;
    }

    /// <summary>Поле для числа с подписью единиц измерения.</summary>
    private static StackPanel NumberField(double initial, string unit, Action<double> onChange)
    {
        var box = new TextBox
        {
            Text = initial.ToString(CultureInfo.CurrentCulture),
            Width = 80,
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

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
        };

        panel.Children.Add(box);
        panel.Children.Add(new TextBlock
        {
            Text = unit,
            Opacity = 0.65,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        });

        return panel;
    }

    /// <summary>Кисть из темы, с запасным цветом, если ресурса нет.</summary>
    private Brush ThemeBrush(string resourceKey, Color fallback) =>
        TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallback);

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

    /// <summary>
    /// Не давать окну разворачиваться на весь экран.
    /// </summary>
    /// <remarks>
    /// Кнопку разворота мы убрали, но остаются двойной клик по заголовку и
    /// Win+Стрелка вверх. Содержимое — колонка карточек фиксированной ширины,
    /// на 27 дюймах во весь экран это выглядело бы полосой текста посреди
    /// пустоты. Ширину ограничивает MaxWidth, высота свободна.
    /// </remarks>
    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }

        base.OnStateChanged(e);
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
