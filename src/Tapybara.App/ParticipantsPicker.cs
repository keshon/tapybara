using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;
// Проект включает и WPF, и WinForms (иконка в трее — форменная), поэтому
// добрая половина имён неоднозначна. Псевдонимы, а не полные имена в коде:
// так видно один раз наверху, из какого мира каждый тип.
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;
using UserControl = System.Windows.Controls.UserControl;

namespace Tapybara.App;

/// <summary>
/// Кто был на звонке: чипы знакомых имён плюс поле для нового.
/// </summary>
/// <remarks>
/// Имена спрашиваются, а не угадываются. Разделить голоса между собой машина
/// когда-нибудь сможет, а узнать, что вот этот голос зовут Кириллом, — нет:
/// это знание есть только у человека, и он его как раз только что применял,
/// разговаривая. Спросить в этот момент дешевле всего.
/// <para>
/// Управление вынесено в отдельный элемент, потому что участников правят в
/// двух местах: сразу после звонка и позже, из списка записей.
/// </para>
/// </remarks>
public sealed class ParticipantsPicker : UserControl
{
    /// <summary>
    /// Сколько знакомых имён показываем, пока в поле ничего не набрано.
    /// </summary>
    /// <remarks>
    /// Восемнадцать — примерно три ряда. Дальше список перестаёт быть
    /// «взглядом узнаю нужное» и становится чтением, а для этого есть фильтр.
    /// </remarks>
    private const int VisibleWithoutFilter = 18;

    private readonly WrapPanel _chips = new();
    private readonly UiTextBox _entry;

    private IReadOnlyList<string> _known = [];
    private IReadOnlyList<string> _selected = [];
    private string _myName = string.Empty;

    public ParticipantsPicker()
    {
        // Полное имя типа обязательно: внутри наследника FrameworkElement
        // «VerticalAlignment» — это ЕГО собственное свойство, а не перечисление,
        // и псевдонимы using тут не спасают: поиск члена идёт раньше них.
        _entry = new UiTextBox
        {
            Width = 132,
            Margin = new Thickness(0, 0, 6, 6),
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
        };

        _entry.KeyDown += OnEntryKeyDown;
        _entry.TextChanged += (_, _) => Rebuild();

        Content = _chips;
    }

    /// <summary>Кого выбрали. Порядок — как выбирали.</summary>
    public IReadOnlyList<string> Selected => _selected;

    /// <summary>Список изменился: выбрали, сняли или ввели новое имя.</summary>
    public event Action? SelectionChanged;

    /// <summary>Заполнить: своё имя, знакомые имена, уже выбранные.</summary>
    public void Load(string myName, IReadOnlyList<string> known, IReadOnlyList<string> selected)
    {
        _myName = myName;
        _known = known;
        _selected = selected;
        _entry.Text = string.Empty;
        Rebuild();
    }

    /// <summary>Обновить подсказку в поле ввода после смены языка.</summary>
    public void ApplyLanguage() => Rebuild();

    /// <summary>
    /// Досчитать имя, оставленное в поле без Enter.
    /// </summary>
    /// <remarks>
    /// Вызывается перед сохранением. Человек набрал имя и нажал «Сохранить»,
    /// не подтвердив ввод, — это не отказ от имени, это обычный порядок
    /// действий. Молча его потерять значило бы наказать за то, что он не
    /// угадал наш ритуал.
    /// </remarks>
    public void Flush()
    {
        string? name = KnownParticipants.Normalize(_known, _entry.Text);
        if (name is null || string.Equals(name, _myName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _entry.Text = string.Empty;
        _selected = KnownParticipants.Add(_selected, name);
        Rebuild();
        SelectionChanged?.Invoke();
    }

    private void OnEntryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true; // иначе Enter уйдёт кнопке по умолчанию и закроет окно
        Flush();
        _entry.Focus();
    }

    /// <summary>
    /// Какие чипы показать: выбранные плюс подходящие под фильтр знакомые.
    /// </summary>
    /// <remarks>
    /// Выбранные показываем ВСЕГДА, даже если они не проходят фильтр. Иначе
    /// набранные в поле буквы прятали бы уже выбранное имя, и снять его стало
    /// бы невозможно, пока не очистишь фильтр.
    /// </remarks>
    private List<string> ChipNames()
    {
        string filter = _entry.Text.Trim();
        List<string> names = [.. _selected];

        IEnumerable<string> pool = filter.Length == 0
            ? _known
            : _known.Where(n => n.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

        foreach (string name in pool)
        {
            if (names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (filter.Length == 0 && names.Count >= VisibleWithoutFilter)
            {
                break;
            }

            names.Add(name);
        }

        return names;
    }

    private void Rebuild()
    {
        _chips.Children.Clear();
        _chips.Children.Add(BuildMeLabel(_myName));

        foreach (string name in ChipNames())
        {
            _chips.Children.Add(BuildChip(
                name,
                selected: _selected.Contains(name, StringComparer.OrdinalIgnoreCase)));
        }

        _entry.PlaceholderText = L.S.ParticipantsEntryPlaceholder;
        _entry.ToolTip = L.S.ParticipantsEntryHint;
        _chips.Children.Add(_entry);
    }

    /// <summary>
    /// Владелец микрофона — подпись, а не чип.
    /// </summary>
    /// <remarks>
    /// Здесь был выключенный чип, отмеченный и полупрозрачный. Выглядел он
    /// как сломанная кнопка, на которую почему-то не нажать. По смыслу это
    /// не выбор, а факт: вы на звонке всегда, это ваша дорожка, — и факт
    /// показываем текстом.
    /// </remarks>
    private static TextBlock BuildMeLabel(string myName)
    {
        TextBlock label = Ui.BodySecondary(myName);
        label.Margin = new Thickness(2, 0, Tokens.Space3, 6);
        label.VerticalAlignment = System.Windows.VerticalAlignment.Center;
        label.ToolTip = L.S.ParticipantsMeHint;
        return label;
    }

    private ToggleButton BuildChip(string name, bool selected)
    {
        var chip = new ToggleButton
        {
            Content = name,
            IsChecked = selected,
            Style = Ui.ChipStyle(),
            ToolTip = selected ? L.S.ParticipantsRemoveHint : L.S.ParticipantsAddHint,
        };

        chip.Click += (_, _) =>
        {
            _selected = selected
                ? KnownParticipants.Remove(_selected, name)
                : KnownParticipants.Add(_selected, name);

            Rebuild();
            SelectionChanged?.Invoke();
        };

        return chip;
    }
}
