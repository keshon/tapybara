using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using Tapybara.App.Localization;
using Tapybara.Core.Audio;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Models;
using Tapybara.Core.Settings;
using Tapybara.Core.Speech;
using Tapybara.Core.Windows;
using Wpf.Ui.Controls;

// Коллизии имён между WinForms, WPF и WPF-UI: часть типов определена во всех
// трёх, а ImplicitUsings подключает пространства имён WinForms и WPF.
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using ProgressBar = System.Windows.Controls.ProgressBar;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Tapybara.App;

/// <summary>Чьё сочетание клавиш: диктовки или записи звонка.</summary>
public enum HotkeyTarget
{
    Dictation,
    Call,
}

/// <summary>Разделы окна настроек.</summary>
public enum SettingsSection
{
    Dictation,
    Recognition,
    Models,
    Text,
    Calls,
    General,
    About,
}

/// <summary>Окно настроек.</summary>
/// <remarks>
/// <para>
/// Карточки собираются кодом, а не размечаются в XAML: полей много, они
/// однотипные, и один построитель честнее, чем две сотни строк копипасты
/// с расходящимися отступами.
/// </para>
/// <para>
/// Разделы устроены по вопросу, с которым человек сюда приходит: «поменять
/// горячую клавишу» — «Диктовка», «взять модель» — «Модели», «поправить
/// расслышанное слово» — «Текст». Прежняя раскладка была устроена по тому,
/// как код разложен по классам, и найти в ней нужное можно было только
/// перебором.
/// </para>
/// </remarks>
public partial class SettingsWindow : FluentWindow
{
    /// <summary>
    /// Ширина колонки контролов.
    /// </summary>
    /// <remarks>
    /// Одна на все карточки. Поля разной ширины давали рваный правый край —
    /// в «Параметрах» Windows все контролы выровнены по единой колонке, и
    /// именно это создаёт ощущение аккуратности.
    /// </remarks>
    private const double ControlColumnWidth = 240;

    /// <summary>Сайт проекта.</summary>
    private const string SiteUrl = "https://tapybara.keshon.ru";

    /// <summary>Исходники.</summary>
    private const string RepositoryUrl = "https://github.com/keshon/tapybara";

    private readonly SettingsHost _host;
    private readonly Func<IReadOnlyList<string>> _availableModels;

    /// <summary>
    /// Удаление модели поручено приложению.
    /// </summary>
    /// <remarks>
    /// Окно настроек не может удалить файл само: удаляемая модель может быть
    /// прямо сейчас загружена в движок, и файл придётся сначала отпустить.
    /// Знает об этом приложение, оно же и решает, что делать после.
    /// Возвращает причину отказа или <c>null</c>, если удалось.
    /// </remarks>
    private readonly Func<InstalledModel, Task<string?>> _deleteModel;

    /// <summary>Действия, возвращающие контролам значения из настроек.</summary>
    /// <remarks>
    /// Нужны, потому что настройки меняются не только отсюда: модель
    /// переключают из меню трея, автозапуск — оттуда же. Без обратной связи
    /// окно отправляло бы назад устаревший снимок и отменяло чужие правки.
    /// </remarks>
    private readonly List<Action> _refreshers = [];

    private readonly ModelsPageState _models = new();

    /// <summary>Кнопка и строка статуса для каждого из двух сочетаний.</summary>
    private readonly Dictionary<HotkeyTarget, (Button Button, TextBlock Status)> _hotkeyFields = [];

    /// <summary>Какое сочетание сейчас записывается. <c>null</c> — никакое.</summary>
    private HotkeyTarget? _capturingHotkey;

    /// <summary>
    /// Идёт синхронизация контролов со значениями настроек.
    /// </summary>
    /// <remarks>
    /// Раньше флаг назывался иначе и означал «правку сделали в этом окне» —
    /// на такие изменения окно себя не обновляло вовсе. Логика казалась
    /// разумной (мы же сами это и написали), но она неверна: настройку
    /// меняет ОДИН контрол, а зависят от неё несколько. Выбрал новую папку
    /// моделей — путь в поле, список установленного и выбор модели остались
    /// от прежней папки, и починить это можно было только закрыв и открыв
    /// окно заново.
    /// <para>
    /// Теперь флаг защищает ровно от того, от чего должен: от рекурсии.
    /// Пока идёт обновление, события контролов — это эхо нашей же записи,
    /// а не намерение пользователя.
    /// </para>
    /// </remarks>
    private bool _refreshing;

    /// <summary>Страница, в которую построители складывают карточки.</summary>
    private StackPanel _page = new();

    public SettingsWindow(
        SettingsHost host,
        Func<IReadOnlyList<string>> availableModels,
        Func<InstalledModel, Task<string?>> deleteModel)
    {
        _host = host;
        _availableModels = availableModels;
        _deleteModel = deleteModel;

        InitializeComponent();
        BuildEverything();

        _host.Changed += OnHostChanged;
        Closed += (_, _) => _host.Changed -= OnHostChanged;
    }

    /// <summary>Идёт захват сочетания клавиш — глобальный хоткей надо снять.</summary>
    /// <remarks>
    /// Иначе нажатие текущего сочетания перехватит система и запустит
    /// диктовку вместо того, чтобы дать нам его записать.
    /// </remarks>
    public event Action<bool>? HotkeyCaptureChanged;

    /// <summary>
    /// Набор моделей на диске изменился — скачали новую.
    /// </summary>
    /// <remarks>
    /// Приложению это нужно, даже когда настройки не поменялись. Скачать
    /// модель, имя которой уже стоит в настройках, — обычное дело на первом
    /// запуске: настройка та же, файла раньше не было, а теперь есть.
    /// Изменения настроек при этом нет, и без отдельного события движок
    /// не собрался бы до перезапуска.
    /// </remarks>
    public event Action? ModelsChanged;

    private AppSettings Settings => _host.Current;

    /// <summary>Действующая папка моделей — всегда пересчитывается.</summary>
    private string TargetDirectory() =>
        ModelLocator.FindModelsDirectory(Settings.ModelsDirectory) ?? AppPaths.DefaultModelsDirectory;

    /// <summary>Перейти к разделу.</summary>
    public void GoTo(SettingsSection section)
    {
        for (int i = 0; i < SectionList.Items.Count; i++)
        {
            if (SectionList.Items[i] is ListBoxItem { Tag: SectionPage page } && page.Section == section)
            {
                SectionList.SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>Пересобрать окно на новом языке, сохранив выбранный раздел.</summary>
    /// <remarks>
    /// Раньше окно закрывалось и открывалось заново, и пользователя
    /// выбрасывало на первую страницу — с той самой, где он только что менял
    /// язык.
    /// </remarks>
    public void ApplyLanguage()
    {
        SettingsSection current = CurrentSection();
        BuildEverything();
        GoTo(current);
    }

    /// <summary>
    /// Перечитать всё с диска: набор моделей мог измениться снаружи.
    /// </summary>
    /// <remarks>
    /// Файл модели могли просто скопировать в папку проводником. Ждать от
    /// человека перезапуска приложения ради того, чтобы программа заметила
    /// новый файл, — не то поведение, которого он ожидает.
    /// </remarks>
    public void RefreshFromDisk() => RefreshControls();

    /// <summary>Приложение сообщает, удалось ли занять выбранное сочетание.</summary>
    public void ReportHotkeyResult(HotkeyTarget target, bool succeeded)
    {
        if (!_hotkeyFields.TryGetValue(target, out (Button Button, TextBlock Status) field))
        {
            return;
        }

        field.Status.Visibility = succeeded ? Visibility.Collapsed : Visibility.Visible;
        field.Status.Text = string.Format(
            CultureInfo.CurrentCulture, L.S.FieldHotkeyTaken, HotkeyOf(target));
    }

    private HotkeyCombo HotkeyOf(HotkeyTarget target) =>
        target == HotkeyTarget.Call ? Settings.CallHotkey : Settings.Hotkey;

    private SettingsSection CurrentSection() =>
        SectionList.SelectedItem is ListBoxItem { Tag: SectionPage page } ? page.Section : SettingsSection.General;

    // --- сборка ------------------------------------------------------------

    private void BuildEverything()
    {
        _refreshers.Clear();
        _hotkeyFields.Clear();
        SectionList.Items.Clear();

        Title = L.S.SettingsTitle;
        WindowTitleBar.Title = L.S.SettingsTitle;
        CloseButton.Content = L.S.ButtonClose;
        FooterHint.Text = string.Format(
            CultureInfo.CurrentCulture, L.S.FooterStoragePath, AppPaths.DataDirectory);

        // Порядок идёт от общего к частному. Сначала настройки самой
        // программы, следом два способа ею пользоваться — диктовка и звонки,
        // они соседи потому, что это две записи с микрофона и человек
        // сравнивает их между собой. Дальше то, что их обслуживает:
        // распознавание, модели, обработка текста. «О программе» — последним,
        // как везде.
        AddSection(SettingsSection.General, L.S.SectionGeneral, SymbolRegular.Settings24, "#81C784", BuildGeneralSection);
        AddSection(SettingsSection.Dictation, L.S.SectionDictation, SymbolRegular.Mic24, "#4FC3F7", BuildDictationSection);
        AddSection(SettingsSection.Calls, L.S.SectionCalls, SymbolRegular.Call24, "#F06292", BuildCallsSection);
        AddSection(SettingsSection.Recognition, L.S.SectionRecognition, SymbolRegular.BrainCircuit24, "#B388FF", BuildRecognitionSection);
        AddSection(SettingsSection.Models, L.S.SectionModels, SymbolRegular.ArrowDownload24, "#4DD0C4", BuildModelsSection);
        AddSection(SettingsSection.Text, L.S.SectionText, SymbolRegular.TextParagraph24, "#FFB74D", BuildTextSection);
        AddSection(SettingsSection.About, L.S.SectionAbout, SymbolRegular.Info24, "#90A4AE", BuildAboutSection);

        SectionList.SelectedIndex = 0;
    }

    /// <summary>Одна страница настроек и пункт бокового меню для неё.</summary>
    /// <param name="section">Что это за раздел — для перехода к нему извне.</param>
    /// <param name="title">Заголовок: и в меню, и над содержимым страницы.</param>
    /// <param name="icon">Значок пункта. У каждого раздела свой, без повторов.</param>
    /// <param name="accent">
    /// Цвет значка. В «Параметрах» Windows каждый раздел окрашен по-своему —
    /// это не украшение, а способ узнавать нужный пункт периферийным зрением,
    /// не вчитываясь в подписи.
    /// </param>
    /// <param name="build">Построитель карточек страницы.</param>
    private void AddSection(
        SettingsSection section,
        string title,
        SymbolRegular icon,
        string accent,
        Action build)
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
            VerticalAlignment = VerticalAlignment.Center,
        });

        label.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var item = new ListBoxItem
        {
            Content = label,
            Tag = new SectionPage(section, title, _page),
        };

        AutomationProperties.SetName(item, title);
        SectionList.Items.Add(item);
    }

