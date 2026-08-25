using System.Text.Json;
using System.Text.Json.Serialization;
using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Settings;

/// <summary>Загрузка и сохранение настроек в <c>%APPDATA%\Tapybara\settings.json</c>.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Файл читает человек: русские буквы в промпте и заменах должны быть
        // буквами, а не последовательностями \uXXXX.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Lock Gate = new();

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
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            return Normalize(settings);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Настройки — не то, из-за чего приложение имеет право не запуститься.
            AppLog.Warn("Не удалось прочитать настройки, беру значения по умолчанию.", ex);
            return new AppSettings();
        }
    }

    /// <summary>
    /// Сохранить настройки. Возвращает <c>false</c>, если записать не удалось.
    /// </summary>
    /// <remarks>
    /// Запись через временный файл и переименование. Прямая запись поверх
    /// существующего файла оставляет обрезанный JSON, если процесс убьют или
    /// машина выключится на середине, — и приложение больше не стартует
    /// с сохранёнными настройками. Переименование в пределах тома атомарно.
    /// <para>
    /// Исключение наружу НЕ выпускаем: сохранение вызывается из обработчиков
    /// интерфейса, а полный диск или профиль только на чтение не повод убивать
    /// приложение без единого сообщения.
    /// </para>
    /// </remarks>
    public static bool Save(AppSettings settings)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);

                string temp = SettingsPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
                File.Move(temp, SettingsPath, overwrite: true);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Error("Не удалось сохранить настройки.", ex);
            return false;
        }
    }

    /// <summary>
    /// Привести прочитанное к тем же правилам, по которым его строит интерфейс.
    /// </summary>
    /// <remarks>
    /// Окно настроек собирает словарь замен без учёта регистра, а
    /// <c>System.Text.Json</c> отдаёт обычный, регистрозависимый. Без
    /// выравнивания «Foo» и «foo» ведут себя по-разному до и после
    /// перезапуска — ошибка, которую невозможно связать с причиной.
    /// </remarks>
    private static AppSettings Normalize(AppSettings settings)
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string from, string to) in settings.Replacements)
        {
            replacements[from] = to;
        }

        return settings with { Replacements = replacements };
    }
}
