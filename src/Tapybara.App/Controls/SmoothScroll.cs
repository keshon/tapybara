using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Tapybara.App.Controls;

/// <summary>Плавный переход к элементу при пиксельной виртуализации; без смены выделения/фокуса.</summary>
public static class SmoothScroll
{
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached("Offset", typeof(double), typeof(SmoothScroll),
        new PropertyMetadata(0.0, (target, args) => ((ScrollViewer)target).ScrollToVerticalOffset((double)args.NewValue)));

    public static void Cancel(System.Windows.Controls.ListBox list)
    {
        if (ListViewport.Viewer(list) is not { } viewer) { return; }
        viewer.SetValue(OffsetProperty, viewer.VerticalOffset);
        viewer.BeginAnimation(OffsetProperty, null);
    }

    public static void Reveal(System.Windows.Controls.ListBox list, object item)
    {
        if (ListViewport.Viewer(list) is not { } viewer) { list.ScrollIntoView(item); return; }
        Cancel(list);
        double origin = viewer.VerticalOffset;
        FrameworkElement? container = list.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
        if (container is null)
        {
            list.ScrollIntoView(item); list.UpdateLayout();
            container = list.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
        }
        if (container is null) { return; }
        double top = container.TranslatePoint(new System.Windows.Point(), viewer).Y;
        if (viewer.VerticalOffset == origin && top >= 0 && top + container.ActualHeight <= viewer.ViewportHeight) { return; }
        double target = Math.Clamp(viewer.VerticalOffset + top - viewer.ViewportHeight * .35, 0, viewer.ScrollableHeight);
        if (!SystemParameters.ClientAreaAnimation) { viewer.ScrollToVerticalOffset(target); return; }
        viewer.SetValue(OffsetProperty, origin);
        var animation = new DoubleAnimation(origin, target, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.HoldEnd };
        viewer.BeginAnimation(OffsetProperty, animation);
    }
}
