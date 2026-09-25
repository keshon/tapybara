using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Settings;
using Wpf.Ui.Controls;

using Button = Wpf.Ui.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Tapybara.App;

/// <summary>
/// Словарь: замены, подсказка для модели и имена людей.
/// </summary>
/// <remarks>
/// <para>
/// Всё, чем исправляют то, что модель слышит не так, — в одном месте и в
/// главном окне, а не в настройках. Замены пополняют постоянно, в том числе
/// прямо из транскрипта звонка; раньше это было многострочное поле в разделе
/// настроек «Текст» в формате «услышано = правильно», куда заходят раз в
/// месяц.
/// </para>
/// <para>
/// Подсказка для модели здесь же: она тоже про словарь — имена, термины,
/// стиль, — а не про устройство распознавания.
/// </para>
/// <para>
/// Словарь растёт годами, и страница рассчитана на сотни замен: одна строка
/// на правильное слово, а не на каждый вариант того, как его услышали;
/// поиск, как только строк становится больше, чем видно на экране; и
/// выгрузка в файл — чтобы на новом компьютере не набирать всё заново.
/// </para>
/// <para>
/// Собирается кодом, как карточки настроек: строки замен и имён — это
/// пары полей, которые добавляются и удаляются, и шаблон с привязками
/// только спрятал бы простую логику.
/// </para>
/// </remarks>
public sealed class DictionaryPage : System.Windows.Controls.UserControl, IDisposable
{
    /// <summary>
    /// Со скольких строк замен показывать поиск.
    /// </summary>
    /// <remarks>
    /// Пока замен горстка, поле поиска — лишний элемент над тремя строками.
    /// Восемь — примерно столько помещается в карточку без прокрутки.
    /// </remarks>
    private const int SearchFrom = 8;

    private readonly SettingsHost _settings;
    private readonly VoiceBook _voices;
    private readonly Func<string> _callsDirectory;
    private readonly Action<string> _render;
    private readonly StackPanel _root = new() { Margin = new Thickness(4, 0, 0, 0), MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _replacements = new();
    private readonly List<ReplacementRow> _rows = [];
    private readonly StackPanel _people = new();
    private readonly TextBox _prompt;
    private readonly TextBox _search;
    private readonly TextBlock _noMatches;
    private readonly TextBlock _status;

    public DictionaryPage(SettingsHost settings, VoiceBook voices, Func<string> callsDirectory, Action<string> render)
    {
        _settings = settings;
        _voices = voices;
        _callsDirectory = callsDirectory;
        _render = render;
        _voices.Changed += OnVoicesChanged;

        _prompt = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            ClearButtonEnabled = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _prompt.LostFocus += (_, _) =>
        {
            string value = _prompt.Text.Trim();
            _settings.Update(s => s with { Prompt = value.Length == 0 ? null : value });
        };

        _search = new TextBox
        {
            Width = 260,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 },
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _search.TextChanged += (_, _) => ApplyFilter();

        _noMatches = Ui.BodySecondary(string.Empty);
        _noMatches.Margin = new Thickness(0, Tokens.Space2, 0, Tokens.Space2);

        _status = Ui.BodySecondary(string.Empty);
        _status.Margin = new Thickness(0, Tokens.Space3, 0, 0);
        _status.Visibility = Visibility.Collapsed;

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = Tokens.PagePadding,
            Content = _root,
        };

        _settings.Changed += OnSettingsChanged;
        Build();
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _voices.Changed -= OnVoicesChanged;
        GC.SuppressFinalize(this);
    }

    private void OnVoicesChanged()
    {
        if (!IsKeyboardFocusWithin)
        {
            Dispatcher.BeginInvoke(Build);
        }
    }

    /// <summary>Подставить надписи текущего языка.</summary>
    public void ApplyLanguage() => Build();

    private AppSettings Settings => _settings.Current;

