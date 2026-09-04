namespace Tapybara.Core.Settings;

/// <summary>Поиск папки с моделями распознавания.</summary>
/// <remarks>
/// Мест несколько, и порядок важен. Установленное приложение держит модели в
/// <c>%APPDATA%</c>, а во время разработки они лежат в репозитории — искать
/// надо и там, иначе после каждой сборки пришлось бы копировать полтора
/// гигабайта.
/// </remarks>
public static class ModelLocator
{
    /// <summary>
    /// Найти папку моделей.
    /// </summary>
    /// <param name="overrideDirectory">
    /// Путь, заданный пользователем в настройках. Если он указан и существует,
    /// побеждает всё остальное.
    /// </param>
    public static string? FindModelsDirectory(string? overrideDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDirectory) && Directory.Exists(overrideDirectory))
        {
            return overrideDirectory;
        }

        if (Directory.Exists(AppPaths.DefaultModelsDirectory))
        {
            return AppPaths.DefaultModelsDirectory;
        }

        string beside = Path.Combine(AppContext.BaseDirectory, "models");
        if (Directory.Exists(beside))
        {
            return beside;
        }

        return FindInRepository();
    }

    /// <summary>Полный путь к модели по имени файла, или null, если её нет.</summary>
    public static string? Resolve(string modelFileName, string? overrideDirectory = null)
    {
        string? directory = FindModelsDirectory(overrideDirectory);
        if (directory is null)
        {
            return null;
        }

        string path = Path.Combine(directory, modelFileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Любая доступная ggml-модель — запасной вариант, если настроенной нет.</summary>
    public static string? ResolveAnyAvailable(string? overrideDirectory = null)
    {
        string? directory = FindModelsDirectory(overrideDirectory);
        if (directory is null)
        {
            return null;
        }

        // Файлы Silero VAD лежат в той же папке и тоже называются ggml-*,
        // но моделью распознавания не являются — отсеиваем по имени.
        return Directory.EnumerateFiles(directory, "ggml-*.bin")
            .Where(p => !Path.GetFileName(p).Contains("silero", StringComparison.OrdinalIgnoreCase))
            .Order()
            .FirstOrDefault();
    }

    /// <summary>
    /// Модели детектора речи отличаются от моделей распознавания только именем.
    /// </summary>
    /// <remarks>
    /// Лежат они в той же папке и тоже называются ggml-*, поэтому списки
    /// приходится разделять по имени файла: silero в списке моделей
    /// распознавания выглядел бы как выбор, который ничего не распознаёт.
    /// </remarks>
    private static bool IsVadModel(string path) =>
        Path.GetFileName(path).Contains("silero", StringComparison.OrdinalIgnoreCase);

    /// <summary>Полный путь к модели детектора речи, или null.</summary>
    public static string? ResolveVadModel(string fileName, string? overrideDirectory = null)
    {
        string? directory = FindModelsDirectory(overrideDirectory);
        if (directory is null)
        {
            return null;
        }

        string path = Path.Combine(directory, fileName);
        if (File.Exists(path))
        {
            return path;
        }

        // Настроенной нет — берём любую доступную: детектор важнее того,
        // какая именно его версия используется.
        return Directory.EnumerateFiles(directory, "ggml-*.bin").Where(IsVadModel).Order().FirstOrDefault();
    }

    /// <summary>Все доступные модели детектора речи.</summary>
    public static IReadOnlyList<string> ListVadModels(string? overrideDirectory = null)
    {
        string? directory = FindModelsDirectory(overrideDirectory);
        return directory is null
            ? []
            : [.. Directory.EnumerateFiles(directory, "ggml-*.bin")
                .Where(IsVadModel)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order()];
    }

    /// <summary>
    /// Модели разделения голосов: сегментация отдельно, слепки отдельно.
    /// </summary>
    /// <remarks>
    /// Различаются по подстроке «segmentation» в имени — тем же способом, что
    /// и модели детектора речи по «silero». Способ грубый, зато не требует
    /// открывать файл и читать заголовок ONNX ради выпадающего списка.
    /// <para>
    /// Перепутать их местами не даёт сама природа моделей: сегментация в роли
    /// слепков не запустится, и нативная сторона скажет об этом сразу, а не
    /// молча выдаст ерунду.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> ListVoiceModels(bool segmentation, string? overrideDirectory = null)
    {
        string? directory = FindModelsDirectory(overrideDirectory);
        if (directory is null)
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(directory, "*.onnx")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => name.Contains("segmentation", StringComparison.OrdinalIgnoreCase) == segmentation)
                .Order(),
        ];
    }

    private static string? FindInRepository()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("Tapybara.sln*").Any())
        {
            dir = dir.Parent;
        }

        string? candidate = dir is null ? null : Path.Combine(dir.FullName, "models");
        return candidate is not null && Directory.Exists(candidate) ? candidate : null;
    }
}
