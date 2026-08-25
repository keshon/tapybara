using Microsoft.Win32;

namespace Tapybara.Core.Windows;

/// <summary>
/// Автозапуск при входе в систему через ключ реестра <c>Run</c>.
/// </summary>
/// <remarks>
/// Ветка HKEY_CURRENT_USER, а не LOCAL_MACHINE: настройка касается одного
/// пользователя и не требует прав администратора. Это же эквивалент
/// <c>SMAppService</c> из macOS-оригинала.
/// </remarks>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Tapybara";

    /// <summary>Включён ли автозапуск и указывает ли он на этот же исполняемый файл.</summary>
    /// <remarks>
    /// Проверяем именно путь: после переноса приложения в другую папку старая
    /// запись осталась бы в реестре и указывала в пустоту, а галочка в
    /// настройках при этом врала бы «включено».
    /// </remarks>
    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string command
                && command.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Путь к исполняемому файлу приложения.</summary>
    public static string ExecutablePath { get; } =
        Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()!.Location;

    /// <summary>Включить или выключить автозапуск.</summary>
    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            // Кавычки обязательны: без них путь с пробелами («C:\Program Files\…»)
            // Windows разберёт как команду с аргументами и запустит не то.
            key.SetValue(ValueName, $"\"{ExecutablePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
