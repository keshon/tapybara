using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ListBox = System.Windows.Controls.ListBox;
using Point = System.Windows.Point;

namespace Tapybara.App.Controls;

/// <summary>Pixel positioning for virtualized lists, anchored to content rather than estimated item heights.</summary>
public static class ListViewport
{
    public sealed record Anchor(object Item, double Top);

    internal static ScrollViewer? Viewer(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) { return viewer; }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            if (Viewer(VisualTreeHelper.GetChild(parent, i)) is { } child) { return child; }
        }
        return null;
    }

    public static Anchor? Capture(ListBox list, Func<FrameworkElement, FrameworkElement?>? content = null, object? preferredItem = null)
    {
        SmoothScroll.Cancel(list);
        if (Viewer(list) is not { } viewer) { return null; }
        // Only realized containers: a long transcript must not be laid out just to save its position.
        return Containers(list).Select(container =>
        {
            FrameworkElement element = content?.Invoke(container) ?? container;
            double top = element.TranslatePoint(new Point(), viewer).Y;
            return (Container: container, Element: element, Top: top);
        }).Where(row => row.Top + row.Element.ActualHeight > 0 && row.Top < viewer.ViewportHeight)
            .Select(row => new Anchor(list.ItemContainerGenerator.ItemFromContainer(row.Container), row.Top))
            .OrderBy(row => Equals(row.Item, preferredItem) ? 0 : 1).ThenBy(row => row.Top).FirstOrDefault();
    }

    public static void Position(ListBox list, object item, double top = 0,
        Func<FrameworkElement, FrameworkElement?>? content = null)
    {
        SmoothScroll.Cancel(list);
        list.ScrollIntoView(item);
        list.UpdateLayout();
        if (Viewer(list) is not { } viewer) { return; }
        // Realizing variable-height rows changes the extent estimate. Resolve the resulting
        // layout before correcting the anchor again; no delayed callbacks can fight user scrolling.
        for (int pass = 0; pass < 3; pass++)
        {
            if (list.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container) { return; }
            FrameworkElement element = content?.Invoke(container) ?? container;
            double delta = element.TranslatePoint(new Point(), viewer).Y - top;
            double target = Math.Clamp(viewer.VerticalOffset + delta, 0, viewer.ScrollableHeight);
            if (Math.Abs(target - viewer.VerticalOffset) < .5) { return; }
            viewer.ScrollToVerticalOffset(target);
            list.UpdateLayout();
        }
    }

    private static IEnumerable<FrameworkElement> Containers(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is ListBoxItem item) { yield return item; }
            else
            {
                foreach (FrameworkElement container in Containers(child)) { yield return container; }
            }
        }
    }
}
