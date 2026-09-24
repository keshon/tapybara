using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
using Tapybara.Core.Settings;
using Wpf.Ui.Controls;

using Brush = System.Windows.Media.Brush;
using Button = Wpf.Ui.Controls.Button;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
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
/// Собирается кодом, как карточки настроек: строки замен и имён — это
/// пары полей, которые добавляются и удаляются, и шаблон с привязками
/// только спрятал бы простую логику.
/// </para>
/// </remarks>
public sealed class DictionaryPage : System.Windows.Controls.UserControl, IDisposable
{
    private readonly SettingsHost _settings;
    private readonly VoiceBook _voices;
    private readonly StackPanel _root = new() { Margin = new Thickness(4, 0, 0, 0), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _replacements = new();
    private readonly StackPanel _people = new();
    private readonly TextBox _prompt;

    public DictionaryPage(SettingsHost settings, VoiceBook voices)
    {
        _settings = settings;
        _voices = voices;
        _voices.Changed += OnVoicesChanged;

        _prompt = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _prompt.LostFocus += (_, _) =>
        {
            string value = _prompt.Text.Trim();
            _settings.Update(s => s with { Prompt = value.Length == 0 ? null : value });
        };

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

        TextBlock title = Ui.Title(L.S.NavDictionary);
        title.Margin = new Thickness(0, 0, 0, Tokens.Space2);
        _root.Children.Add(title);

        TextBlock intro = Ui.BodySecondary(L.S.DictionaryIntro);
        intro.Margin = new Thickness(0, 0, 0, Tokens.Space2);
        _root.Children.Add(intro);

        // --- замены
        _root.Children.Add(Group(L.S.GroupReplacements, L.S.DictionaryReplacementsHint));
        FillReplacements();
        var add = new Button
        {
            Content = L.S.ReplacementTitle,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Add24 },
            Margin = new Thickness(0, _replacements.Children.Count > 0 ? Tokens.Space1 : 0, 0, 0),
        };
        add.Click += (_, _) =>
        {
            TextBox heard = AddReplacementRow(string.Empty, string.Empty);
            heard.Focus();
        };

        _root.Children.Add(Card(new StackPanel { Children = { _replacements, add } }));

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

    // --- замены --------------------------------------------------------------

    private void FillReplacements()
    {
        _replacements.Children.Clear();
        foreach (KeyValuePair<string, string> pair in Settings.Replacements.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            AddReplacementRow(pair.Key, pair.Value);
        }
    }

    /// <summary>Строка «услышано → правильно» с кнопкой удаления.</summary>
    /// <returns>Поле «услышано» — чтобы поставить в него курсор.</returns>
    private TextBox AddReplacementRow(string heard, string correct)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, Tokens.Space2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var from = new TextBox { Text = heard, PlaceholderText = L.S.DictionaryHeardPlaceholder };
        var to = new TextBox { Text = correct, PlaceholderText = L.S.DictionaryCorrectPlaceholder };
        var arrow = new SymbolIcon
        {
            Symbol = SymbolRegular.ArrowRight16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var remove = new Button
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Delete24 },
            Appearance = ControlAppearance.Transparent,
            Margin = new Thickness(Tokens.Space2, 0, 0, 0),
            ToolTip = L.S.ButtonDelete,
        };

        arrow.SetResourceReference(ForegroundProperty, "TextFillColorTertiaryBrush");
        Grid.SetColumn(arrow, 1);
        Grid.SetColumn(to, 2);
        Grid.SetColumn(remove, 3);
        row.Children.Add(from);
        row.Children.Add(arrow);
        row.Children.Add(to);
        row.Children.Add(remove);

        from.LostFocus += (_, _) => SaveReplacements();
        to.LostFocus += (_, _) => SaveReplacements();
        remove.Click += (_, _) =>
        {
            _replacements.Children.Remove(row);
            SaveReplacements();
        };

        _replacements.Children.Add(row);
        return from;
    }

    /// <summary>
    /// Собрать замены из строк и записать.
    /// </summary>
    /// <remarks>
    /// Пустое «услышано» пропускаем, а не удаляем строку: человек мог
    /// заполнить правую половину первой. Строка исчезнет при следующей
    /// сборке страницы, если так и останется пустой.
    /// </remarks>
    private void SaveReplacements()
    {
        var map = new Dictionary<string, string>();
        foreach (Grid row in _replacements.Children.OfType<Grid>())
        {
            string heard = ((TextBox)row.Children[0]).Text.Trim();
            string correct = ((TextBox)row.Children[2]).Text.Trim();
            if (heard.Length > 0 && correct.Length > 0)
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

    // --- люди ----------------------------------------------------------------

    /// <summary>
    /// Знакомые имена: переименовать или убрать.
    /// </summary>
    /// <remarks>
    /// Раньше список можно было только пополнять. Имя с опечаткой жило в
    /// чипах, пока его не вытеснят шестьдесят других, — то есть годами.
    /// </remarks>
    private void FillPeople()
    {
        _people.Children.Clear();

        if (Settings.KnownParticipants.Count == 0)
        {
            _people.Children.Add(Ui.BodySecondary(L.S.DictionaryPeopleEmpty));
            return;
        }

        var wrap = new WrapPanel();
        foreach (string name in Settings.KnownParticipants)
        {
            wrap.Children.Add(PersonRow(name));
        }

        _people.Children.Add(wrap);
    }

    private StackPanel PersonRow(string name)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, Tokens.Space4, Tokens.Space2) };
        var box = new TextBox { Text = name, Width = 170 };
        var remove = new Button
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Dismiss16 },
            Appearance = ControlAppearance.Transparent,
            Padding = new Thickness(6),
            ToolTip = L.S.ButtonDelete,
        };

        box.LostFocus += (_, _) =>
        {
            string? renamed = box.Text.Trim() is { Length: > 0 } t ? t : null;
            if (renamed is null || renamed == name)
            {
                box.Text = name;
                return;
            }

            _settings.Update(s => s with
            {
                KnownParticipants = [.. s.KnownParticipants.Select(n => n == name ? renamed : n)
                    .Distinct(StringComparer.OrdinalIgnoreCase)],
            });

            // Голос переезжает вместе с именем: иначе переименованный человек
            // перестал бы узнаваться, а старое имя всплывало бы в подсказках.
            _voices.Rename(name, renamed);
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
        row.Children.Add(remove);

        if (_voices.PrintsOf(name) > 0)
        {
            TextBlock known = Ui.Caption(L.S.DictionaryVoiceKnown);
            known.VerticalAlignment = VerticalAlignment.Center;
            known.Margin = new Thickness(Tokens.Space1, 0, Tokens.Space1, 0);

            var forget = new Button
            {
                Icon = new SymbolIcon { Symbol = SymbolRegular.PersonVoice24 },
                Appearance = ControlAppearance.Transparent,
                Padding = new Thickness(6),
                ToolTip = L.S.DictionaryForgetVoice,
            };
            forget.Click += (_, _) => _voices.Forget(name);

            row.Children.Add(known);
            row.Children.Add(forget);
        }

        return row;
    }
}
