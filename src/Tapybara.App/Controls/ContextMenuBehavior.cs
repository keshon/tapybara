using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using ContextMenu = System.Windows.Controls.ContextMenu;

namespace Tapybara.App.Controls;

/// <summary>
/// Открывает динамическое меню сразу, включая первый щелчок и клавиатуру.
/// </summary>
internal static class ContextMenuBehavior
{
    /// <summary>Заменяет штатное открытие: поздняя подстановка меню теряла первый щелчок.</summary>
    public static void Open(FrameworkElement anchor, ContextMenuEventArgs args, ContextMenu menu)
    {
        args.Handled = true;
        anchor.ContextMenu = menu;
        menu.PlacementTarget = anchor;
        menu.Placement = args.CursorLeft < 0 ? PlacementMode.Bottom : PlacementMode.MousePoint;
        // Внешний прозрачный запас для тени не должен сдвигать видимый край.
        menu.HorizontalOffset = -Tokens.MenuShadowInset;
        menu.VerticalOffset = -Tokens.MenuShadowInset + (args.CursorLeft < 0 ? Tokens.Space2 : 0);
        menu.IsOpen = true;
    }
}
