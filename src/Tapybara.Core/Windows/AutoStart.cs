using Microsoft.Win32;
using Tapybara.Core.Diagnostics;

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

    /// <summary>Путь к исполняемому файлу приложения.</summary>
    public static string ExecutablePath { get; } =
        Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty;

    /// <summary>
    /// Можно ли вообще включать автозапуск для текущего процесса.
    /// </summary>
    /// <remarks>
    /// Под <c>dotnet run</c> путь процесса — это сам <c>dotnet.exe</c>. Записав
    /// его в автозапуск, мы получили бы запуск голого хоста без аргументов при
    /// каждом входе в систему: приложение не стартует, а галочка в настройках
    /// при этом уверенно показывает «включено».
    /// </remarks>
    public static bool IsAvailable =>
        ExecutablePath.Length > 0
        && !string.Equals(
            Path.GetFileNameWithoutExtension(ExecutablePath),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);

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
            if (!IsAvailable)
            {
                return false;
            }

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string command
                       && command.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                AppLog.Warn("Не удалось прочитать автозапуск из реестра.", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// Включить или выключить автозапуск.
    /// </summary>
    /// <returns><c>false</c>, если запись в реестр не удалась.</returns>
    public static bool SetEnabled(bool enabled)
    {
        if (enabled && !IsAvailable)
        {
            AppLog.Warn($"Автозапуск не включён: путь процесса «{ExecutablePath}» не годится.");
            return false;
        }

        try
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

            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            AppLog.Error("Не удалось изменить автозапуск.", ex);
            return false;
        }
    }
}
