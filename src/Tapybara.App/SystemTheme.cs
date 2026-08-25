using System.IO;
using Microsoft.Win32;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Settings;
using Wpf.Ui.Appearance;

namespace Tapybara.App;

/// <summary>
/// Согласование темы приложения с темой Windows.
/// </summary>
/// <remarks>
/// Тема была зашита тёмной. На светлой системе окно настроек выглядело чужим
/// куском ночи посреди светлого рабочего стола, а иконка покоя в трее —
/// почти белая — становилась невидимой на светлой панели задач: приложение
/// казалось незапущенным.
/// </remarks>
public static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Тема системы сменилась.</summary>
    public static event Action? Changed;

    /// <summary>Светлая ли тема у приложений Windows.</summary>
    public static bool IsAppLight => ReadFlag("AppsUseLightTheme", fallback: true);

    /// <summary>
    /// Светлая ли панель задач.
    /// </summary>
    /// <remarks>
    /// Отдельный ключ: Windows позволяет светлые окна при тёмной панели задач,
    /// и это распространённая комбинация. Иконку в трее надо красить именно
    /// под панель.
    /// </remarks>
    public static bool IsTaskbarLight => ReadFlag("SystemUsesLightTheme", fallback: false);

    /// <summary>Начать следить за сменой темы.</summary>
    public static void StartWatching()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
            {
                Changed?.Invoke();
            }
        };
    }

    /// <summary>Применить выбранную тему к приложению.</summary>
    public static void Apply(AppTheme theme)
    {
        ApplicationTheme resolved = theme switch
        {
            AppTheme.Light => ApplicationTheme.Light,
            AppTheme.Dark => ApplicationTheme.Dark,
            _ => IsAppLight ? ApplicationTheme.Light : ApplicationTheme.Dark,
        };

        ApplicationThemeManager.Apply(resolved);
    }

    private static bool ReadFlag(string valueName, bool fallback)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(valueName) is int value ? value != 0 : fallback;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            AppLog.Warn("Не удалось прочитать тему системы.", ex);
            return fallback;
        }
    }
}
