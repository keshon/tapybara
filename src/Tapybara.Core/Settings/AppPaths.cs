namespace Tapybara.Core.Settings;

/// <summary>
/// Где приложение хранит настройки и модели.
/// </summary>
/// <remarks>
/// <para>
/// Два режима. Обычный — данные в <c>%APPDATA%\Tapybara</c>, как принято для
/// установленных программ. Портативный — всё рядом с исполняемым файлом, чтобы
/// приложение можно было носить на флешке и не оставлять следов в системе.
/// </para>
/// <para>
/// Режим определяется наличием файла-маркера рядом с exe, а НЕ настройкой
/// внутри settings.json. Иначе получилась бы курица с яйцом: чтобы прочитать
/// настройку, надо знать, где лежит файл настроек.
/// </para>
/// <para>
/// Маркеров признаётся несколько. Основной — <c>portable.txt</c>: так это
/// сделано в RPCS3 и ещё десятке портативных программ, и человек, знакомый с
/// приёмом, положит рядом с exe именно его, не читая никакой документации.
/// Прежнее имя тоже понимается, чтобы у тех, кто уже включил режим, ничего
/// не сломалось.
/// </para>
/// </remarks>
public static class AppPaths
{
    /// <summary>
    /// Имена файлов-маркеров портативного режима.
    /// </summary>
    /// <remarks>
    /// Содержимое не важно, важно наличие. Первое имя — то, которое создаёт
    /// сама программа.
    /// </remarks>
    private static readonly string[] PortableMarkerNames = ["portable.txt", "Tapybara.portable"];

    private const string FolderName = "Tapybara";

    static AppPaths()
    {
        string executableDirectory = AppContext.BaseDirectory;

        ExecutableDirectory = executableDirectory;
        PortableMarkerPath = Path.Combine(executableDirectory, PortableMarkerNames[0]);
        PortableDataDirectory = Path.Combine(executableDirectory, "Data");
        RoamingDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            FolderName);

        // Значение фиксируется на всё время работы процесса — см. SetPortable.
        IsPortable = IsPortableNow;
        DataDirectory = IsPortable ? PortableDataDirectory : RoamingDataDirectory;
    }

    /// <summary>Папка, из которой запущено приложение.</summary>
    public static string ExecutableDirectory { get; }

    /// <summary>
    /// Работает ли приложение в портативном режиме ПРЯМО СЕЙЧАС.
    /// </summary>
    /// <remarks>
    /// Значение зафиксировано при старте. Именно оно определяет, откуда
    /// читаются настройки и модели этим запуском.
    /// </remarks>
    public static bool IsPortable { get; }

    /// <summary>
    /// Лежит ли маркер рядом с программой в этот момент.
    /// </summary>
    /// <remarks>
    /// Отличается от <see cref="IsPortable"/> сразу после переключения:
    /// маркер уже создан, но действующие пути остались прежними до
    /// перезапуска. Переключателю в настройках нужно именно это значение,
    /// иначе он показывал бы старое положение и выглядел сломанным.
    /// </remarks>
    public static bool IsPortableNow =>
        PortableMarkerNames.Any(name => File.Exists(Path.Combine(ExecutableDirectory, name)));

    /// <summary>Действующая папка данных с учётом режима.</summary>
    public static string DataDirectory { get; }

    /// <summary>Папка данных обычного режима — <c>%APPDATA%\Tapybara</c>.</summary>
    public static string RoamingDataDirectory { get; }

    /// <summary>Папка данных портативного режима — <c>Data</c> рядом с exe.</summary>
    public static string PortableDataDirectory { get; }

    /// <summary>Путь к файлу-маркеру, который создаёт сама программа.</summary>
    public static string PortableMarkerPath { get; }

    /// <summary>Папка моделей по умолчанию внутри действующей папки данных.</summary>
    public static string DefaultModelsDirectory => Path.Combine(DataDirectory, "models");

    /// <summary>
    /// Папка моделей по умолчанию для режима, который будет ПОСЛЕ перезапуска.
    /// </summary>
    /// <remarks>
    /// Нужна кнопке «По умолчанию» в настройках: если человек только что
    /// включил портативный режим, вернуть его к пути от прежнего режима было
    /// бы издевательством.
    /// </remarks>
    public static string PendingDefaultModelsDirectory => Path.Combine(
        IsPortableNow ? PortableDataDirectory : RoamingDataDirectory,
        "models");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    /// <summary>История диктовок — рядом с настройками, чтобы портативный режим увозил и её.</summary>
    public static string DictationsPath => Path.Combine(DataDirectory, "dictations.jsonl");

    /// <summary>Книга голосов — рядом с настройками, по той же причине.</summary>
    public static string VoicesPath => Path.Combine(DataDirectory, "voices.json");

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
                + "Удалите файл, чтобы вернуться к хранению в %APPDATA%\\Tapybara.");
            return;
        }

        // Убираем ВСЕ известные маркеры, а не только свой: иначе выключение
        // не выключало бы режим, если файл положили руками под другим именем.
        foreach (string name in PortableMarkerNames)
        {
            File.Delete(Path.Combine(ExecutableDirectory, name));
        }
    }
}
