using System.Runtime.InteropServices;

namespace Tapybara.App;

/// <summary>Фокус клавиатуры окну Windows — там, где WPF сам его не отдаёт.</summary>
/// <remarks>
/// Всплывающее окно WPF (<see cref="System.Windows.Controls.Primitives.Popup"/>) —
/// отдельное окно Windows. Открытое из кода, оно получает логический фокус
/// WPF, но не фокус Windows: поле внутри выглядит активным, а нажатия уходят
/// в главное окно.
/// </remarks>
internal static partial class NativeFocus
{
    public static void Set(nint window) => SetFocus(window);

    [LibraryImport("user32.dll")]
    private static partial nint SetFocus(nint hWnd);
}
