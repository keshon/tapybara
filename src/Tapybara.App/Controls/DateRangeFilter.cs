using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Tapybara.App.Controls;

/// <summary>Две равные колонки диапазона дат; сохраняет поля при смене языка.</summary>
public sealed class DateRangeFilter : Grid
{
    public DatePicker From { get; } = new();
    public DatePicker To { get; } = new();
    private readonly TextBlock _fromLabel = new();
    private readonly TextBlock _toLabel = new();

    public DateRangeFilter()
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Tokens.Space2) });
        ColumnDefinitions.Add(new ColumnDefinition());
        Add(_fromLabel, From, 0);
        Add(_toLabel, To, 2);
    }
    private void Add(TextBlock label, DatePicker field, int column)
    {
        label.SetResourceReference(StyleProperty, "TextCaption");
        label.Margin = new Thickness(0, 0, 0, Tokens.Space1);
        ControlSizing.Apply(field);
        field.SelectedDateFormat = DatePickerFormat.Short;
        var panel = new StackPanel { Children = { label, field } };
        SetColumn(panel, column);
        Children.Add(panel);
    }
    public void SetLabels(string from, string to)
    {
        _fromLabel.Text = from;
        _toLabel.Text = to;
        AutomationProperties.SetName(From, from);
        AutomationProperties.SetName(To, to);
    }
}
