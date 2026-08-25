using System.Text;

namespace TapRecorder.Core.Windows;

/// <summary>Модификаторы горячей клавиши. Значения совпадают с MOD_* из Win32.</summary>
/// <remarks>
/// <c>[Flags]</c> означает, что значения комбинируются побитово:
/// <c>Control | Alt</c> — это одно значение, а не коллекция.
/// </remarks>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>Сочетание клавиш для глобального хоткея.</summary>
/// <param name="Modifiers">Ctrl / Alt / Shift / Win.</param>
/// <param name="VirtualKey">Виртуальный код основной клавиши (VK_*).</param>
public sealed record HotkeyCombo(HotkeyModifiers Modifiers, ushort VirtualKey)
{
    /// <summary>
    /// Ctrl + Alt + D по умолчанию.
    /// </summary>
    /// <remarks>
    /// Не Win+H: его занимает системная диктовка Windows. Не одиночная
    /// клавиша: сочетание без Ctrl/Alt/Win сломало бы обычный набор текста
    /// во всей системе.
    /// </remarks>
    public static HotkeyCombo Default { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VkD);

    private const ushort VkD = 0x44;

    /// <summary>Годится ли сочетание в глобальные: нужен хотя бы один «настоящий» модификатор.</summary>
    /// <remarks>
    /// Один Shift не считается: ⇧D в качестве глобального хоткея отняло бы у
    /// системы ввод заглавной D.
    /// </remarks>
    public bool IsUsableAsGlobal =>
        (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0;

    public override string ToString()
    {
        var text = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            text.Append("Ctrl + ");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            text.Append("Alt + ");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            text.Append("Shift + ");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            text.Append("Win + ");
        }

        return text.Append(KeyName(VirtualKey)).ToString();
    }

    private static string KeyName(ushort virtualKey) => virtualKey switch
    {
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),          // A–Z
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),          // 0–9
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",                // F1–F24
        0x20 => "Space",
        0x0D => "Enter",
        0x1B => "Esc",
        _ => $"VK_{virtualKey:X2}",
    };
}
