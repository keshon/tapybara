namespace TapRecorder.Core.Settings;

/// <summary>
/// Где приложение хранит настройки и модели.
/// </summary>
/// <remarks>
/// <para>
/// Два режима. Обычный — данные в <c>%APPDATA%\TapRecorder</c>, как принято для
/// установленных программ. Портативный — всё рядом с исполняемым файлом, чтобы
/// приложение можно было носить на флешке и не оставлять следов в системе.
/// </para>
/// <para>
/// Режим определяется наличием файла-маркера рядом с exe, а НЕ настройкой
/// внутри settings.json. Иначе получилась бы курица с яйцом: чтобы прочитать
/// настройку, надо знать, где лежит файл настроек.
/// </para>
/// </remarks>
public static class AppPaths
{
    /// <summary>Файл-маркер портативного режима. Пустой — важно лишь его наличие.</summary>
    private const string PortableMarkerName = "TapRecorder.portable";

    private const string FolderName = "TapRecorder";

    static AppPaths()
    {
        string executableDirectory = AppContext.BaseDirectory;

        PortableMarkerPath = Path.Combine(executableDirectory, PortableMarkerName);
        PortableDataDirectory = Path.Combine(executableDirectory, "Data");
        RoamingDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            FolderName);

        IsPortable = File.Exists(PortableMarkerPath);
        DataDirectory = IsPortable ? PortableDataDirectory : RoamingDataDirectory;
    }

    /// <summary>Работает ли приложение в портативном режиме.</summary>
    public static bool IsPortable { get; }

    /// <summary>Действующая папка данных с учётом режима.</summary>
    public static string DataDirectory { get; }

    /// <summary>Папка данных обычного режима — <c>%APPDATA%\TapRecorder</c>.</summary>
    public static string RoamingDataDirectory { get; }

    /// <summary>Папка данных портативного режима — <c>Data</c> рядом с exe.</summary>
    public static string PortableDataDirectory { get; }

    /// <summary>Путь к файлу-маркеру портативного режима.</summary>
    public static string PortableMarkerPath { get; }

    /// <summary>Папка моделей по умолчанию внутри действующей папки данных.</summary>
    public static string DefaultModelsDirectory => Path.Combine(DataDirectory, "models");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    /// <summary>
    /// Папка со звонками по умолчанию.
    /// </summary>
    /// <remarks>
    /// В «Документах», а не рядом с настройками: записи и транскрипты —
    /// это документы пользователя, которые он открывает, ищет и переносит,
    /// а не служебные файлы приложения. В портативном режиме — рядом с
    /// программой, чтобы ничего не оставалось в системе.
    /// </remarks>
    public static string DefaultCallsDirectory => IsPortable
        ? Path.Combine(PortableDataDirectory, "Calls")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            FolderName,
            "Calls");

    /// <summary>
    /// Переключить портативный режим.
    /// </summary>
    /// <remarks>
    /// Изменение вступает в силу только после перезапуска: пути вычисляются
    /// один раз при старте, и менять их у работающего приложения — верный
    /// способ получить настройки в одной папке, а модели в другой.
    /// Файлы между папками НЕ переносятся: модели весят гигабайты, и решать
    /// их судьбу должен пользователь, а не переключатель в настройках.
    /// </remarks>
    public static void SetPortable(bool enabled)
    {
        if (enabled)
        {
            Directory.CreateDirectory(PortableDataDirectory);
            File.WriteAllText(
                PortableMarkerPath,
                "Наличие этого файла включает портативный режим:" + Environment.NewLine
                + "настройки и модели берутся из папки Data рядом с программой." + Environment.NewLine
                + "Удалите файл, чтобы вернуться к хранению в %APPDATA%\\TapRecorder.");
        }
        else
        {
            File.Delete(PortableMarkerPath);
        }
    }
}
