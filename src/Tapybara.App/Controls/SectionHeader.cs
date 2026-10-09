using System.Windows;
using System.Windows.Controls;

namespace Tapybara.App.Controls;

/// <summary>Общий заголовок раздела: название и основное действие на одной строке.</summary>
public sealed class SectionHeader : Grid
{
    private readonly ContentControl _title = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _action = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    public UIElement? TitleContent { get => _title.Content as UIElement; set => _title.Content = value; }
    public UIElement? ActionContent { get => _action.Content as UIElement; set => _action.Content = value; }

    public SectionHeader()
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Children.Add(_title);
        SetColumn(_action, 1);
        Children.Add(_action);
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size constraint)
    {
        if (_action.Content is ContentControl { Content: RecordPill pill })
        {
            _title.Measure(new System.Windows.Size(double.PositiveInfinity, constraint.Height));
            _action.Measure(new System.Windows.Size(double.PositiveInfinity, constraint.Height));
            double actionWidth = _action.DesiredSize.Width + (pill.ShortcutShown ? 0 : pill.ShortcutWidth);
            pill.SetShortcutVisible(_title.DesiredSize.Width + actionWidth <= constraint.Width);
        }
        return base.MeasureOverride(constraint);
    }
}
