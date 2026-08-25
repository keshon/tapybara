using System.Text.Json;
using System.Text.Json.Serialization;

namespace TapRecorder.Core.Settings;

/// <summary>Загрузка и сохранение настроек в <c>%APPDATA%\TapRecorder\settings.json</c>.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Файл читает человек: русские буквы в промпте и заменах должны быть
        // буквами, а не последовательностями \uXXXX.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Папка с данными приложения — с учётом портативного режима.</summary>
    public static string DataDirectory => AppPaths.DataDirectory;

    public static string SettingsPath => AppPaths.SettingsPath;

    /// <summary>Прочитать настройки. Битый или отсутствующий файл — это значения по умолчанию.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Настройки — не то, из-за чего приложение имеет право не запуститься.
            return new AppSettings();
        }
    }

    /// <summary>
    /// Сохранить настройки.
    /// </summary>
    /// <remarks>
    /// Запись через временный файл и переименование. Прямая запись поверх
    /// существующего файла оставляет обрезанный JSON, если процесс убьют или
    /// машина выключится на середине, — и приложение больше не стартует
    /// с сохранёнными настройками. Переименование в пределах тома атомарно.
    /// </remarks>
    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);

        string temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, SettingsPath, overwrite: true);
    }
}
