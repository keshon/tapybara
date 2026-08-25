namespace TapRecorder.Core.Windows;

/// <summary>Правка стилей окна, недоступная из WPF.</summary>
public static class WindowChrome
{
    /// <summary>
    /// Сделать окно «не забирающим фокус» и невидимым для Alt+Tab.
    /// </summary>
    /// <param name="windowHandle">HWND окна; в WPF берётся через <c>WindowInteropHelper</c>.</param>
    /// <remarks>
    /// Для оверлея диктовки это критично. Свойства WPF <c>ShowActivated="False"</c>
    /// недостаточно: без <c>WS_EX_NOACTIVATE</c> окно всё равно перехватывает
    /// фокус при клике по нему. А фокус здесь — это всё: текст вставляется в то
    /// окно, которое активно, и увод фокуса на оверлей означает, что диктовка
    /// вставится в пустоту вместо приложения пользователя.
    /// </remarks>
    public static void MakeNonActivating(nint windowHandle)
    {
        nint style = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE);
        nint updated = style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GWL_EXSTYLE, updated);
    }
}
