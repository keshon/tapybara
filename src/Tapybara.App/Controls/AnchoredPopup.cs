using System.Windows;
using System.Windows.Controls.Primitives;
using Point = System.Windows.Point;

namespace Tapybara.App.Controls;

/// <summary>Единый зазор у вызывающего элемента; верх/низ выбирается до открытия.</summary>
public static class AnchoredPopup
{
    public static void Open(Popup popup, FrameworkElement anchor, bool above = false, Rect? rectangle = null)
    {
        popup.PlacementTarget = anchor;
        popup.Placement = PlacementMode.Custom;
        if (rectangle is { } rect) { popup.PlacementRectangle = rect; }
        popup.PopupAnimation = PopupAnimation.None;
        popup.CustomPopupPlacementCallback = (size, target, _) =>
        {
            var top = new CustomPopupPlacement(new Point(0, -size.Height - Tokens.Space2), PopupPrimaryAxis.Horizontal);
            var bottom = new CustomPopupPlacement(new Point(0, target.Height + Tokens.Space2), PopupPrimaryAxis.Horizontal);
            return above ? [top, bottom] : [bottom, top];
        };
        popup.IsOpen = true;
    }
}
