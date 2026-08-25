namespace TapRecorder.Core.Settings;

/// <summary>Поиск папки с моделями распознавания.</summary>
/// <remarks>
/// Мест несколько, и порядок важен. Установленное приложение держит модели в
/// <c>%APPDATA%</c>, а во время разработки они лежат в репозитории — искать
/// надо и там, иначе после каждой сборки пришлось бы копировать полтора
/// гигабайта.
/// </remarks>
public static class ModelLocator
{
    /// <summary>Найти папку моделей: %APPDATA%, рядом с exe, затем корень репозитория.</summary>
    public static string? FindModelsDirectory()
    {
        if (Directory.Exists(SettingsStore.ModelsDirectory))
        {
            return SettingsStore.ModelsDirectory;
        }

        string beside = Path.Combine(AppContext.BaseDirectory, "models");
        if (Directory.Exists(beside))
        {
            return beside;
        }

        return FindInRepository();
    }

    /// <summary>Полный путь к модели по имени файла, или null, если её нет.</summary>
    public static string? Resolve(string modelFileName)
    {
        string? directory = FindModelsDirectory();
        if (directory is null)
        {
            return null;
        }

        string path = Path.Combine(directory, modelFileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Любая доступная ggml-модель — запасной вариант, если настроенной нет.</summary>
    public static string? ResolveAnyAvailable()
    {
        string? directory = FindModelsDirectory();
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

    private static string? FindInRepository()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("TapRecorder.sln*").Any())
        {
            dir = dir.Parent;
        }

        string? candidate = dir is null ? null : Path.Combine(dir.FullName, "models");
        return candidate is not null && Directory.Exists(candidate) ? candidate : null;
    }
}
