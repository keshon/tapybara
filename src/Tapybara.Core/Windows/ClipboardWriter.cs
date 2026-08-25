using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Tapybara.Core.Windows;

/// <summary>
/// Запись текста в буфер обмена напрямую через Win32.
/// </summary>
/// <remarks>
/// Не <c>System.Windows.Clipboard</c> из WPF намеренно: тот требует потока в
/// режиме STA (в консоли и на фоновых потоках просто не работает) и не умеет
/// класть служебные форматы, которыми буфер прячется из истории Win+V.
/// </remarks>
public static class ClipboardWriter
{
    /// <summary>Сколько раз пробовать открыть буфер, если его держит другое приложение.</summary>
    private const int OpenAttempts = 10;

    /// <summary>Положить текст в буфер обмена.</summary>
    /// <param name="text">Что положить.</param>
    /// <param name="excludeFromHistory">
    /// Не показывать эту запись в истории буфера (Win+V) и не синхронизировать
    /// её с облачным буфером.
    /// </param>
    /// <remarks>
    /// Про <paramref name="excludeFromHistory"/>. В macOS-оригинале автор
    /// сознательно отказался восстанавливать прежнее содержимое буфера: попытка
    /// «вернуть как было» лишь удваивала записи в менеджерах истории. В Windows
    /// у этой дилеммы есть решение, которого на маке нет, — служебные форматы
    /// буфера: текст доступен для обычного Ctrl+V, но в историю не попадает.
    /// </remarks>
    /// <exception cref="Win32Exception">Буфер обмена занят другим процессом.</exception>
    public static void SetText(string text, bool excludeFromHistory = true)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!TryOpenClipboard())
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Не удалось открыть буфер обмена — его удерживает другое приложение.");
        }

        try
        {
            NativeMethods.EmptyClipboard();
            PutUnicodeText(text);

            if (excludeFromHistory)
            {
                // Значение 0 в этих форматах = «нельзя». Формат
                // ExcludeClipboardContentFromMonitorProcessing сознательно НЕ
                // ставим: он прячет запись от всех менеджеров буфера вообще,
                // включая сторонние, которыми пользователь может пользоваться
                // намеренно. Нам нужно убрать мусор только из истории Windows.
                PutDwordFlag("CanIncludeInClipboardHistory", 0);
                PutDwordFlag("CanUploadToCloudClipboard", 0);
            }
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static bool TryOpenClipboard()
    {
        for (int attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (NativeMethods.OpenClipboard(nint.Zero))
            {
                return true;
            }

            // Буфер — общесистемный ресурс с эксклюзивным захватом: его на
            // миллисекунды занимают браузеры, менеджеры буфера, сам explorer.
            // Отказ с первой попытки — норма, а не ошибка.
            Thread.Sleep(20);
        }

        return false;
    }

    /// <summary>Положить строку как CF_UNICODETEXT.</summary>
    private static void PutUnicodeText(string text)
    {
        // +1 символ под завершающий ноль: получатели читают строку до него.
        nuint bytes = (nuint)((text.Length + 1) * sizeof(char));
        nint handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, bytes);
        if (handle == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось выделить память под буфер обмена.");
        }

        try
        {
            nint target = NativeMethods.GlobalLock(handle);
            if (target == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось заблокировать память буфера обмена.");
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                Marshal.WriteInt16(target, text.Length * sizeof(char), 0); // завершающий ноль
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }

            if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, handle) == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Буфер обмена отклонил текст.");
            }

            // Успех — владельцем памяти стала система, освобождать её нельзя.
            handle = nint.Zero;
        }
        finally
        {
            if (handle != nint.Zero)
            {
                NativeMethods.GlobalFree(handle);
            }
        }
    }

    /// <summary>Положить служебный флаг-DWORD под именованным форматом.</summary>
    private static void PutDwordFlag(string formatName, int value)
    {
        uint format = NativeMethods.RegisterClipboardFormat(formatName);
        if (format == 0)
        {
            return; // формат не поддерживается этой версией Windows — не беда
        }

        nint handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, sizeof(int));
        if (handle == nint.Zero)
        {
            return;
        }

        nint target = NativeMethods.GlobalLock(handle);
        if (target == nint.Zero)
        {
            NativeMethods.GlobalFree(handle);
            return;
        }

        Marshal.WriteInt32(target, value);
        NativeMethods.GlobalUnlock(handle);

        if (NativeMethods.SetClipboardData(format, handle) == nint.Zero)
        {
            NativeMethods.GlobalFree(handle);
        }
    }
}
