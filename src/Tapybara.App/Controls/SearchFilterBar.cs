using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Tapybara.App.Localization;
using Wpf.Ui.Controls;

namespace Tapybara.App.Controls;

/// <summary>Поиск, раскрываемые фильтры и сохраняющиеся чипы; содержимое фильтров задаёт экран.</summary>
public sealed class SearchFilterBar : StackPanel
{
    public Wpf.Ui.Controls.TextBox Search { get; } = new() { Icon = new SymbolIcon { Symbol = SymbolRegular.Search24 }, ClearButtonEnabled = true };
    public ContentControl Filters { get; } = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, Tokens.Space2, 0, 0) };
    private readonly Wpf.Ui.Controls.Button _toggle = new() { Icon = new SymbolIcon { Symbol = SymbolRegular.Filter24, FontSize = 16 }, Margin = new Thickness(Tokens.Space2, 0, 0, 0) };
    private readonly WrapPanel _chips = new() { Margin = new Thickness(0, Tokens.Space2, 0, 0) };
    private int _count;

    public SearchFilterBar()
    {
        ControlSizing.Apply(Search);
        ControlSizing.Apply(_toggle, iconOnly: true);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(Search);
        Grid.SetColumn(_toggle, 1);
        row.Children.Add(_toggle);
        Children.Add(row);
        Children.Add(Filters);
        Children.Add(_chips);
        _toggle.Click += (_, _) => Filters.Visibility = Filters.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        ApplyLanguage();
    }

    public void SetActiveFilters(IReadOnlyList<(string Text, Action Remove)> filters)
    {
        _chips.Children.Clear();
        _count = filters.Count;
        foreach (var filter in filters)
        {
            var chip = new Wpf.Ui.Controls.Button { Content = filter.Text + " ×", Margin = new Thickness(0, 0, Tokens.Space1, Tokens.Space1) };
            AutomationProperties.SetName(chip, L.S.LibraryRemoveFilter + ": " + filter.Text);
            chip.Click += (_, _) => filter.Remove();
            _chips.Children.Add(chip);
        }
        _chips.Visibility = _count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ApplyLanguage();
    }

    public void ApplyLanguage()
    {
        _toggle.Content = null;
        _toggle.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, _count > 0 ? "AccentTextFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
        _toggle.ToolTip = L.S.LibraryFilters;
        AutomationProperties.SetName(_toggle, L.S.LibraryFilters + (_count > 0 ? $" ({_count})" : string.Empty));
    }
}