    /// <summary>
    /// Настройки поменяли снаружи — например, замену добавили из транскрипта.
    /// </summary>
    /// <remarks>
    /// Перестраиваем, только если человек сейчас не печатает на этой
    /// странице: иначе пересборка выдернула бы поле у него из-под пальцев.
    /// </remarks>
    private void OnSettingsChanged(SettingsChange change)
    {
        bool relevant = !ReferenceEquals(change.Previous.Replacements, change.Current.Replacements)
                        || !ReferenceEquals(change.Previous.KnownParticipants, change.Current.KnownParticipants)
                        || change.Previous.Prompt != change.Current.Prompt;

        if (relevant && !IsKeyboardFocusWithin)
        {
            Build();
        }
    }

    private void Build()
    {
        _root.Children.Clear();

        // Поля и списки живут между пересборками — отцепляем их от прежних
        // карточек, иначе WPF откажется вставлять элемент во второго родителя.
        Detach(_replacements);
        Detach(_prompt);
        Detach(_people);
        Detach(_search);
        Detach(_noMatches);
        Detach(_status);

        _root.Children.Add(Header());

        TextBlock intro = Ui.BodySecondary(L.S.DictionaryIntro);
        intro.Margin = new Thickness(0, Tokens.Space2, 0, 0);
        _root.Children.Add(intro);
        _root.Children.Add(_status);

        // --- замены
        _root.Children.Add(Group(L.S.GroupReplacements, L.S.DictionaryReplacementsHint));
        _search.PlaceholderText = L.S.DictionarySearch;
        _noMatches.Text = L.S.DictionaryNoMatches;
        FillReplacements();

        var add = new Button
        {
            Content = L.S.ReplacementTitle,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Add24 },
            VerticalAlignment = VerticalAlignment.Center,
        };
        add.Click += (_, _) =>
        {
            // Новая строка — сверху, а не в конце списка из сотни: иначе её
            // пришлось бы искать прокруткой сразу после нажатия.
            _search.Text = string.Empty;
            ReplacementRow row = AddReplacementRow([], string.Empty, atTop: true);
            row.NewVariant.Focus();
        };