    /// <summary>Показать страницу выбранного раздела.</summary>
    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is ListBoxItem { Tag: SectionPage page })
        {
            PageTitle.Text = page.Title;
            PageHost.Content = page.Content;
            PageScroll.ScrollToTop();
        }
    }

    private sealed record SectionPage(SettingsSection Section, string Title, StackPanel Content);

    // --- раздел: диктовка --------------------------------------------------

    private void BuildDictationSection()
    {
        AddGroup(L.S.GroupHotkey);
        AddCard(SymbolRegular.Keyboard24, L.S.FieldHotkey, L.S.FieldHotkeyHint, HotkeyField(HotkeyTarget.Dictation));

        AddGroup(L.S.GroupAudioInput);
        AddCard(
            SymbolRegular.Mic24,
            L.S.FieldMicrophone,
            L.S.FieldMicrophoneHint,
            DeviceCombo(
                DataFlow.Capture,
                () => Settings.MicrophoneDeviceId,
                id => Apply(s => s with { MicrophoneDeviceId = id })));

        AddGroup(L.S.GroupInsertion);
        AddToggleCard(
            SymbolRegular.ClipboardPaste24,
            L.S.FieldAutoPaste,
            L.S.FieldAutoPasteHint,
            () => Settings.AutoPaste,
            value => Apply(s => s with { AutoPaste = value }));

        AddToggleCard(
            SymbolRegular.History24,
            L.S.FieldClipboardHistory,
            description: null,
            () => Settings.ExcludeFromClipboardHistory,
            value => Apply(s => s with { ExcludeFromClipboardHistory = value }));

        AddToggleCard(
            SymbolRegular.Eye24,
            L.S.FieldShowOverlay,
            L.S.FieldShowOverlayHint,
            () => Settings.ShowOverlay,
            value => Apply(s => s with { ShowOverlay = value }));

        AddGroup(L.S.GroupLimits);
        AddCard(
            SymbolRegular.Timer24,
            L.S.FieldMaxDictation,
            L.S.FieldMaxDictationHint,
            NumberField(
                () => Settings.MaxDictationMinutes,
                L.S.Minutes,
                value => Apply(s => s with { MaxDictationMinutes = (int)Math.Clamp(value, 1, 24 * 60) })));
    }

    // --- раздел: распознавание ---------------------------------------------

    private void BuildRecognitionSection()
    {
        AddGroup(L.S.GroupModel);

        var modelBox = new ComboBox { Width = ControlColumnWidth };
        RefillModelBox(modelBox);
        modelBox.SelectionChanged += (_, _) =>
        {
            if (modelBox.SelectedItem is string fileName)
            {
                Apply(s => s with { ModelFileName = fileName });
            }
        };

        Refresh(() => RefillModelBox(modelBox));
        AddCard(SymbolRegular.BrainCircuit24, L.S.FieldModel, L.S.FieldModelHint, modelBox);

        AddCard(
            SymbolRegular.Options24,
            L.S.FieldDecoding,
            L.S.FieldDecodingHint,
            DecodingCombo());

        AddCard(
            SymbolRegular.Timer24,
            L.S.FieldIdleUnload,
            L.S.FieldIdleUnloadHint,
            NumberField(
                () => Settings.IdleUnloadMinutes,
                L.S.Minutes,
                value => Apply(s => s with { IdleUnloadMinutes = (int)Math.Clamp(value, 1, 24 * 60) })));

        AddGroup(L.S.GroupLanguageAndStyle);

        AddCard(
            SymbolRegular.LocalLanguage24,
            L.S.FieldRecognitionLanguage,
            L.S.FieldRecognitionLanguageHint,
            LanguageCombo(() => Settings.Language, code => Apply(s => s with { Language = code })));

        var promptBox = new TextBox
        {
            Text = Settings.Prompt ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 64,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        promptBox.LostFocus += (_, _) =>
        {
            string value = promptBox.Text.Trim();
            Apply(s => s with { Prompt = value.Length == 0 ? null : value });
        };

        Refresh(() =>
        {
            if (!promptBox.IsKeyboardFocusWithin)
            {
                promptBox.Text = Settings.Prompt ?? string.Empty;
            }
        });

        var resetPrompt = new Button
        {
            Content = L.S.ButtonDefault,
            MinWidth = 110,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };

        resetPrompt.Click += (_, _) =>
        {
            promptBox.Text = LanguageDefaults.DefaultPrompt(Settings.Language);
            Apply(s => s with { Prompt = promptBox.Text });
        };

        AddStackedCard(SymbolRegular.TextAlignLeft24, L.S.FieldPrompt, L.S.FieldPromptHint, promptBox, resetPrompt);

        AddGroup(L.S.GroupSpeechDetection);
        BuildSpeechDetectionCards();
    }

    private void BuildSpeechDetectionCards()
    {
        AddToggleCard(
            SymbolRegular.PulseSquare24,
            L.S.FieldUseVad,
            L.S.FieldUseVadHint,
            () => Settings.UseVoiceActivityDetection,
            value => Apply(s => s with { UseVoiceActivityDetection = value }));

        IReadOnlyList<string> vadModels = ModelLocator.ListVadModels(Settings.ModelsDirectory);
        if (vadModels.Count == 0)
        {
            // Раздел детектора раньше просто исчезал, если модели не было, —
            // и человек не мог узнать, что она вообще существует и что её
            // надо скачать.
            var goToModels = new Button
            {
                Content = L.S.SectionModels,
                MinWidth = 130,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0),
            };

            goToModels.Click += (_, _) => GoTo(SettingsSection.Models);

            AddStackedCard(
                SymbolRegular.Warning24,
                L.S.FieldVadModel,
                L.S.FieldVadModelMissing,
                goToModels,
                trailing: null);
            return;
        }

        var vadBox = new ComboBox { Width = ControlColumnWidth };
        RefillVadBox(vadBox, vadModels);
        vadBox.SelectionChanged += (_, _) =>
        {
            if (vadBox.SelectedItem is string fileName)
            {
                Apply(s => s with { VadModelFileName = fileName });
            }
        };

        Refresh(() => RefillVadBox(vadBox, ModelLocator.ListVadModels(Settings.ModelsDirectory)));
        AddCard(SymbolRegular.Options24, L.S.FieldVadModel, description: null, vadBox);

        BuildVadThresholdCard();

        AddToggleCard(
            SymbolRegular.TopSpeed24,
            L.S.FieldNormalize,
            L.S.FieldNormalizeHint,
            () => Settings.NormalizeAudio,
            value => Apply(s => s with { NormalizeAudio = value }));
    }

    /// <summary>
    /// Чувствительность детектора: значение, подбор по образцу и сброс.
    /// </summary>
    /// <remarks>
    /// Кнопка подбора здесь не украшение. Верное значение зависит от
    /// микрофона, комнаты и голоса, и совет «подвигай ползунок» перекладывает
    /// на пользователя задачу, которую программа умеет решить измерением.
    /// </remarks>
    private void BuildVadThresholdCard()
    {
        var valueBox = new TextBox
        {
            Text = Settings.VadThreshold.ToString("F2", CultureInfo.CurrentCulture),
            Width = 80,
            TextAlignment = TextAlignment.Right,

            // Без явного выравнивания StackPanel центрирует элемент с заданной
            // шириной, и поле уезжает на середину карточки.
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        void SetThreshold(double value)
        {
            double clamped = Math.Clamp(value, 0.05, 0.95);
            valueBox.Text = clamped.ToString("F2", CultureInfo.CurrentCulture);
            Apply(s => s with { VadThreshold = clamped });
        }

        valueBox.LostFocus += (_, _) =>
        {
            string normalized = valueBox.Text.Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                SetThreshold(parsed);
            }
            else
            {
                valueBox.Text = Settings.VadThreshold.ToString("F2", CultureInfo.CurrentCulture);
            }
        };

        Refresh(() =>
        {
            if (!valueBox.IsKeyboardFocusWithin)
            {
                valueBox.Text = Settings.VadThreshold.ToString("F2", CultureInfo.CurrentCulture);
            }
        });

        var test = new Button { Content = L.S.ButtonTest, MinWidth = 130, Margin = new Thickness(0, 0, 8, 0) };
        test.Click += (_, _) =>
        {
            string? vadPath = ModelLocator.ResolveVadModel(Settings.VadModelFileName, Settings.ModelsDirectory);
            if (vadPath is null)
            {
                GoTo(SettingsSection.Models);
                return;
            }

            var window = new VadTestWindow(vadPath, Settings.NormalizeAudio, Settings.MicrophoneDeviceId)
            {
                Owner = this,
            };

            if (window.ShowDialog() == true && window.AcceptedThreshold is { } accepted)
            {
                SetThreshold(accepted);
            }
        };

        var reset = new Button { Content = L.S.ButtonDefault, MinWidth = 130 };
        reset.Click += (_, _) => SetThreshold(new AppSettings().VadThreshold);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0),
        };

        buttons.Children.Add(test);
        buttons.Children.Add(reset);

        var row = new StackPanel();
        row.Children.Add(valueBox);
        row.Children.Add(buttons);

        AddStackedCard(
            SymbolRegular.Gauge24,
            L.S.FieldVadThreshold,
            L.S.FieldVadThresholdHint,
            row,
            trailing: null);
    }

    // --- раздел: модели ----------------------------------------------------

    /// <summary>
    /// Где взять модели.
    /// </summary>
    /// <remarks>
    /// Самая важная страница для нового пользователя и единственный ответ на
    /// вопрос, на котором раньше всё заканчивалось. Приложение сообщало
    /// «модель не найдена» и предлагало самому найти в интернете
    /// полуторагигабайтный файл, о котором человек ничего не знает: ни имени,
    /// ни размера, ни того, какой из тридцати вариантов ему нужен.
    /// </remarks>
    private void BuildModelsSection()
    {
        _page.Children.Add(new TextBlock
        {
            Text = L.S.ModelsIntro,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
            Margin = new Thickness(2, 0, 24, 16),
        });

        AddGroup(L.S.GroupModelsFolder);

        var folderBox = new TextBox
        {
            Text = TargetDirectory(),
            IsReadOnly = true,
        };

        Refresh(() => folderBox.Text = TargetDirectory());

        AddStackedCard(
            SymbolRegular.FolderOpen24,
            L.S.FieldModelsFolder,
            L.S.FieldModelsFolderHint,
            folderBox,
            FolderButtons(
                () => folderBox.Text,
                picked => _ = ChangeModelsFolderAsync(picked),
                // Возврат к умолчанию — это сброс настройки в null, а не
                // запись текущего пути: умолчание зависит от режима хранения,
                // и в портативном режиме оно другое.
                () => _ = ChangeModelsFolderAsync(null)));

        AddGroup(L.S.ModelsInstalled);
        BuildInstalledList();

        AddGroup(L.S.GroupDownload);
        BuildDownloadList();
    }

    private void BuildInstalledList()
    {
        var list = new StackPanel();
        void Fill()
        {
            list.Children.Clear();

            IReadOnlyList<InstalledModel> installed = ModelStorage.List(TargetDirectory());

            if (installed.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = L.S.ModelsNothingInstalled,
                    Opacity = 0.7,
                });

                return;
            }

            foreach (InstalledModel model in installed)
            {
                list.Children.Add(BuildInstalledRow(model));
            }

            list.Children.Add(new TextBlock
            {
                Text = string.Format(
                    CultureInfo.CurrentCulture,
                    L.S.ModelsTotalSize,
                    FormatSize(installed.Sum(m => m.Bytes))),
                Opacity = 0.6,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }

        Fill();
        Refresh(Fill);

        // Без заголовка: он уже стоит над группой, и повторять его на карточке
        // значило бы написать «Установлены» дважды подряд.
        AddPlainCard(list);
    }

    /// <summary>Строка списка установленного: что это, сколько весит и кнопка удаления.</summary>
    private Grid BuildInstalledRow(InstalledModel model)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new SymbolIcon
        {
            Symbol = model.Kind switch
            {
                ModelKind.SpeechDetector => SymbolRegular.PulseSquare24,
                ModelKind.VoiceSegmentation or ModelKind.VoiceEmbedding => SymbolRegular.PeopleTeam24,
                _ => SymbolRegular.BrainCircuit24,
            },
            FontSize = 15,
            Margin = new Thickness(0, 0, 10, 0),
            Foreground = ThemeBrush("SystemFillColorSuccessBrush", Colors.SeaGreen),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var label = new TextBlock
        {
            Text = $"{PrettyModelName(model.FileName)}  ·  {FormatSize(model.Bytes)}",
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        var delete = new Button
        {
            Content = L.S.ButtonDelete,
            MinWidth = 100,
            VerticalAlignment = VerticalAlignment.Center,
        };

        delete.Click += async (_, _) => await DeleteModelAsync(model, delete);

        Grid.SetColumn(label, 1);
        Grid.SetColumn(delete, 2);
        row.Children.Add(icon);
        row.Children.Add(label);
        row.Children.Add(delete);
        return row;
    }

    /// <summary>Спросить и удалить модель.</summary>
    /// <remarks>
    /// Подтверждение обязательно: файл весит сотни мегабайт и качался
    /// минутами, а кнопка стоит рядом со списком, по которому просто
    /// скользят глазами.
    /// </remarks>
    private async Task DeleteModelAsync(InstalledModel model, Button button)
    {
        bool inUse = string.Equals(model.FileName, Settings.ModelFileName, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(model.FileName, Settings.VadModelFileName, StringComparison.OrdinalIgnoreCase);

        string question = string.Format(
            CultureInfo.CurrentCulture,
            L.S.ModelsDeleteQuestion,
            PrettyModelName(model.FileName),
            FormatSize(model.Bytes));

        if (inUse)
        {
            question += Environment.NewLine + Environment.NewLine + L.S.ModelsDeleteInUse;
        }

        var confirm = new ConfirmWindow(
            L.S.ModelsDeleteTitle,
            question,
            primaryButton: L.S.ButtonDelete,
            cancelButton: L.S.ButtonCancel,
            icon: SymbolRegular.Delete24,
            danger: true);

        confirm.AddDetail(L.S.LabelFrom, model.Path);

        if (ConfirmWindow.Ask(this, confirm) != ConfirmChoice.Primary)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            if (await _deleteModel(model) is { } failure)
            {
                ConfirmWindow.Ask(this, new ConfirmWindow(
                    L.S.ModelsDeleteTitle,
                    string.Format(CultureInfo.CurrentCulture, L.S.ModelsDeleteFailed, failure),
                    primaryButton: L.S.ButtonClose,
                    cancelButton: L.S.ButtonClose,
                    icon: SymbolRegular.Warning24,
                    danger: true));
            }
        }
        finally
        {
            button.IsEnabled = true;
            RefreshControls();
        }
    }

    private void BuildDownloadList()
    {
        AddDownloadGroup(
            L.S.ModelsRecognitionHeader,
            description: null,
            ModelKind.Recognition);

        AddDownloadGroup(
            L.S.ModelsDetectorHeader,
            L.S.ModelsDetectorNote,
            ModelKind.SpeechDetector);

        AddDownloadGroup(
            L.S.ModelsVoicesHeader,
            L.S.ModelsVoicesNote,
            ModelKind.VoiceSegmentation,
            ModelKind.VoiceEmbedding);

        var link = new Wpf.Ui.Controls.HyperlinkButton
        {
            Content = L.S.ModelsFullListLink,
            NavigateUri = ModelCatalog.WhisperModelsPageUrl,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0),
        };

        _page.Children.Add(link);
    }

    private void AddDownloadGroup(string title, string? description, params ModelKind[] kinds)
    {
        var list = new StackPanel();
        foreach (CatalogModel model in ModelCatalog.All.Where(m => kinds.Contains(m.Kind)))
        {
            list.Children.Add(BuildDownloadRow(model));
        }

        AddStackedCard(
            kinds[0] switch
            {
                ModelKind.Recognition => SymbolRegular.BrainCircuit24,
                ModelKind.SpeechDetector => SymbolRegular.PulseSquare24,
                _ => SymbolRegular.PeopleTeam24,
            },
            title,
            description,
            list,
            trailing: null);
    }

    private Grid BuildDownloadRow(CatalogModel model)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel();
        info.Children.Add(new TextBlock
        {
            Text = model.DisplayName,
            FontWeight = model.Tier == ModelTier.Recommended ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
        });

        var detail = new TextBlock
        {
            Text = $"{L.S.Describe(model.Tier)} · {FormatSize(model.ApproximateBytes)}",
            FontSize = 12,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        };

        info.Children.Add(detail);

        var progress = new ProgressBar
        {
            Height = 4,
            Minimum = 0,
            Maximum = 1,
            Margin = new Thickness(0, 8, 12, 0),
            Visibility = Visibility.Collapsed,
        };

        info.Children.Add(progress);

        var action = new Button
        {
            MinWidth = 120,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, 0, 0, 0),
        };

        Grid.SetColumn(action, 1);
        grid.Children.Add(info);
        grid.Children.Add(action);

        WireDownloadRow(model, action, detail, progress);
        return grid;
    }

    private void WireDownloadRow(
        CatalogModel model,
        Button action,
        TextBlock detail,
        ProgressBar progress)
    {
        string baseDetail = $"{L.S.Describe(model.Tier)} · {FormatSize(model.ApproximateBytes)}";

        void ShowIdle()
        {
            // Папку спрашиваем каждый раз, а не запоминаем при сборке
            // страницы: её меняют прямо здесь, на этой же странице, и
            // запомненное значение отправило бы закачку в прежнюю папку.
            bool installed = File.Exists(Path.Combine(TargetDirectory(), model.FileName));
            progress.Visibility = Visibility.Collapsed;
            action.Content = installed ? L.S.LabelInstalled : L.S.ButtonDownload;
            action.IsEnabled = !installed;
            detail.Text = baseDetail;
        }

        ShowIdle();

        // Строка тоже обновляется при смене папки: в новой эта модель может
        // быть уже установлена, а может и не быть.
        Refresh(() =>
        {
            if (!_models.Running.ContainsKey(model.FileName))
            {
                ShowIdle();
            }
        });

        action.Click += async (_, _) =>
        {
            if (_models.Running.TryGetValue(model.FileName, out CancellationTokenSource? running))
            {
                running.Cancel();
                return;
            }

            var cancellation = new CancellationTokenSource();
            _models.Running[model.FileName] = cancellation;

            action.Content = L.S.ButtonCancelDownload;
            action.IsEnabled = true;
            progress.Visibility = Visibility.Visible;
            progress.Value = 0;

            var reporter = new Progress<DownloadProgress>(p =>
            {
                progress.IsIndeterminate = p.Fraction is null;
                progress.Value = p.Fraction ?? 0;
                detail.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    L.S.DownloadProgress,
                    FormatSize(p.ReceivedBytes),
                    p.TotalBytes is { } total ? FormatSize(total) : "?",
                    FormatSize((long)p.BytesPerSecond));
            });

            try
            {
                using var downloader = new ModelDownloader();
                await downloader.DownloadAsync(model, TargetDirectory(), reporter, cancellation.Token);

                // Скачали первую модель — сразу ею и пользуемся: заставлять
                // выбирать её отдельным действием было бы бессмысленной
                // формальностью.
                if (model.Kind == ModelKind.Recognition && _availableModels().Count <= 1)
                {
                    Apply(s => s with { ModelFileName = model.FileName });
                }
                else if (model.Kind == ModelKind.SpeechDetector
                         && ModelLocator.ResolveVadModel(Settings.VadModelFileName, Settings.ModelsDirectory) is null)
                {
                    Apply(s => s with { VadModelFileName = model.FileName });
                }

                // Обновляем ВСЁ, что зависит от набора моделей: список
                // установленного, выбор модели распознавания, выбор детектора
                // и подписи на самих кнопках закачки. Раньше обновлялся только
                // список установленного, и скачанная модель не появлялась в
                // выборе до перезапуска окна.
                RefreshControls();
                ModelsChanged?.Invoke();
            }
            catch (OperationCanceledException)
            {
                detail.Text = L.S.DownloadCancelled;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
            {
                AppLog.Error($"Не удалось скачать {model.FileName}.", ex);
                detail.Text = string.Format(CultureInfo.CurrentCulture, L.S.DownloadFailed, ex.Message);
            }
            finally
            {
                _models.Running.Remove(model.FileName);
                cancellation.Dispose();

                bool installed = File.Exists(Path.Combine(TargetDirectory(), model.FileName));
                progress.Visibility = Visibility.Collapsed;
                action.Content = installed ? L.S.LabelInstalled : L.S.ButtonDownload;
                action.IsEnabled = !installed;
            }
        };
    }

    /// <summary>Размер файла в понятных человеку единицах.</summary>
    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_000_000_000 => $"{bytes / 1_000_000_000.0:F1} GB",
        >= 1_000_000 => $"{bytes / 1_000_000.0:F0} MB",
        >= 1_000 => $"{bytes / 1_000.0:F0} KB",
        _ => $"{bytes} B",
    };

    // --- раздел: текст -----------------------------------------------------

    private void BuildTextSection()
    {
        AddGroup(L.S.GroupParagraphs);

        AddToggleCard(
            SymbolRegular.TextParagraph24,
            L.S.FieldSplitParagraphs,
            description: null,
            () => Settings.SplitParagraphsByPauses,
            value => Apply(s => s with { SplitParagraphsByPauses = value }));

        AddCard(
            SymbolRegular.Timer24,
            L.S.FieldParagraphPause,
            description: null,
            NumberField(
                () => Settings.ParagraphPauseSeconds,
                L.S.Seconds,
                value => Apply(s => s with { ParagraphPauseSeconds = Math.Clamp(value, 0.2, 10) })));

        AddGroup(L.S.GroupReplacements);

        var replacementsBox = new TextBox
        {
            Text = FormatReplacements(Settings.Replacements),
            AcceptsReturn = true,
            MinHeight = 120,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, Courier New"),
        };

        replacementsBox.LostFocus += (_, _) =>
            Apply(s => s with { Replacements = ParseReplacements(replacementsBox.Text) });

        Refresh(() =>
        {
            if (!replacementsBox.IsKeyboardFocusWithin)
            {
                replacementsBox.Text = FormatReplacements(Settings.Replacements);
            }
        });

        AddStackedCard(
            SymbolRegular.ArrowSwap24,
            L.S.FieldReplacements,
            L.S.FieldReplacementsHint,
            replacementsBox,
            trailing: null);
    }

    // --- раздел: звонки ----------------------------------------------------

    private void BuildCallsSection()
    {
        _page.Children.Add(new InfoBar
        {
            Title = L.S.CallConsentTitle,
            Message = L.S.CallConsentNote,
            Severity = InfoBarSeverity.Warning,
            IsOpen = true,
            IsClosable = false,
            Margin = new Thickness(0, 0, 0, 14),
        });

        AddGroup(L.S.GroupRecording);

        AddCard(SymbolRegular.Keyboard24, L.S.FieldHotkey, L.S.FieldCallHotkeyHint, HotkeyField(HotkeyTarget.Call));

        AddCard(
            SymbolRegular.Speaker224,
            L.S.FieldSystemAudioDevice,
            L.S.FieldSystemAudioDeviceHint,
            DeviceCombo(
                DataFlow.Render,
                () => Settings.SystemAudioDeviceId,
                id => Apply(s => s with { SystemAudioDeviceId = id })));

        var callsFolderBox = new TextBox
        {
            Text = Settings.CallsDirectory ?? AppPaths.DefaultCallsDirectory,
            IsReadOnly = true,
        };

        Refresh(() => callsFolderBox.Text = Settings.CallsDirectory ?? AppPaths.DefaultCallsDirectory);

        AddStackedCard(
            SymbolRegular.FolderOpen24,
            L.S.FieldCallsFolder,
            description: null,
            callsFolderBox,
            FolderButtons(
                () => callsFolderBox.Text,
                picked => Apply(s => s with { CallsDirectory = picked }),
                () => Apply(s => s with { CallsDirectory = null })));

        AddCard(
            SymbolRegular.Timer24,
            L.S.FieldMaxCall,
            L.S.FieldMaxCallHint,
            NumberField(
                () => Settings.MaxCallMinutes,
                L.S.Minutes,
                value => Apply(s => s with { MaxCallMinutes = (int)Math.Clamp(value, 1, 24 * 60) })));

        AddGroup(L.S.GroupVoices);

        AddToggleCard(
            SymbolRegular.PeopleTeam24,
            L.S.FieldSplitVoices,
            L.S.FieldSplitVoicesHint,
            () => Settings.SplitVoices,
            value => Apply(s => s with { SplitVoices = value }));

        AddCard(
            SymbolRegular.PeopleTeam24,
            L.S.FieldVoiceEmbeddingModel,
            L.S.FieldVoiceEmbeddingModelHint,
            VoiceModelCombo(
                segmentation: false,
                () => Settings.VoiceEmbeddingModelFileName,
                name => Apply(s => s with { VoiceEmbeddingModelFileName = name })));

        AddCard(
            SymbolRegular.PulseSquare24,
            L.S.FieldVoiceSegmentationModel,
            L.S.FieldVoiceSegmentationModelHint,
            VoiceModelCombo(
                segmentation: true,
                () => Settings.VoiceSegmentationModelFileName,
                name => Apply(s => s with { VoiceSegmentationModelFileName = name })));

        AddCard(
            SymbolRegular.Options24,
            L.S.FieldVoiceThreshold,
            L.S.FieldVoiceThresholdHint,
            VoiceThresholdField());

        AddGroup(L.S.GroupTranscript);

        AddCard(
            SymbolRegular.Person24,
            L.S.FieldMyName,
            L.S.FieldMyNameHint,
            TextField(
                () => Settings.EffectiveMyName,
                value => Apply(s => s with { MyName = value.Trim().Length == 0 ? null : value.Trim() })));

        AddCard(
            SymbolRegular.PeopleTeam24,
            L.S.FieldOtherSideName,
            description: null,
            TextField(
                () => Settings.OtherSideName ?? L.S.TranscriptUnknownSpeaker,
                value => Apply(s => s with { OtherSideName = value.Trim().Length == 0 ? null : value.Trim() })));

        AddCard(
            SymbolRegular.LocalLanguage24,
            L.S.FieldOtherSideLanguage,
            L.S.FieldOtherSideLanguageHint,
            LanguageCombo(() => Settings.OtherSideLanguage, code => Apply(s => s with { OtherSideLanguage = code })));
    }

    // --- раздел: общие -----------------------------------------------------

    private void BuildGeneralSection()
    {
        AddGroup(L.S.GroupAppearance);

        var languageBox = new ComboBox { Width = ControlColumnWidth };
        languageBox.Items.Add(L.S.FieldUiLanguageAuto);
        languageBox.Items.Add(L.S.LanguageEnglish);
        languageBox.Items.Add(L.S.LanguageRussian);
        languageBox.SelectedIndex = Settings.UiLanguage switch { "en" => 1, "ru" => 2, _ => 0 };
        languageBox.SelectionChanged += (_, _) =>
        {
            string? value = languageBox.SelectedIndex switch { 1 => "en", 2 => "ru", _ => null };
            Apply(s => s with { UiLanguage = value });
        };

        AddCard(SymbolRegular.LocalLanguage24, L.S.FieldUiLanguage, description: null, languageBox);

        var themeBox = new ComboBox { Width = ControlColumnWidth };
        themeBox.Items.Add(L.S.ThemeSystem);
        themeBox.Items.Add(L.S.ThemeLight);
        themeBox.Items.Add(L.S.ThemeDark);
        themeBox.SelectedIndex = Settings.Theme switch
        {
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            _ => 0,
        };

        themeBox.SelectionChanged += (_, _) => Apply(s => s with
        {
            Theme = themeBox.SelectedIndex switch
            {
                1 => AppTheme.Light,
                2 => AppTheme.Dark,
                _ => AppTheme.System,
            },
        });

        Refresh(() => themeBox.SelectedIndex = Settings.Theme switch
        {
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            _ => 0,
        });

        AddCard(SymbolRegular.DarkTheme24, L.S.FieldTheme, description: null, themeBox);

        AddGroup(L.S.GroupStartupAndStorage);

        var autoStart = new ToggleSwitch
        {
            IsChecked = AutoStart.IsEnabled,
            IsEnabled = AutoStart.IsAvailable,
        };

        // Проверка на синхронизацию обязательна: обновление галочки из
        // реестра иначе тут же записывало бы прочитанное обратно.
        autoStart.Checked += (_, _) =>
        {
            if (!_refreshing)
            {
                AutoStart.SetEnabled(true);
            }
        };

        autoStart.Unchecked += (_, _) =>
        {
            if (!_refreshing)
            {
                AutoStart.SetEnabled(false);
            }
        };
        Refresh(() => autoStart.IsChecked = AutoStart.IsEnabled);

        AddCard(
            SymbolRegular.Power24,
            L.S.FieldAutoStart,
            AutoStart.IsAvailable ? L.S.FieldAutoStartHint : L.S.FieldAutoStartUnavailable,
            autoStart);

        // Читаем ЖИВОЕ состояние маркера, а не то, с которым стартовал
        // процесс: иначе переключатель показывал бы прежнее положение сразу
        // после нажатия и выглядел бы неработающим.
        AddToggleCard(
            SymbolRegular.UsbStick24,
            L.S.FieldPortable,
            L.S.FieldPortableHint + " " + L.S.RestartRequired + ".",
            () => AppPaths.IsPortableNow,
            SetPortable);

        AddGroup(L.S.GroupPrivacy);

        AddToggleCard(
            SymbolRegular.EyeOff24,
            L.S.FieldTrayPreview,
            L.S.FieldTrayPreviewHint,
            () => Settings.ShowTextPreviewInTray,
            value => Apply(s => s with { ShowTextPreviewInTray = value }));
    }

    /// <summary>
    /// Переключить портативный режим.
    /// </summary>
    /// <remarks>
    /// Запись файла-маркера может не пройти — например, если приложение стоит
    /// в Program Files без прав администратора. Раньше исключение отсюда
    /// уходило в никуда и убивало процесс без единого сообщения.
    /// </remarks>
    private void SetPortable(bool enabled)
    {
        try
        {
            AppPaths.SetPortable(enabled);
            RefreshControls(); // путь по умолчанию у моделей теперь другой
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Не удалось переключить портативный режим.", ex);
            ConfirmWindow.Ask(this, new ConfirmWindow(
                L.S.FieldPortable,
                ex.Message,
                primaryButton: L.S.ButtonClose,
                cancelButton: L.S.ButtonClose,
                icon: SymbolRegular.Warning24,
                danger: true));
        }
    }

    // --- раздел: о программе ------------------------------------------------

    private void BuildAboutSection()
    {
        _page.Children.Add(new TextBlock
        {
            Text = L.S.AboutTagline,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Margin = new Thickness(2, 0, 24, 14),
        });

        AddCard(SymbolRegular.Tag24, L.S.AboutVersion, description: null, ValueText(AppVersion));
        AddCard(SymbolRegular.People24, L.S.AboutAuthors, description: null, ValueText(L.S.AboutAuthorsValue));

        // Бэкенд показываем именно здесь: библиотека молча откатывается с GPU
        // на CPU, если нативную сборку не удалось загрузить, и без этой строки
        // разница в скорости в полсотни раз выглядит необъяснимой.
        AddCard(
            SymbolRegular.DeveloperBoard24,
            L.S.AboutRuntime,
            L.S.AboutRuntimeHint,
            ValueText(WhisperEngine.LoadedRuntime));

        AddCard(SymbolRegular.Document24, L.S.AboutLicense, description: null, ValueText("MIT"));

        // Ссылки — кнопками, а не текстом: адрес, который нельзя нажать,
        // придётся перепечатывать руками, а это ровно тот случай, когда
        // человек не станет и просто закроет окно.
        var links = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        links.Children.Add(new Wpf.Ui.Controls.HyperlinkButton
        {
            Content = SiteUrl.Replace("https://", string.Empty, StringComparison.Ordinal),
            NavigateUri = SiteUrl,
            Margin = new Thickness(0, 0, 8, 0),
        });

        links.Children.Add(new Wpf.Ui.Controls.HyperlinkButton
        {
            Content = L.S.AboutSource,
            NavigateUri = RepositoryUrl,
        });

        AddStackedCard(SymbolRegular.Link24, L.S.AboutSite, L.S.AboutSiteHint, links, trailing: null);

        var copy = new Button
        {
            Content = L.S.ButtonCopyDiagnostics,
            MinWidth = 180,
            Margin = new Thickness(0, 0, 8, 0),
        };

        copy.Click += (_, _) => CopyDiagnostics(copy);

        var openLog = new Button { Content = L.S.ButtonOpenLog, MinWidth = 140 };
        openLog.Click += (_, _) => OpenFile(AppLog.LogPath);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };

        buttons.Children.Add(copy);
        buttons.Children.Add(openLog);

        AddStackedCard(SymbolRegular.Bug24, "Tapybara", L.S.AboutComponents, buttons, trailing: null);
    }

    /// <summary>
    /// Скопировать сводку окружения.
    /// </summary>
    /// <remarks>
    /// Подпись кнопки возвращается назад. Прежняя версия меняла её навсегда:
    /// кнопка «Скопировать диагностику» превращалась в «Диагностика
    /// скопирована» и оставалась такой до конца сессии, продолжая при этом
    /// копировать.
    /// </remarks>
    private void CopyDiagnostics(Button button)
    {
        object original = button.Content;
        try
        {
            ClipboardWriter.SetText(BuildDiagnostics(), excludeFromHistory: false);
            button.Content = L.S.DiagnosticsCopied;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Не удалось скопировать диагностику.", ex);
            button.Content = ex.Message;
        }

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            button.Content = original;
        };

        timer.Start();
    }

    /// <summary>Сводка окружения — чтобы её можно было приложить к отчёту об ошибке.</summary>
    private string BuildDiagnostics()
    {
        var builder = new StringBuilder();
        builder.Append("Tapybara ").AppendLine(AppVersion);
        builder.Append("OS: ").AppendLine(Environment.OSVersion.VersionString);
        builder.Append("Runtime: ").AppendLine(Environment.Version.ToString());
        builder.Append("Backend: ").AppendLine(WhisperEngine.LoadedRuntime);
        builder.Append("Model: ").AppendLine(Settings.ModelFileName);
        builder.Append("Detector: ").AppendLine(
            Settings.UseVoiceActivityDetection ? Settings.VadModelFileName : "off");
        builder.Append("Language: ").AppendLine(Settings.Language);
        builder.Append("Data: ").AppendLine(AppPaths.DataDirectory);
        builder.Append("Portable: ").AppendLine(AppPaths.IsPortable ? "yes" : "no");
        builder.AppendLine().AppendLine("Recent log:");

        foreach (string line in AppLog.Tail(30))
        {
            builder.AppendLine(line);
        }

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
        VerticalAlignment = VerticalAlignment.Center,
    };

    // --- захват сочетания клавиш -------------------------------------------

    /// <summary>
    /// Кнопка-сочетание со строкой статуса под ней.
    /// </summary>
    /// <remarks>
    /// Одна на оба сочетания. Захват устроен одинаково, и копия кода для
    /// второго сочетания разошлась бы с первой на первой же правке.
    /// </remarks>
    private StackPanel HotkeyField(HotkeyTarget target)
    {
        var button = new Button { Content = HotkeyOf(target).ToString(), Width = ControlColumnWidth };
        button.Click += (_, _) => BeginHotkeyCapture(target);

        var status = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = ControlColumnWidth,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = ThemeBrush("SystemFillColorCautionBrush", Colors.OrangeRed),
        };

        _hotkeyFields[target] = (button, status);

        Refresh(() =>
        {
            if (_capturingHotkey != target)
            {
                button.Content = HotkeyOf(target).ToString();
            }
        });

        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        stack.Children.Add(button);
        stack.Children.Add(status);
        return stack;
    }

    private void BeginHotkeyCapture(HotkeyTarget target)
    {
        if (_capturingHotkey is { } previous && previous != target)
        {
            EndHotkeyCapture();
        }

        _capturingHotkey = target;
        (Button button, TextBlock status) = _hotkeyFields[target];
        button.Content = L.S.FieldHotkeyCapturing;
        status.Visibility = Visibility.Visible;
        status.Text = L.S.FieldHotkeyCaptureHint;
        HotkeyCaptureChanged?.Invoke(true);
    }

    private void EndHotkeyCapture()
    {
        if (_capturingHotkey is not { } target)
        {
            return;
        }

        _capturingHotkey = null;
        (Button button, TextBlock status) = _hotkeyFields[target];
        button.Content = HotkeyOf(target).ToString();
        status.Visibility = Visibility.Collapsed;
        HotkeyCaptureChanged?.Invoke(false);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_capturingHotkey is not { } target)
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
            _hotkeyFields[target].Button.Content = L.S.FieldHotkeyCapturing;
            return;
        }

        // Одно сочетание на два действия заняло бы хоткей дважды: вторая
        // регистрация провалилась бы и сообщила, что сочетание «занято другим
        // приложением», — хотя заняли его мы сами.
        HotkeyCombo other = HotkeyOf(target == HotkeyTarget.Call ? HotkeyTarget.Dictation : HotkeyTarget.Call);
        if (combo == other)
        {
            _hotkeyFields[target].Status.Text = string.Format(
                CultureInfo.CurrentCulture, L.S.FieldHotkeyDuplicate, combo);
            return;
        }

        Apply(s => target == HotkeyTarget.Call ? s with { CallHotkey = combo } : s with { Hotkey = combo });
        EndHotkeyCapture();

        // Занять сочетание пробует приложение, и оно же сообщит результат
        // через ReportHotkeyResult. Раньше окно показывало новое сочетание как
        // принятое, даже если занять его не удалось.
    }

    // --- построители карточек ----------------------------------------------

    /// <summary>
    /// Заголовок группы внутри страницы.
    /// </summary>
    /// <remarks>
    /// Так же устроены «Параметры» Windows: страница делится на подписанные
    /// группы по три-четыре карточки. Без них длинная страница читается как
    /// свалка, и понять, почему «Выгружать модель» стоит рядом с «Языком», —
    /// невозможно.
    /// </remarks>
    private void AddGroup(string title)
    {
        _page.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, _page.Children.Count == 0 ? 0 : 20, 0, 8),
        });
    }

    /// <summary>Карточка «иконка, заголовок с описанием — контрол справа».</summary>
    private void AddCard(SymbolRegular icon, string title, string? description, UIElement control)
    {
        var card = new CardControl
        {
            Icon = new SymbolIcon { Symbol = icon },
            Header = BuildHeader(title, description),
            Content = control,
            Margin = new Thickness(0, 0, 0, 4),
        };

        AutomationProperties.SetName(card, title);
        _page.Children.Add(card);
    }

    private void AddToggleCard(
        SymbolRegular icon,
        string title,
        string? description,
        Func<bool> read,
        Action<bool> onChange)
    {
        var toggle = new ToggleSwitch { IsChecked = read() };
        AutomationProperties.SetName(toggle, title);

        toggle.Checked += (_, _) => onChange(true);
        toggle.Unchecked += (_, _) => onChange(false);
        Refresh(() => toggle.IsChecked = read());

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
            VerticalAlignment = VerticalAlignment.Top,
        });

        head.Children.Add(BuildHeader(title, description, maxWidth: 520));
        content.Children.Add(head);
        content.Children.Add(control);

        if (trailing is not null)
        {
            content.Children.Add(trailing);
        }

        var border = new Border
        {
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush", Colors.Transparent),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush", Colors.Gray),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 4),
            Child = content,
        };

        AutomationProperties.SetName(border, title);
        _page.Children.Add(border);
    }

    /// <summary>Карточка без заголовка — только содержимое.</summary>
    private void AddPlainCard(UIElement content)
    {
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

    private static StackPanel BuildHeader(string title, string? description, double maxWidth = 360)
    {
        // Прижимаем влево явно: без этого CardControl центрирует заголовок в
        // отведённой ему ширине, и карточки без описания выглядят съехавшими
        // относительно карточек с описанием — колонка заголовков «пляшет».
        var panel = new StackPanel
        {
            MaxWidth = maxWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
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

    // --- поля --------------------------------------------------------------

    /// <summary>Однострочное поле, применяющее значение по потере фокуса.</summary>
    /// <remarks>
    /// Именно по потере фокуса, а не по каждому нажатию клавиши: иначе
    /// настройка сохранялась бы на каждую букву, а промежуточные обрывки
    /// вроде «Со» успели бы попасть в транскрипт.
    /// </remarks>
    private TextBox TextField(Func<string> read, Action<string> onChange)
    {
        var box = new TextBox { Width = ControlColumnWidth, Text = read() };
        box.LostFocus += (_, _) =>
        {
            string value = box.Text.Trim();
            if (value.Length > 0)
            {
                onChange(value);
            }
            else
            {
                box.Text = read();
            }
        };

        Refresh(() =>
        {
            if (!box.IsKeyboardFocusWithin)
            {
                box.Text = read();
            }
        });

        return box;
    }

    /// <summary>Поле для числа с подписью единиц измерения.</summary>
    private StackPanel NumberField(Func<double> read, string unit, Action<double> onChange)
    {
        var box = new TextBox
        {
            Text = read().ToString(CultureInfo.CurrentCulture),
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

            box.Text = read().ToString(CultureInfo.CurrentCulture);
        };

        Refresh(() =>
        {
            if (!box.IsKeyboardFocusWithin)
            {
                box.Text = read().ToString(CultureInfo.CurrentCulture);
            }
        });

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        panel.Children.Add(box);
        panel.Children.Add(new TextBlock
        {
            Text = unit,
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        });

        return panel;
    }

    /// <summary>Выбор звукового устройства с пунктом «по умолчанию».</summary>
    private ComboBox DeviceCombo(DataFlow flow, Func<string?> read, Action<string?> onChange)
    {
        var box = new ComboBox { Width = ControlColumnWidth };

        void Fill()
        {
            box.Items.Clear();
            box.Items.Add(new DeviceChoice(null, L.S.DeviceSystemDefault));

            foreach (AudioDeviceInfo device in flow == DataFlow.Capture
                         ? AudioDevices.Inputs()
                         : AudioDevices.Outputs())
            {
                box.Items.Add(new DeviceChoice(device.Id, device.Name));
            }

            string? current = read();
            int index = 0;
            for (int i = 0; i < box.Items.Count; i++)
            {
                if (box.Items[i] is DeviceChoice choice && choice.Id == current)
                {
                    index = i;
                    break;
                }
            }

            box.SelectedIndex = index;
        }

        Fill();
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is DeviceChoice choice && choice.Id != read())
            {
                onChange(choice.Id);
            }
        };

        // Устройства появляются и исчезают, пока окно открыто: гарнитуру
        // втыкают именно тогда, когда собираются ею пользоваться.
        box.DropDownOpened += (_, _) => Fill();
        Refresh(Fill);
        return box;
    }

    private sealed record DeviceChoice(string? Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record LanguageChoice(string Code, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Поле порога разделения голосов: доля от нуля до единицы, без единиц.
    /// </summary>
    /// <remarks>
    /// Своё поле, а не общий <see cref="NumberField"/>: тому нужна подпись
    /// единицы измерения, а у доли её нет, и пустая подпись рядом со значением
    /// выглядит как недогруженный интерфейс.
    /// </remarks>
    private System.Windows.Controls.TextBox VoiceThresholdField()
    {
        var box = new System.Windows.Controls.TextBox
        {
            Text = Settings.VoiceSplitThreshold.ToString("F2", CultureInfo.CurrentCulture),
            Width = 80,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        };

        box.LostFocus += (_, _) =>
        {
            string normalized = box.Text.Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                double clamped = Math.Clamp(parsed, 0.1, 0.95);
                box.Text = clamped.ToString("F2", CultureInfo.CurrentCulture);
                Apply(s => s with { VoiceSplitThreshold = clamped });
            }
            else
            {
                box.Text = Settings.VoiceSplitThreshold.ToString("F2", CultureInfo.CurrentCulture);
            }
        };

        Refresh(() =>
        {
            if (!box.IsKeyboardFocusWithin)
            {
                box.Text = Settings.VoiceSplitThreshold.ToString("F2", CultureInfo.CurrentCulture);
            }
        });

        return box;
    }

    /// <summary>
    /// Выбор модели разделения голосов из того, что лежит в папке.
    /// </summary>
    /// <remarks>
    /// Настроенное имя показываем, даже если файла нет: иначе список молча
    /// перескакивал бы на другую модель, и человек, не скачавший нужную,
    /// считал бы, что всё в порядке.
    /// </remarks>
    private ComboBox VoiceModelCombo(bool segmentation, Func<string> read, Action<string> onChange)
    {
        var box = new ComboBox { Width = ControlColumnWidth };

        void Fill()
        {
            string current = read();
            box.Items.Clear();

            foreach (string name in ModelLocator.ListVoiceModels(segmentation, Settings.ModelsDirectory))
            {
                box.Items.Add(name);
            }

            if (!box.Items.Contains(current))
            {
                box.Items.Add(current);
            }

            box.SelectedItem = current;
        }

        Fill();
        Refresh(Fill);

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string name && name != read())
            {
                onChange(name);
            }
        };

        return box;
    }

    private sealed record DecodingChoice(int BeamSize, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Выбор ширины луча при декодировании.
    /// </summary>
    /// <remarks>
    /// Список из трёх пунктов, а не поле для числа. Ширина луча — величина не
    /// с непрерывным смыслом: между пятью и шестью разницы на слух нет, а
    /// между единицей и пятёркой — есть, и она в другом качестве текста, а не
    /// в «чуть-чуть лучше». Поле для числа приглашало бы подбирать то, что
    /// подбору не поддаётся.
    /// <para>
    /// Значение из настроек может не совпасть ни с одним пунктом: файл правят
    /// руками. Тогда показываем ближайший разумный, а не пустой список.
    /// </para>
    /// </remarks>
    private ComboBox DecodingCombo()
    {
        var box = new ComboBox { Width = ControlColumnWidth };

        box.Items.Add(new DecodingChoice(1, L.S.DecodingFast));
        box.Items.Add(new DecodingChoice(5, L.S.DecodingAccurate));
        box.Items.Add(new DecodingChoice(8, L.S.DecodingThorough));

        void Show()
        {
            int current = Settings.BeamSize;
            box.SelectedItem = box.Items.OfType<DecodingChoice>()
                .OrderBy(c => Math.Abs(c.BeamSize - current))
                .First();
        }

        Show();
        Refresh(Show);

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is DecodingChoice choice)
            {
                Apply(s => s with { BeamSize = choice.BeamSize });
            }
        };

        return box;
    }

    /// <summary>
    /// Выбор языка распознавания.
    /// </summary>
    /// <remarks>
    /// Список, а не текстовое поле. Whisper знает девяносто девять языков и
    /// принимает только двухбуквенные коды; «russian» вместо «ru» он молча
    /// понимает как «не задано» — и вся диктовка едет. Поле остаётся
    /// редактируемым, чтобы можно было вписать редкий код, но введённое
    /// проверяется.
    /// </remarks>
    private ComboBox LanguageCombo(Func<string> read, Action<string> onChange)
    {
        var box = new ComboBox
        {
            Width = ControlColumnWidth,
            IsEditable = true,
            IsTextSearchEnabled = true,
        };

        // Названия языков остаются английскими: «Russian», а не «русский».
        // Так они записаны в самой модели и так же выглядят во всей
        // документации whisper — переводить их значило бы расходиться с
        // источником. А вот «определять автоматически» — это подпись
        // интерфейса, и она обязана быть на языке интерфейса.
        foreach (WhisperLanguage language in WhisperLanguages.All)
        {
            box.Items.Add(new LanguageChoice(
                language.Code,
                language.Code == WhisperLanguages.AutoCode ? L.S.LanguageAutoDetect : language.ToString()));
        }

        void Show()
        {
            string current = read();
            LanguageChoice? known = box.Items.OfType<LanguageChoice>().FirstOrDefault(
                c => string.Equals(c.Code, current, StringComparison.OrdinalIgnoreCase));

            if (known is not null)
            {
                box.SelectedItem = known;
            }
            else
            {
                box.SelectedItem = null;
                box.Text = current;
            }
        }

        Show();

        box.LostFocus += (_, _) =>
        {
            // Принимаем и код, и название, и запись с регионом. Неразобранное
            // просто откатывается к текущему значению: молча сохранить
            // «russian» вместо «ru» означало бы сломать распознавание так,
            // что виноватой выглядела бы модель.
            string? code = WhisperLanguages.Normalize(box.Text)
                           ?? (string.Equals(box.Text.Trim(), L.S.LanguageAutoDetect, StringComparison.OrdinalIgnoreCase)
                               ? WhisperLanguages.AutoCode
                               : null);

            if (code is not null)
            {
                onChange(code);
            }

            Show();
        };

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is LanguageChoice choice && choice.Code != read())
            {
                onChange(choice.Code);
            }
        };

        Refresh(() =>
        {
            if (!box.IsKeyboardFocusWithin)
            {
                Show();
            }
        });

        return box;
    }

    /// <summary>Кнопки «Обзор», «Открыть» и «По умолчанию» под полем пути.</summary>
    private StackPanel FolderButtons(Func<string> read, Action<string> onPicked, Action? onReset = null)
    {
        var browse = new Button { Content = L.S.ButtonBrowse, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = read() };
            if (dialog.ShowDialog(this) == true)
            {
                onPicked(dialog.FolderName);
            }
        };

        var open = new Button { Content = L.S.ButtonOpen, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
        open.Click += (_, _) => OpenFolder(read());

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
        };

        buttons.Children.Add(browse);
        buttons.Children.Add(open);

        if (onReset is not null)
        {
            var reset = new Button { Content = L.S.ButtonUseDefault, MinWidth = 130 };
            reset.Click += (_, _) => onReset();
            buttons.Children.Add(reset);
        }

        return buttons;
    }

    /// <summary>
    /// Сменить папку моделей, предложив перенести то, что уже скачано.
    /// </summary>
    /// <param name="picked">Новая папка или <c>null</c> — вернуться к умолчанию.</param>
    /// <remarks>
    /// Просто переставить путь мало: модели весят гигабайты, и человек,
    /// сменивший папку, почти наверняка хочет забрать их с собой, а не
    /// скачивать заново. Спрашиваем — потому что бывает и наоборот: папку
    /// меняют как раз затем, чтобы начать с чистого места.
    /// </remarks>
    private async Task ChangeModelsFolderAsync(string? picked)
    {
        string oldDirectory = TargetDirectory();
        string newDirectory = picked ?? AppPaths.PendingDefaultModelsDirectory;

        if (string.Equals(
                Path.TrimEndingDirectorySeparator(oldDirectory),
                Path.TrimEndingDirectorySeparator(newDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IReadOnlyList<InstalledModel> existing = ModelStorage.List(oldDirectory);
        if (existing.Count > 0)
        {
            // Оба пути показываем целиком. Без них вопрос «перенести в новую
            // папку?» требует от человека помнить, какую папку он выбрал
            // полсекунды назад в системном диалоге, и какая была до неё.
            // Отдельная фраза для единственного числа. «1 models are
            // sitting» — мелочь, но именно по таким мелочам видно, что текст
            // собран машиной и никем не прочитан.
            string size = FormatSize(existing.Sum(m => m.Bytes));
            string question = existing.Count == 1
                ? string.Format(CultureInfo.CurrentCulture, L.S.ModelsMoveQuestionOne, size)
                : string.Format(CultureInfo.CurrentCulture, L.S.ModelsMoveQuestion, existing.Count, size);

            var dialog = new ConfirmWindow(
                L.S.ModelsMoveTitle,
                question,
                primaryButton: L.S.ButtonMove,
                cancelButton: L.S.ButtonCancel,
                secondaryButton: L.S.ButtonKeep,
                icon: SymbolRegular.FolderArrowRight24);

            dialog.AddDetail(L.S.LabelFrom, oldDirectory);
            dialog.AddDetail(L.S.LabelTo, newDirectory);

            ConfirmChoice answer = ConfirmWindow.Ask(this, dialog);

            if (answer == ConfirmChoice.Cancel)
            {
                return;
            }

            if (answer == ConfirmChoice.Primary)
            {
                IsEnabled = false;
                try
                {
                    var progress = new Progress<string>(name => FooterHint.Text =
                        string.Format(CultureInfo.CurrentCulture, L.S.ModelsMoving, name));

                    MoveResult result = await ModelStorage.MoveAllAsync(oldDirectory, newDirectory, progress);
                    if (result.Failed.Count > 0)
                    {
                        ConfirmWindow.Ask(this, new ConfirmWindow(
                            L.S.ModelsMoveTitle,
                            string.Format(
                                CultureInfo.CurrentCulture,
                                L.S.ModelsMoveFailed,
                                string.Join(", ", result.Failed)),
                            primaryButton: L.S.ButtonClose,
                            cancelButton: L.S.ButtonClose,
                            icon: SymbolRegular.Warning24,
                            danger: true));
                    }
                }
                finally
                {
                    IsEnabled = true;
                    FooterHint.Text = string.Format(
                        CultureInfo.CurrentCulture, L.S.FooterStoragePath, AppPaths.DataDirectory);
                }
            }
        }

        Apply(s => s with { ModelsDirectory = picked });
        ModelsChanged?.Invoke();
    }

    private static void OpenFolder(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directory}\"",
                UseShellExecute = false,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            AppLog.Warn($"Не удалось открыть {directory}.", ex);
        }
    }

    private static void OpenFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppLog.Warn($"Не удалось открыть {path}.", ex);
        }
    }

    /// <summary>Кисть из темы, с запасным цветом, если ресурса нет.</summary>
    private Brush ThemeBrush(string resourceKey, Color fallback) =>
        TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallback);

    private void RefillModelBox(ComboBox box)
    {
        IReadOnlyList<string> models = _availableModels();
        box.Items.Clear();
        foreach (string model in models)
        {
            box.Items.Add(model);
        }

        int index = models.ToList().FindIndex(
            m => string.Equals(m, Settings.ModelFileName, StringComparison.OrdinalIgnoreCase));

        box.SelectedIndex = index >= 0 ? index : (models.Count > 0 ? 0 : -1);
    }

    private void RefillVadBox(ComboBox box, IReadOnlyList<string> models)
    {
        box.Items.Clear();
        foreach (string model in models)
        {
            box.Items.Add(model);
        }

        int index = models.ToList().FindIndex(
            m => string.Equals(m, Settings.VadModelFileName, StringComparison.OrdinalIgnoreCase));

        box.SelectedIndex = index >= 0 ? index : (models.Count > 0 ? 0 : -1);
    }

    private static string PrettyModelName(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        return name.StartsWith("ggml-", StringComparison.OrdinalIgnoreCase) ? name[5..] : name;
    }

    // --- словарь замен -----------------------------------------------------

    private static string FormatReplacements(IReadOnlyDictionary<string, string> replacements)
    {
        var builder = new StringBuilder();
        foreach ((string from, string to) in replacements.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
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

    // --- связь с настройками ------------------------------------------------

    private void Refresh(Action refresher) => _refreshers.Add(refresher);

    private void Apply(Func<AppSettings, AppSettings> mutate)
    {
        if (_refreshing)
        {
            return; // эхо синхронизации, а не выбор пользователя
        }

        _host.Update(mutate);
    }

    /// <summary>
    /// Настройки изменил кто-то другой — подтягиваем значения в контролы.
    /// </summary>
    /// <remarks>
    /// Именно этого раньше не было. Окно держало собственный снимок настроек,
    /// сделанный при открытии, и любая правка отправляла назад его целиком:
    /// сменить модель из меню трея, а потом щёлкнуть здесь что угодно — и
    /// модель возвращалась к прежней.
    /// </remarks>
    private void OnHostChanged(SettingsChange change) => RefreshControls();

    /// <summary>Подтянуть в контролы текущие значения настроек.</summary>
    private void RefreshControls()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            foreach (Action refresher in _refreshers)
            {
                refresher();
            }
        }
        finally
        {
            _refreshing = false;
        }
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

    /// <summary>
    /// Применить то, что человек печатал прямо перед закрытием.
    /// </summary>
    /// <remarks>
    /// Поля применяются по потере фокуса. Кнопка «Закрыть» фокус забирает и
    /// потому работала, а крестик в заголовке и Alt+F4 — нет: набранная
    /// подсказка или страница замен пропадали молча. Здесь фокус
    /// принудительно уводится на окно, и все обработчики успевают сработать.
    /// </remarks>
    protected override void OnClosing(CancelEventArgs e)
    {
        Focus();
        Keyboard.ClearFocus();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_capturingHotkey is not null)
        {
            HotkeyCaptureChanged?.Invoke(false); // не оставить хоткей снятым
        }

        foreach (CancellationTokenSource cancellation in _models.Running.Values)
        {
            cancellation.Cancel();
        }

        base.OnClosed(e);
    }

    /// <summary>Состояние страницы моделей, переживающее пересборку страниц.</summary>
    private sealed class ModelsPageState
    {
        public Dictionary<string, CancellationTokenSource> Running { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
