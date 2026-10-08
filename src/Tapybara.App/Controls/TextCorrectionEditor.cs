using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tapybara.App.Localization;
using Wpf.Ui.Controls;

using CheckBox = System.Windows.Controls.CheckBox;
using Orientation = System.Windows.Controls.Orientation;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace Tapybara.App.Controls;

/// <summary>
/// Локальная правка текста с явным opt-in в массовую замену и словарь.
/// </summary>
/// <remarks>
/// Не знает о звонках и хранении. Хозяин выбирает область замены и выполняет
/// транзакцию; этот компонент пригоден и для истории диктовок.
/// </remarks>
internal sealed class TextCorrectionEditor : StackPanel
{
    private readonly UiTextBox _field;
    private readonly CheckBox _remember;
    private readonly List<(CheckBox Box, string Form, int Count)> _forms = [];
    private readonly string _original;

    /// <summary>Кандидаты раскрываются отдельно и никогда не выбираются автоматически.</summary>
    public TextCorrectionEditor(string original, IReadOnlyList<(string Form, int Count)> forms)
    {
        _original = original;
        Width = 360;
        _field = new UiTextBox { Text = original, ClearButtonEnabled = false };
        ControlSizing.Apply(_field);
        var label = Ui.BodySecondary(original);
        label.Margin = new Thickness(0, 0, 0, Tokens.Space2);
        Children.Add(label);
        Children.Add(_field);

        var candidates = new WrapPanel();
        foreach ((string form, int count) in forms)
        {
            var check = new CheckBox
            {
                Content = $"{form} ×{count}",
                IsChecked = false,
                Margin = new Thickness(0, 0, Tokens.Space3, 0),
            };
            _forms.Add((check, form, count));
            candidates.Children.Add(check);
        }

        if (_forms.Count > 0)
        {
            Children.Add(new Expander
            {
                Header = L.S.FixSimilar,
                IsExpanded = false,
                Content = candidates,
                Margin = new Thickness(0, Tokens.Space3, 0, Tokens.Space1),
            });
        }

        _remember = new CheckBox
        {
            Content = L.S.FixRemember,
            IsChecked = false,
            Margin = new Thickness(0, Tokens.Space2, 0, 0),
        };
        Children.Add(_remember);

        var here = new UiButton { Content = L.S.FixOnlyHere, Appearance = ControlAppearance.Primary };
        var all = new UiButton { Margin = new Thickness(Tokens.Space2, 0, 0, 0) };
        ControlSizing.Apply(here);
        ControlSizing.Apply(all);
        Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, Tokens.Space3, 0, 0),
            Children = { here, all },
        });

        void Update()
        {
            int count = _forms.Where(f => f.Box.IsChecked == true).Sum(f => f.Count);
            bool changed = Replacement.Length > 0 && Replacement != _original;
            here.IsEnabled = changed;
            all.Content = string.Format(L.S.Formatting, L.S.FixReplaceAll, count);
            all.IsEnabled = changed && count > 0;
        }

        _field.TextChanged += (_, _) => Update();
        foreach ((CheckBox box, _, _) in _forms)
        {
            box.Checked += (_, _) => Update();
            box.Unchecked += (_, _) => Update();
        }

        here.Click += (_, _) => ReplaceHereRequested?.Invoke();
        all.Click += (_, _) => ReplaceAllRequested?.Invoke();
        _field.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                args.Handled = true;
                if (here.IsEnabled)
                {
                    ReplaceHereRequested?.Invoke();
                }
            }
            else if (args.Key == Key.Escape)
            {
                args.Handled = true;
                DismissRequested?.Invoke();
            }
        };
        Update();
    }

    /// <summary>Новый текст; локальная замена никогда не учит словарь.</summary>
    public string Replacement => _field.Text.Trim();

    /// <summary>Только явно выбранные варианты для массовой операции.</summary>
    public IReadOnlyList<string> SelectedForms => [.. _forms.Where(f => f.Box.IsChecked == true).Select(f => f.Form)];

    /// <summary>Отдельное разрешение на обучение при массовой замене.</summary>
    public bool RememberInDictionary => _remember.IsChecked == true;

    /// <summary>Запросы без записи на диск: транзакцию выполняет хозяин.</summary>
    public event Action? ReplaceHereRequested;

    /// <summary>Массовая замена только по отдельной кнопке.</summary>
    public event Action? ReplaceAllRequested;

    /// <summary>Escape не сохраняет правку.</summary>
    public event Action? DismissRequested;

    /// <summary>Попап Windows получает фокус до передачи его этому полю.</summary>
    public void FocusText()
    {
        _field.Focus();
        Keyboard.Focus(_field);
        _field.SelectAll();
    }
}
