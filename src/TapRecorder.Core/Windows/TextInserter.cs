using System.Runtime.InteropServices;

namespace TapRecorder.Core.Windows;

/// <summary>Вставка распознанного текста в поле под курсором.</summary>
public static class TextInserter
{
    /// <summary>
    /// Пауза между отпусканием «залипших» модификаторов и Ctrl+V.
    /// </summary>
    /// <remarks>
    /// Без неё целевое приложение может не успеть обработать отпускание Alt
    /// и увидит Ctrl+Alt+V вместо Ctrl+V.
    /// </remarks>
    private static readonly TimeSpan ModifierSettleDelay = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// Положить текст в буфер и вставить его в активное окно через Ctrl+V.
    /// </summary>
    /// <remarks>
    /// Это универсальный путь: он работает и в нативных приложениях, и в
    /// Electron/Chromium, где прямая вставка через UI Automation принимается
    /// «на словах», но не применяется.
    /// </remarks>
    public static void PasteViaClipboard(string text, bool excludeFromHistory = true)
    {
        ClipboardWriter.SetText(text, excludeFromHistory);

        ReleaseHeldModifiers();
        Thread.Sleep(ModifierSettleDelay);

        SendKeyChord(NativeMethods.VK_CONTROL, NativeMethods.VK_V);
    }

    /// <summary>
    /// Отпустить модификаторы, которые пользователь физически держит нажатыми.
    /// </summary>
    /// <remarks>
    /// Грабля, которой нет на macOS. Диктовка запускается сочетанием вроде
    /// Ctrl+Alt+D, и к моменту вставки пользователь всё ещё держит эти клавиши.
    /// Отправленный поверх них Ctrl+V превращается в Ctrl+Alt+V — приложение
    /// либо проигнорирует его, либо выполнит совсем другую команду.
    /// Поэтому перед вставкой синтезируем отпускание всего, что зажато.
    /// </remarks>
    private static void ReleaseHeldModifiers()
    {
        ushort[] modifiers =
        [
            NativeMethods.VK_CONTROL,
            NativeMethods.VK_MENU,
            NativeMethods.VK_SHIFT,
            NativeMethods.VK_LWIN,
            NativeMethods.VK_RWIN,
        ];

        List<NativeMethods.INPUT> release = [];
        foreach (ushort key in modifiers)
        {
            // Старший бит результата — «клавиша нажата прямо сейчас».
            if ((NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0)
            {
                release.Add(KeyEvent(key, keyUp: true));
            }
        }

        if (release.Count > 0)
        {
            Send([.. release]);
        }
    }

    /// <summary>Нажать модификатор, нажать и отпустить клавишу, отпустить модификатор.</summary>
    private static void SendKeyChord(ushort modifier, ushort key) => Send(
    [
        KeyEvent(modifier, keyUp: false),
        KeyEvent(key, keyUp: false),
        KeyEvent(key, keyUp: true),
        KeyEvent(modifier, keyUp: true),
    ]);

    private static NativeMethods.INPUT KeyEvent(ushort virtualKey, bool keyUp) => new()
    {
        Type = NativeMethods.INPUT_KEYBOARD,
        Union = new NativeMethods.INPUTUNION
        {
            Keyboard = new NativeMethods.KEYBDINPUT
            {
                Vk = virtualKey,
                Flags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
            },
        },
    };

    private static void Send(NativeMethods.INPUT[] inputs)
    {
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent == inputs.Length)
        {
            return;
        }

        // Самая частая причина — UIPI: обычный процесс не имеет права слать
        // ввод в окно, запущенное от администратора. Молча проглатывать это
        // нельзя, иначе пользователь решит, что приложение сломалось.
        int error = Marshal.GetLastWin32Error();
        throw new InvalidOperationException(
            $"Не удалось отправить ввод (код {error}). Обычно это значит, что активное окно "
            + "запущено от имени администратора, а TapRecorder — нет. Текст остался в буфере обмена.");
    }
}
