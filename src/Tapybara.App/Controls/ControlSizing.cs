using System.Windows;
using System.Windows.Controls;

namespace Tapybara.App.Controls;

/// <summary>Единая плотность команд; подписи не влияют на высоту ряда.</summary>
public static class ControlSizing
{
    public static readonly DependencyProperty CompactProperty = DependencyProperty.RegisterAttached("Compact", typeof(bool), typeof(ControlSizing),
        new PropertyMetadata(false, (target, args) => { if (args.NewValue is true && target is System.Windows.Controls.Control control) { Apply(control); } }));
    public static bool GetCompact(DependencyObject target) => (bool)target.GetValue(CompactProperty);
    public static void SetCompact(DependencyObject target, bool value) => target.SetValue(CompactProperty, value);
    public static readonly DependencyProperty IconOnlyProperty = DependencyProperty.RegisterAttached("IconOnly", typeof(bool), typeof(ControlSizing),
        new PropertyMetadata(false, (target, args) => { if (args.NewValue is true && target is System.Windows.Controls.Control control) { Apply(control, true); } }));
    public static bool GetIconOnly(DependencyObject target) => (bool)target.GetValue(IconOnlyProperty);
    public static void SetIconOnly(DependencyObject target, bool value) => target.SetValue(IconOnlyProperty, value);

    public static void Apply(System.Windows.Controls.Control control, bool iconOnly = false)
    {
        control.Height = Tokens.ControlHeight;
        control.MinHeight = Tokens.ControlHeight;
        control.VerticalAlignment = VerticalAlignment.Center;
        control.VerticalContentAlignment = VerticalAlignment.Center;
        // Штатные поля рассчитаны на более высокий ряд: их вертикальные
        // отступы обрезают строку при компактной высоте, особенно Segoe Variable.
        if (control is System.Windows.Controls.TextBox or System.Windows.Controls.ComboBox or DatePicker)
        {
            control.Padding = new Thickness(10, 4, 10, 4);
        }
        if (iconOnly)
        {
            control.Width = Tokens.ControlHeight;
            control.MinWidth = Tokens.ControlHeight;
            control.Padding = new Thickness(0);
            control.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
        }
    }
}
