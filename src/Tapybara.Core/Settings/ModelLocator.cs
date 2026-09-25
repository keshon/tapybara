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
        // Заданная пользователем папка побеждает всегда, даже пустую: он указал
        // именно её, и молча уехать в другую значило бы проигнорировать выбор.
        if (!string.IsNullOrWhiteSpace(overrideDirectory) && Directory.Exists(overrideDirectory))
        {
            return overrideDirectory;
        }

        // Дальше выбираем первую папку, в которой ЕСТЬ модели, а не первую
        // существующую. Разница не теоретическая: приложение создаёт папку по
        // умолчанию заранее, и пустая она заслоняла собой ту, куда модели
        // действительно положили — рядом с exe или в репозитории. Выглядело
        // это как «модель не найдена» при полутора гигабайтах на диске.
        // По одному кандидату за раз, а не списком: поиск репозитория обходит
        // дерево каталогов вверх, а зовут этот метод часто — на каждое чтение
        // списка моделей в окне настроек. Вычислять его, когда до него не
        // дошло, значит платить обходом диска за ответ, который не нужен.
        if (HasAnyModel(AppPaths.DefaultModelsDirectory))
        {
            return AppPaths.DefaultModelsDirectory;
        }

        string beside = Path.Combine(AppContext.BaseDirectory, "models");
        if (HasAnyModel(beside))
        {
            return beside;
        }

        string? repository = FindInRepository();
        return Choose([AppPaths.DefaultModelsDirectory, beside, repository]);
    }

    /// <summary>
    /// Выбрать папку моделей из кандидатов в порядке предпочтения.
    /// </summary>
    /// <remarks>
    /// Первая, в которой ЕСТЬ модели, — а если их нет нигде, первая
    /// существующая: скачивать их всё равно куда-то нужно.
    /// <para>
    /// Отдельная функция ради теста. Здесь была ошибка «первая существующая»
    /// вместо «первая с моделями», и приложение, которое само создаёт папку по
    /// умолчанию заранее, этой пустышкой заслоняло полуторагигабайтную модель
    /// рядом с собой. Проверить это внутри метода, который читает
    /// <c>AppContext.BaseDirectory</c> и путь установки, было невозможно.
    /// </para>
    /// </remarks>
    internal static string? Choose(IReadOnlyList<string?> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        foreach (string? candidate in candidates)
        {
            if (candidate is not null && HasAnyModel(candidate))
            {
                return candidate;
            }
        }

        return candidates.OfType<string>().FirstOrDefault(Directory.Exists);
    }

    /// <summary>
    /// Лежит ли в папке хоть одна модель — любого назначения.
    /// </summary>
    /// <remarks>
    /// Считаем моделью и <c>.bin</c>, и <c>.onnx</c>: папка с одними лишь
    /// моделями разделения голосов — тоже папка моделей, и уводить из неё
    /// поиск в пустую было бы тем же самым дефектом наизнанку.
    /// </remarks>
    private static bool HasAnyModel(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                   && (Directory.EnumerateFiles(directory, "*.bin").Any()
                       || Directory.EnumerateFiles(directory, "*.onnx").Any());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
            .Order(StringComparer.Ordinal)
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
        return Directory.EnumerateFiles(directory, "ggml-*.bin").Where(IsVadModel).Order(StringComparer.Ordinal).FirstOrDefault();
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
