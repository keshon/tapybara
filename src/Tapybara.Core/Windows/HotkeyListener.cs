using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Tapybara.Core.Windows;

/// <summary>
/// Глобальная горячая клавиша: срабатывает из любого приложения.
/// </summary>
/// <remarks>
/// <para>
/// Внутри — <c>RegisterHotKey</c> с нулевым дескриптором окна. Тогда Windows
/// шлёт <c>WM_HOTKEY</c> не окну, а в очередь сообщений ПОТОКА, который вызвал
/// регистрацию. Это избавляет от необходимости заводить скрытое окно и делает
/// класс пригодным и для консольного приложения, и для WPF.
/// </para>
/// <para>
/// Расплата: поток обязан крутить цикл <c>GetMessage</c> и не может заниматься
/// ничем другим, поэтому listener поднимает собственный поток.
/// </para>
/// </remarks>
public sealed class HotkeyListener : IDisposable
{
    private const int HotkeyId = 1;

    private readonly HotkeyCombo _combo;
    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _thread;
    private uint _threadId;
    private Exception? _startFailure;
    private bool _disposed;

    public HotkeyListener(HotkeyCombo combo) => _combo = combo;

    /// <summary>
    /// Хоткей нажат.
    /// </summary>
    /// <remarks>
    /// ВНИМАНИЕ: вызывается из внутреннего потока listener'а, а не из UI-потока.
    /// Всё, что трогает интерфейс, обработчик обязан переправить в диспетчер.
    /// </remarks>
    public event Action? Pressed;

    /// <summary>Занять сочетание и начать слушать.</summary>
    /// <exception cref="Win32Exception">
    /// Сочетание уже занято другим приложением. Это ожидаемая ситуация,
    /// а не сбой: вызывающий код должен показать её пользователю.
    /// </exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null)
        {
            return;
        }

        var thread = new Thread(RunMessageLoop)
        {
            Name = $"Hotkey {_combo}",
            IsBackground = true,
        };

        _thread = thread;
        thread.Start();

        // Ждём, пока поток отчитается о результате регистрации: иначе Start()
        // вернул бы управление раньше, чем стало известно, занято сочетание или нет.
        _ready.Wait();
        if (_startFailure is not null)
        {
            _thread = null;
            throw _startFailure;
        }
    }

    private void RunMessageLoop()
    {
        _threadId = NativeMethods.GetCurrentThreadId();

        // MOD_NOREPEAT: пока клавишу держат, событие приходит один раз, а не
        // потоком автоповтора — иначе удержание хоткея переключало бы диктовку
        // десятки раз в секунду.
        uint modifiers = (uint)_combo.Modifiers | NativeMethods.MOD_NOREPEAT;

        if (!NativeMethods.RegisterHotKey(nint.Zero, HotkeyId, modifiers, _combo.VirtualKey))
        {
            _startFailure = new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Не удалось занять {_combo} — сочетание уже используется другим приложением.");
            _ready.Set();
            return;
        }

        _ready.Set();

        try
        {
            // GetMessage блокирует поток до прихода сообщения и возвращает 0
            // на WM_QUIT — именно его шлёт Dispose, чтобы завершить цикл.
            while (NativeMethods.GetMessage(out NativeMethods.MSG message, nint.Zero, 0, 0) > 0)
            {
                if (message.Message == NativeMethods.WM_HOTKEY)
                {
                    Pressed?.Invoke();
                }
            }
        }
        finally
        {
            NativeMethods.UnregisterHotKey(nint.Zero, HotkeyId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_thread is { } thread && _threadId != 0)
        {
            NativeMethods.PostThreadMessageW(_threadId, NativeMethods.WM_QUIT, 0, 0);
            thread.Join(TimeSpan.FromSeconds(2));
        }

        _thread = null;
        _ready.Dispose();
    }
}
