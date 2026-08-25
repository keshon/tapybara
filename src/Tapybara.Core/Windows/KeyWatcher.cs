using System.ComponentModel;
using System.Runtime.InteropServices;
using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Windows;

/// <summary>
/// Наблюдение за клавишей, НЕ отнимая её у системы.
/// </summary>
/// <remarks>
/// <para>
/// Нужно для отмены диктовки по Escape. Очевидное решение —
/// <c>RegisterHotKey</c> — здесь категорически не годится: занятое сочетание
/// перестаёт доходить до активного окна. Пока шла диктовка, Escape не работал
/// НИГДЕ: не закрывались диалоги, не сворачивались меню, не отменялось
/// автодополнение. Диктовка длится минуты, и всё это время система была
/// наполовину сломана.
/// </para>
/// <para>
/// Низкоуровневый хук видит нажатие раньше окна, но, передав его дальше через
/// <c>CallNextHookEx</c>, ничего не отнимает: приложение получает свой Escape
/// как обычно, а мы просто узнаём о нём.
/// </para>
/// <para>
/// Хук ставится на поток с циклом сообщений — в приложении это поток
/// интерфейса. Обработчик обязан возвращаться немедленно: Windows снимает
/// хуки, которые думают дольше <c>LowLevelHooksTimeout</c>.
/// </para>
/// </remarks>
public sealed class KeyWatcher : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int HC_ACTION = 0;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_SYSKEYDOWN = 0x0104;

    private readonly ushort _virtualKey;
    private readonly HookProc _callback;

    private nint _hook;
    private bool _disposed;

    /// <param name="virtualKey">Виртуальный код клавиши, за которой следим.</param>
    public KeyWatcher(ushort virtualKey)
    {
        _virtualKey = virtualKey;

        // Делегат в поле, а не временный: сборщик мусора не знает про ссылку
        // из неуправляемого кода и собрал бы его, а следующее нажатие клавиши
        // ушло бы в освобождённую память.
        _callback = OnKey;
    }

    /// <summary>Клавиша нажата. Вызывается в потоке, поставившем хук.</summary>
    public event Action? Pressed;

    /// <summary>Поставить хук. Ставить нужно из потока с циклом сообщений.</summary>
    /// <exception cref="Win32Exception">Хук не удалось поставить.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hook != nint.Zero)
        {
            return;
        }

        // Низкоуровневому клавиатурному хуку модуль не нужен: обработчик живёт
        // в этом же процессе. Передаём ноль вместо дескриптора модуля.
        _hook = SetWindowsHookExW(WH_KEYBOARD_LL, _callback, nint.Zero, 0);
        if (_hook == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось поставить наблюдение за клавишей.");
        }
    }

    /// <summary>Снять хук.</summary>
    public void Stop()
    {
        if (_hook == nint.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hook);
        _hook = nint.Zero;
    }

    private nint OnKey(int code, nuint wParam, nint lParam)
    {
        if (code == HC_ACTION && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            try
            {
                // Первое поле KBDLLHOOKSTRUCT — виртуальный код.
                int virtualKey = Marshal.ReadInt32(lParam);
                if (virtualKey == _virtualKey)
                {
                    Pressed?.Invoke();
                }
            }
            catch (Exception ex)
            {
                // Исключение отсюда уходит в неуправляемый код и роняет процесс.
                AppLog.Error("Сбой в наблюдателе за клавишей.", ex);
            }
        }

        // Ключевая строка: событие уходит дальше по цепочке и доходит до
        // активного окна. Мы наблюдаем, а не перехватываем.
        return CallNextHookEx(nint.Zero, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private delegate nint HookProc(int code, nuint wParam, nint lParam);

    // Здесь DllImport, а не LibraryImport как в остальном проекте: генератор
    // LibraryImport не умеет маршалить параметр-делегат, а хук — это именно
    // делегат. Вызов происходит один раз за диктовку, так что накладные
    // расходы старого маршалинга роли не играют.
#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint SetWindowsHookExW(int idHook, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
#pragma warning restore SYSLIB1054
}
