using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Point = System.Windows.Point;

namespace Tapybara.App.Controls;

/// <summary>Размер до открытия и позиция у кнопки; нет позднего сдвига из-за анимации меню.</summary>
public static class AnchoredMenu
{
    public static void Open(ContextMenu menu, FrameworkElement anchor)
    {
        menu.Resources[SystemParameters.MenuPopupAnimationKey] = PopupAnimation.None;
        menu.PlacementTarget = anchor;
        // Выбираем сторону один раз по окончательному размеру. Custom callback
        // повторно выбирал кандидата после раскладки Popup и переносил меню наверх.
        menu.FontFamily = System.Windows.Documents.TextElement.GetFontFamily(anchor);
        menu.FontSize = System.Windows.Documents.TextElement.GetFontSize(anchor);
        Point bottom = anchor.PointToScreen(new Point(0, anchor.ActualHeight));
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)bottom.X, (int)bottom.Y));
        var transform = PresentationSource.FromVisual(anchor)?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
        Point origin = anchor.PointToScreen(new Point());
        var available = new Rect((screen.WorkingArea.Left - origin.X) / transform.M11,
            (screen.WorkingArea.Top - origin.Y) / transform.M22,
            screen.WorkingArea.Width / transform.M11, screen.WorkingArea.Height / transform.M22);
        menu.MaxHeight = available.Height;
        menu.MaxWidth = available.Width;
        menu.ApplyTemplate();
        menu.Measure(available.Size);
        menu.Placement = PlacementMode.RelativePoint;
        menu.CustomPopupPlacementCallback = null;
        double x = anchor.ActualWidth - menu.DesiredSize.Width + Tokens.MenuShadowInset;
        double y = anchor.ActualHeight + Tokens.Space2 - Tokens.MenuShadowInset;
        y = y + menu.DesiredSize.Height <= available.Bottom ? y
            : -menu.DesiredSize.Height + Tokens.MenuShadowInset - Tokens.Space2;
        menu.HorizontalOffset = Math.Clamp(x, available.Left, Math.Max(available.Left, available.Right - menu.DesiredSize.Width));
        menu.VerticalOffset = Math.Clamp(y, available.Top, Math.Max(available.Top, available.Bottom - menu.DesiredSize.Height));
        menu.IsOpen = true;
    }
}
