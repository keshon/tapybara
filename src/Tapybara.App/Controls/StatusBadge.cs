using System.Windows;
using System.Windows.Controls;

namespace Tapybara.App.Controls;

/// <summary>Цвета состояния остаются динамическими ресурсами, включая смену темы после загрузки списка.</summary>
public sealed class StatusBadge : Border
{
    private readonly TextBlock _label = new() { FontSize = Tokens.Caption };
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusBadge),
        new PropertyMetadata(string.Empty, (target, args) => ((StatusBadge)target)._label.Text = (string)args.NewValue));
    public static readonly DependencyProperty BackgroundKeyProperty = DependencyProperty.Register(nameof(BackgroundKey), typeof(string), typeof(StatusBadge),
        new PropertyMetadata("SubtleFillColorSecondaryBrush", (target, args) => ((StatusBadge)target).SetResourceReference(BackgroundProperty, args.NewValue)));
    public static readonly DependencyProperty ForegroundKeyProperty = DependencyProperty.Register(nameof(ForegroundKey), typeof(string), typeof(StatusBadge),
        new PropertyMetadata("TextFillColorSecondaryBrush", (target, args) => ((StatusBadge)target)._label.SetResourceReference(TextBlock.ForegroundProperty, args.NewValue)));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string BackgroundKey { get => (string)GetValue(BackgroundKeyProperty); set => SetValue(BackgroundKeyProperty, value); }
    public string ForegroundKey { get => (string)GetValue(ForegroundKeyProperty); set => SetValue(ForegroundKeyProperty, value); }

    public StatusBadge()
    {
        Child = _label;
        SetResourceReference(StyleProperty, "Badge");
        SetResourceReference(BackgroundProperty, BackgroundKey);
        _label.SetResourceReference(TextBlock.ForegroundProperty, ForegroundKey);
    }
}