        System.Windows.Controls.Button applyToCalls = Ui.Link(L.S.DictionaryApplyToCalls);
        applyToCalls.VerticalAlignment = VerticalAlignment.Center;
        applyToCalls.Margin = new Thickness(Tokens.Space4, 0, 0, 0);
        applyToCalls.Click += (_, _) => ApplyToPastCalls();

        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, Tokens.Space3) };
        toolbar.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { add, applyToCalls } });
        toolbar.Children.Add(_search);

        _root.Children.Add(Card(new StackPanel { Children = { toolbar, _replacements, _noMatches } }));
        ApplyFilter();

        // --- подсказка
        _root.Children.Add(Group(L.S.FieldPrompt, L.S.FieldPromptHint));
        if (!_prompt.IsKeyboardFocusWithin)
        {
            _prompt.Text = Settings.Prompt ?? string.Empty;
        }

        var reset = new Button
        {
            Content = L.S.ButtonDefault,
            Margin = new Thickness(0, Tokens.Space2, 0, 0),
        };
        reset.Click += (_, _) =>
        {
            _prompt.Text = LanguageDefaults.DefaultPrompt(Settings.Language);
            _settings.Update(s => s with { Prompt = _prompt.Text });
        };

        _root.Children.Add(Card(new StackPanel { Children = { _prompt, reset } }));

        // --- люди
        _root.Children.Add(Group(L.S.DictionaryPeople, L.S.DictionaryPeopleHint));
        FillPeople();
        _root.Children.Add(Card(_people));
    }

    /// <summary>Заголовок страницы и перенос словаря в файл и из файла.</summary>
    private Grid Header()
    {
        var header = new Grid();
        header.Children.Add(Ui.Title(L.S.NavDictionary));

        var import = new Button { Content = L.S.DictionaryImport, Margin = new Thickness(0, 0, Tokens.Space2, 0) };
        import.Click += (_, _) => Import();
        var export = new Button { Content = L.S.DictionaryExport };
        export.Click += (_, _) => Export();

        header.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { import, export },
        });
        return header;
    }

    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case Border border:
                border.Child = null;
                break;
        }
    }

    /// <summary>Заголовок группы — как в окне настроек: название и одна строка пояснения.</summary>
    private static StackPanel Group(string title, string hint)
    {
        var group = new StackPanel { Margin = new Thickness(0, Tokens.Space5, 0, Tokens.Space2) };
        group.Children.Add(Ui.BodyStrong(title));
        TextBlock caption = Ui.Caption(hint);
        caption.Margin = new Thickness(0, 2, 0, 0);
        group.Children.Add(caption);
        return group;
    }

    private static Border Card(UIElement child) => Ui.Card(child);

    /// <summary>Кнопка удаления строки — одна и та же у замен и у людей.</summary>
    private static Button DeleteButton() => new()
    {
        Icon = new SymbolIcon { Symbol = SymbolRegular.Delete24 },
        Appearance = ControlAppearance.Transparent,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(Tokens.Space2, 0, 0, 0),
        ToolTip = L.S.ButtonDelete,
    };

    // --- замены --------------------------------------------------------------

    /// <summary>
    /// Строка замены: несколько вариантов «как услышано» → одно «как надо».
    /// </summary>
    /// <remarks>
    /// Хранятся замены по-прежнему парами (так их применяет распознавание),
    /// а показываются по правильному слову. Модель коверкает одно название
    /// на пять ладов, и пять одинаковых строк «… → YouGile» превращали
    /// словарь в простыню, где правильное слово повторялось, а варианты
    /// терялись.
    /// </remarks>
    private sealed class ReplacementRow
    {
        public required Grid Root { get; init; }

        public required WrapPanel Variants { get; init; }

        public required TextBox NewVariant { get; init; }

        public required TextBox Correct { get; init; }

        public List<string> Heard { get; } = [];

        public bool Matches(string query) =>
            query.Length == 0
            || Correct.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || Heard.Any(h => h.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void FillReplacements()
    {
        _replacements.Children.Clear();
        _rows.Clear();

        IEnumerable<IGrouping<string, KeyValuePair<string, string>>> groups = Settings.Replacements
            .GroupBy(p => p.Value, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

        foreach (IGrouping<string, KeyValuePair<string, string>> group in groups)
        {
            AddReplacementRow(
                [.. group.Select(p => p.Key).Order(StringComparer.CurrentCultureIgnoreCase)],
                group.Key,
                atTop: false);
        }
    }

    private ReplacementRow AddReplacementRow(IReadOnlyList<string> heard, string correct, bool atTop)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, Tokens.Space2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var variants = new WrapPanel();
        // Узкое: поле ввода — хвост ряда плашек, а не ещё одна плашка. Шириной
        // с плашку оно уезжало на отдельную строку уже при трёх вариантах.
        var newVariant = new TextBox
        {
            Width = 120,
            ClearButtonEnabled = false,
            Margin = new Thickness(0, 0, 0, Tokens.Space1),
        };
        variants.Children.Add(newVariant);

        var arrow = new SymbolIcon
        {
            Symbol = SymbolRegular.ArrowRight16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9, 0, 0),
        };
        arrow.SetResourceReference(ForegroundProperty, "TextFillColorTertiaryBrush");

        var to = new TextBox
        {
            Text = correct,
            PlaceholderText = L.S.DictionaryCorrectPlaceholder,
            ClearButtonEnabled = false,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Button remove = DeleteButton();

        Grid.SetColumn(arrow, 1);
        Grid.SetColumn(to, 2);
        Grid.SetColumn(remove, 3);
        grid.Children.Add(variants);
        grid.Children.Add(arrow);
        grid.Children.Add(to);
        grid.Children.Add(remove);

        var row = new ReplacementRow { Root = grid, Variants = variants, NewVariant = newVariant, Correct = to };
        foreach (string h in heard)
        {
            AddVariantChip(row, h);
        }

        UpdateVariantPlaceholder(row);

        newVariant.KeyDown += (_, e) => OnNewVariantKey(row, e);
        newVariant.LostFocus += (_, _) => CommitNewVariant(row);
        to.LostFocus += (_, _) => SaveReplacements();
        remove.Click += (_, _) =>
        {
            _replacements.Children.Remove(grid);
            _rows.Remove(row);
            SaveReplacements();
            ApplyFilter();
        };

        if (atTop)
        {
            _replacements.Children.Insert(0, grid);
            _rows.Insert(0, row);
        }
        else
        {
            _replacements.Children.Add(grid);
            _rows.Add(row);
        }

        return row;
    }

    /// <summary>
    /// Enter в поле варианта — принять вариант и остаться в поле: вариантов
    /// обычно несколько, и набирать их хочется подряд.
    /// </summary>
    private void OnNewVariantKey(ReplacementRow row, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        CommitNewVariant(row);
    }

    /// <summary>
    /// Принять набранное в поле варианта.
    /// </summary>
    /// <remarks>
    /// Запятая разделяет варианты: список, скопированный откуда-то целиком,
    /// должен лечь чипами, а не одним странным вариантом «ugel, ugl».
    /// </remarks>
    private void CommitNewVariant(ReplacementRow row)
    {
        string[] typed = row.NewVariant.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (typed.Length == 0)
        {
            return;
        }

        foreach (string variant in typed)
        {
            // Замены применяются без учёта регистра, так что «Ugel» и «ugel» —
            // один и тот же вариант, и второй чип был бы обманом.
            if (!row.Heard.Contains(variant, StringComparer.OrdinalIgnoreCase))
            {
                AddVariantChip(row, variant);
            }
        }

        row.NewVariant.Text = string.Empty;
        UpdateVariantPlaceholder(row);
        SaveReplacements();
    }

    private static void UpdateVariantPlaceholder(ReplacementRow row) =>
        row.NewVariant.PlaceholderText = row.Heard.Count == 0 ? L.S.DictionaryHeardPlaceholder : L.S.DictionaryAnotherVariant;

    private void AddVariantChip(ReplacementRow row, string variant)
    {
        // Плоский крестик, а не кнопка в рамке: рамка внутри плашки делала
        // из каждого варианта две коробки, и строка из трёх вариантов
        // читалась как шесть элементов.
        System.Windows.Controls.Button dismiss = Ui.Link(string.Empty);
        dismiss.Content = new SymbolIcon { Symbol = SymbolRegular.Dismiss12, FontSize = 12 };
        dismiss.Padding = new Thickness(Tokens.Space1);
        dismiss.Margin = new Thickness(Tokens.Space1, 0, 0, 0);
        dismiss.VerticalAlignment = VerticalAlignment.Center;
        dismiss.ToolTip = L.S.ButtonDelete;
        dismiss.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        TextBlock text = Ui.Body(variant);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.TextWrapping = TextWrapping.NoWrap;

        var chip = new Border
        {
            Height = 32,
            Padding = new Thickness(Tokens.Space3, 0, Tokens.Space1, 0),
            Margin = new Thickness(0, 0, Tokens.Space1 + 2, Tokens.Space1),
            CornerRadius = Tokens.ControlRadius,
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { text, dismiss } },
        };
        chip.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
        chip.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");

        dismiss.Click += (_, _) =>
        {
            row.Variants.Children.Remove(chip);
            row.Heard.Remove(variant);
            UpdateVariantPlaceholder(row);
            SaveReplacements();
        };

        // Перед полем ввода: оно всегда последнее в ряду.
        row.Variants.Children.Insert(row.Variants.Children.Count - 1, chip);
        row.Heard.Add(variant);
    }

    /// <summary>
    /// Собрать замены из строк и записать.
    /// </summary>
    /// <remarks>
    /// Строка без правильного слова или без вариантов пропускается, а не
    /// удаляется: человек мог начать с любой половины. Исчезнет она при
    /// следующей сборке страницы, если так и останется недописанной.
    /// </remarks>
    private void SaveReplacements()
    {
        var map = new Dictionary<string, string>();
        foreach (ReplacementRow row in _rows)
        {
            string correct = row.Correct.Text.Trim();
            if (correct.Length == 0)
            {
                continue;
            }

            foreach (string heard in row.Heard)
            {
                map[heard] = correct;
            }
        }

        bool same = map.Count == Settings.Replacements.Count
                    && map.All(p => Settings.Replacements.TryGetValue(p.Key, out string? v) && v == p.Value);
        if (!same)
        {
            _settings.Update(s => s with { Replacements = map });
        }
    }

    private void ApplyFilter()
    {
        string query = _search.Text.Trim();
        _search.Visibility = _rows.Count >= SearchFrom || query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        int shown = 0;
        foreach (ReplacementRow row in _rows)
        {
            bool match = row.Matches(query);
            row.Root.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            shown += match ? 1 : 0;
        }

        _noMatches.Visibility = query.Length > 0 && shown == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- люди ----------------------------------------------------------------

    /// <summary>
    /// Знакомые имена: переименовать или убрать.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Раньше список можно было только пополнять. Имя с опечаткой жило в
    /// чипах, пока его не вытеснят шестьдесят других, — то есть годами.
    /// </para>
    /// <para>
    /// Строками, как замены, а не плиткой. В плитке крестик «удалить» стоял
    /// вплотную к полю, у поля в фокусе появлялся свой крестик «очистить»,
    /// а «голос запомнен» с кнопкой висел между людьми — и было непонятно,
    /// к кому из соседей он относится.
    /// </para>
    /// </remarks>
    private void FillPeople()
    {
        _people.Children.Clear();

        if (Settings.KnownParticipants.Count == 0)
        {
            _people.Children.Add(Ui.BodySecondary(L.S.DictionaryPeopleEmpty));
            return;
        }

        foreach (string name in Settings.KnownParticipants)
        {
            _people.Children.Add(PersonRow(name));
        }
    }

    private Grid PersonRow(string name)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, Tokens.Space2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new TextBox { Text = name, ClearButtonEnabled = false };
        Button remove = DeleteButton();

        // Переименование — везде: звонки, упоминания, книга голосов.
        // Отказался — имя в поле возвращается.
        box.LostFocus += (_, _) =>
        {
            string renamed = box.Text.Trim();
            if (renamed.Length == 0
                || renamed == name
                || !PersonRenamer.Run(Window.GetWindow(this), _settings, _voices, _callsDirectory(), name, renamed, _render))
            {
                box.Text = name;
            }
        };

        // Убрать человека — значит и забыть его голос: подсказывать имя,
        // которого больше нет в списке, было бы странно.
        remove.Click += (_, _) =>
        {
            _voices.Forget(name);
            _settings.Update(s => s with
            {
                KnownParticipants = [.. KnownParticipants.Remove(s.KnownParticipants, name)],
            });
        };

        row.Children.Add(box);
        if (_voices.PrintsOf(name) > 0)
        {
            UIElement voice = VoiceStatus(name);
            Grid.SetColumn(voice, 1);
            row.Children.Add(voice);
        }

        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);
        return row;
    }

    /// <summary>«Голос запомнен · Забыть голос» — справа от имени, в его строке.</summary>
    private StackPanel VoiceStatus(string name)
    {
        var icon = new SymbolIcon
        {
            Symbol = SymbolRegular.PersonVoice20,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, Tokens.Space2, 0),
        };
        icon.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

        TextBlock known = Ui.Caption(L.S.DictionaryVoiceKnown);
        known.VerticalAlignment = VerticalAlignment.Center;

        System.Windows.Controls.Button forget = Ui.Link(L.S.DictionaryForgetVoice);
        forget.VerticalAlignment = VerticalAlignment.Center;
        forget.Margin = new Thickness(Tokens.Space3, 0, 0, 0);
        forget.Click += (_, _) => _voices.Forget(name);

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(Tokens.Space4, 0, 0, 0),
            Children = { icon, known, forget },
        };
    }

    // --- перенос в файл -------------------------------------------------------

    /// <summary>
    /// Исправить словарём уже распознанные звонки — без повторного распознавания.
    /// </summary>
    /// <remarks>
    /// Сначала считаем, потом спрашиваем: «исправится 23 места в 5 звонках» —
    /// понятная цена, а молча переписанные звонки не понравились бы никому.
    /// </remarks>
    private void ApplyToPastCalls()
    {
        Dictionary<string, string> dictionary = Settings.Replacements;
        string root = _callsDirectory();
        if (dictionary.Count == 0 || !Directory.Exists(root))
        {
            ShowStatus(L.S.DictionaryApplyNothing);
            return;
        }

        var touched = new List<string>();
        int places = 0;
        foreach (string directory in Directory.GetDirectories(root))
        {
            if (CallTranscriptStore.Load(directory) is not { } transcript)
            {
                continue;
            }

            int replaced = TranscriptEdit.ApplyDictionary(transcript, dictionary).Replaced;
            if (replaced > 0)
            {
                touched.Add(directory);
                places += replaced;
            }
        }

        if (places == 0)
        {
            ShowStatus(L.S.DictionaryApplyNothing);
            return;
        }

        var ask = new ConfirmWindow(
            L.S.DictionaryApplyTitle,
            string.Format(L.S.Formatting, L.S.DictionaryApplyBody, places, touched.Count),
            primaryButton: L.S.DictionaryApplyGo,
            cancelButton: L.S.ButtonCancel,
            icon: SymbolRegular.TextEditStyle24);
        if (ConfirmWindow.Ask(Window.GetWindow(this), ask) != ConfirmChoice.Primary)
        {
            return;
        }

        foreach (string directory in touched)
        {
            if (CallTranscriptStore.Update(directory, t => TranscriptEdit.ApplyDictionary(t, dictionary).Transcript) is not null)
            {
                _render(directory);
            }
        }

        ShowStatus(string.Format(L.S.Formatting, L.S.DictionaryApplied, places, touched.Count));
    }

    private void ShowStatus(string text)
    {
        _status.Text = text;
        _status.Visibility = Visibility.Visible;
    }

    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = L.S.DictionaryFileName + ".json",
            DefaultExt = ".json",
            Filter = L.S.DictionaryFileFilter,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            DictionaryFile file = DictionaryTransfer.Export(Settings, _voices.Snapshot(), DateTimeOffset.Now);
            File.WriteAllText(dialog.FileName, DictionaryTransfer.Serialize(file));
            ShowStatus(string.Format(L.S.Formatting, L.S.DictionaryExported, dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Не удалось выгрузить словарь.", ex);
            ShowStatus(string.Format(L.S.Formatting, L.S.DictionaryFileFailed, ex.Message));
        }
    }

    private void Import()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = L.S.DictionaryFileFilter };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        DictionaryFile file;
        try
        {
            file = DictionaryTransfer.Parse(File.ReadAllText(dialog.FileName));
        }
        catch (InvalidDataException)
        {
            ShowStatus(L.S.DictionaryNotADictionary);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Не удалось прочитать файл словаря.", ex);
            ShowStatus(string.Format(L.S.Formatting, L.S.DictionaryFileFailed, ex.Message));
            return;
        }

        DictionaryMerge merge = DictionaryTransfer.Merge(Settings, file);
        _settings.Update(s => DictionaryTransfer.Merge(s, file).Settings);

        var report = new List<string>();
        int voices = 0;
        if (file.Voices.Count > 0)
        {
            if (DictionaryTransfer.VoicesFit(file, Settings))
            {
                voices = _voices.Import(file.Voices);
            }
            else
            {
                report.Add(Settings.RememberVoices ? L.S.DictionaryVoicesOtherModel : L.S.DictionaryVoicesOff);
            }
        }

        bool anything = merge.ReplacementsAdded + merge.ReplacementsChanged + merge.PeopleAdded + voices > 0 || merge.PromptTaken;
        if (anything)
        {
            report.Insert(0, string.Format(
                L.S.Formatting, L.S.DictionaryImported, merge.ReplacementsAdded, merge.ReplacementsChanged, merge.PeopleAdded));
            if (voices > 0)
            {
                report.Insert(1, string.Format(L.S.Formatting, L.S.DictionaryImportedVoices, voices));
            }

            if (merge.PromptTaken)
            {
                report.Add(L.S.DictionaryImportedPrompt);
            }
        }
        else
        {
            report.Insert(0, L.S.DictionaryImportedNothing);
        }

        // Фокус сейчас на кнопке этой страницы, и сама она по смене настроек
        // не перестроится — пересобираем явно.
        Build();
        ShowStatus(string.Join(' ', report));
    }
}
