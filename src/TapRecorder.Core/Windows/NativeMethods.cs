using System.Runtime.InteropServices;

namespace TapRecorder.Core.Windows;

/// <summary>
/// Объявления функций Win32, которых нет в .NET.
/// </summary>
/// <remarks>
/// Используется <c>LibraryImport</c>, а не привычный по примерам из интернета
/// <c>DllImport</c>: генератор исходников пишет маршалинг на этапе компиляции,
/// поэтому нет накладных расходов в рантайме и всё работает при публикации в
/// native AOT. Требует, чтобы класс и методы были <c>partial</c>.
/// </remarks>
internal static partial class NativeMethods
{
    // --- сообщения ---------------------------------------------------------

    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_QUIT = 0x0012;

    // --- модификаторы для RegisterHotKey -----------------------------------

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    /// <summary>Не слать повторные WM_HOTKEY, пока клавишу держат нажатой.</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    // --- виртуальные коды клавиш -------------------------------------------

    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12;      // Alt
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_RWIN = 0x5C;
    public const ushort VK_V = 0x56;

    // --- SendInput ---------------------------------------------------------

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    // --- стили окна --------------------------------------------------------

    public const int GWL_EXSTYLE = -20;

    /// <summary>Окно не получает фокус при показе и по клику.</summary>
    public const int WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>Окна нет в Alt+Tab и на панели задач.</summary>
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    // --- буфер обмена ------------------------------------------------------

    public const uint CF_UNICODETEXT = 13;
    public const uint GMEM_MOVEABLE = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint HWnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    /// <summary>
    /// Объединение из трёх видов ввода. В Win32 это <c>union</c>, поэтому все
    /// поля лежат по нулевому смещению; размер задаёт самое большое из них
    /// (мышиный ввод), и его нельзя урезать — SendInput проверяет размер.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;

        /// <summary>Резерв под MOUSEINPUT: он длиннее клавиатурного.</summary>
        [FieldOffset(0)]
        private readonly MouseInputPadding _padding;

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct MouseInputPadding
        {
            private readonly int _dx;
            private readonly int _dy;
            private readonly uint _mouseData;
            private readonly uint _flags;
            private readonly uint _time;
            private readonly nuint _extraInfo;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;
        public INPUTUNION Union;
    }

    // --- горячие клавиши ---------------------------------------------------

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hWnd, int id);

    /// <summary>Блокирующее ожидание сообщения. Возвращает 0 на WM_QUIT.</summary>
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    public static partial int GetMessage(out MSG message, nint hWnd, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    // --- ввод --------------------------------------------------------------

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SendInput(uint count, [In] INPUT[] inputs, int size);

    /// <summary>Состояние клавиши прямо сейчас: старший бит — «нажата».</summary>
    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int virtualKey);

    // --- стили окна --------------------------------------------------------

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static partial nint GetWindowLongPtr(nint hWnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static partial nint SetWindowLongPtr(nint hWnd, int index, nint newLong);

    // --- буфер обмена ------------------------------------------------------

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint SetClipboardData(uint format, nint data);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW",
                   SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterClipboardFormat(string formatName);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GlobalFree(nint memory);
}
