using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace Tapybara.App.Controls;

/// <summary>Отдельное окно popup всегда имеет непрозрачную поверхность, даже при Mica у владельца.</summary>
public sealed class PopoverSurface : Border
{
    static PopoverSurface() => BackgroundProperty.OverrideMetadata(typeof(PopoverSurface),
        new FrameworkPropertyMetadata(SystemColors.WindowBrush, FrameworkPropertyMetadataOptions.None, null, Opaque));

    private static SolidColorBrush Opaque(DependencyObject target, object value)
    {
        if (value is not SolidColorBrush brush) { return SystemColors.WindowBrush; }
        if (brush.Color.A == 255 && brush.Opacity == 1) { return brush; }
        var opaque = new SolidColorBrush(Color.FromRgb(brush.Color.R, brush.Color.G, brush.Color.B));
        opaque.Freeze();
        return opaque;
    }

    public PopoverSurface()
    {
        SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
        Padding = new Thickness(Tokens.Space3);
        CornerRadius = Tokens.CardRadius;
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        SetResourceReference(BorderBrushProperty, "CardStrokeColorDefaultBrush");
    }
}
