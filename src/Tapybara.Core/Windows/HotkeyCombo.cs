using System.Text;
using System.Text.Json.Serialization;

namespace Tapybara.Core.Windows;

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

    /// <summary>
    /// Ctrl + Alt + C для записи звонка.
    /// </summary>
    /// <remarks>
    /// Рядом с сочетанием диктовки и с той же парой модификаторов: две
    /// записи с микрофона человек запоминает как пару, а не как два
    /// отдельных правила. «C» — call.
    /// <para>
    /// Не Ctrl+Alt+R, хотя «record» напрашивался: на машине автора его уже
    /// держит другая программа, и сочетание по умолчанию, которое не
    /// занимается при первом же запуске, хуже любого другого. Проверено
    /// регистрацией, а не угадано.
    /// </para>
    /// </remarks>
    public static HotkeyCombo DefaultCall { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VkC);

    private const ushort VkC = 0x43;
    private const ushort VkD = 0x44;

    /// <summary>Годится ли сочетание в глобальные: нужен хотя бы один «настоящий» модификатор.</summary>
    /// <remarks>
    /// Один Shift не считается: ⇧D в качестве глобального хоткея отняло бы у
    /// системы ввод заглавной D.
    /// <para>
    /// <c>JsonIgnore</c>: это вывод из модификаторов, а не отдельная настройка,
    /// и в settings.json ему делать нечего.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public bool IsUsableAsGlobal =>
        (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0;

    public override string ToString()
    {
        // Без пробелов вокруг плюса: «Ctrl+Alt+D» — принятая в Windows запись,
        // и она заметно короче. В меню трея ширина считается по самому длинному
        // пункту, так что лишние пробелы там стоят реального места.
        var text = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            text.Append("Ctrl+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            text.Append("Alt+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            text.Append("Shift+");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            text.Append("Win+");
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
