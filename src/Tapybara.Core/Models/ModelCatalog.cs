namespace Tapybara.Core.Models;

/// <summary>Для чего модель нужна.</summary>
public enum ModelKind
{
    /// <summary>Whisper: превращает звук в текст.</summary>
    Recognition,

    /// <summary>Silero: находит, где в записи речь. Меньше мегабайта.</summary>
    SpeechDetector,
}

/// <summary>
/// Во что обходится качество.
/// </summary>
/// <remarks>
/// Перечисление, а не готовая строка: <c>Core</c> не знает языка интерфейса,
/// и подписи ему не принадлежат.
/// </remarks>
public enum ModelTier
{
    /// <summary>Лучшее качество, самая большая.</summary>
    Best,

    /// <summary>Разумное умолчание: качество почти как у полной, размер втрое меньше.</summary>
    Recommended,

    /// <summary>Компромисс ради размера.</summary>
    Compact,

    /// <summary>Для слабого железа и проверки, что всё вообще работает.</summary>
    Minimal,
}

/// <summary>Одна модель, которую приложение умеет скачать само.</summary>
/// <param name="FileName">Имя файла в папке моделей.</param>
/// <param name="DisplayName">Короткое имя для списка.</param>
/// <param name="Kind">Распознавание или детектор речи.</param>
/// <param name="Tier">Место в ряду «качество против размера».</param>
/// <param name="ApproximateBytes">Размер для показа ДО скачивания; точный придёт из заголовка ответа.</param>
/// <param name="Url">Прямая ссылка на файл.</param>
public sealed record CatalogModel(
    string FileName,
    string DisplayName,
    ModelKind Kind,
    ModelTier Tier,
    long ApproximateBytes,
    string Url);

/// <summary>
/// Модели, которые приложение предлагает скачать.
/// </summary>
/// <remarks>
/// <para>
/// Без этого списка первый запуск — тупик: приложение сообщает, что модели
/// нет, и предлагает пользователю самому найти в интернете полуторагигабайтный
/// файл, о котором он ничего не знает. Половина установок заканчивается здесь.
/// </para>
/// <para>
/// Список намеренно короткий. Полный набор на Hugging Face — это тридцать с
/// лишним файлов, отличающихся квантизацией и языковыми вариантами; выбор из
/// тридцати вариантов для человека, который просто хочет диктовать, ничем не
/// лучше отсутствия выбора вовсе.
/// </para>
/// </remarks>
public static class ModelCatalog
{
    private const string WhisperRepository = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
    private const string VadRepository = "https://huggingface.co/ggml-org/whisper-vad/resolve/main/";

    /// <summary>Страница со всеми моделями — на случай, если нужного варианта в списке нет.</summary>
    public const string WhisperModelsPageUrl = "https://huggingface.co/ggerganov/whisper.cpp/tree/main";

    /// <summary>Всё, что приложение умеет скачать.</summary>
    public static IReadOnlyList<CatalogModel> All { get; } =
    [
        new("ggml-large-v3-turbo-q5_0.bin", "Large v3 Turbo (q5_0)",
            ModelKind.Recognition, ModelTier.Recommended, 574_000_000,
            WhisperRepository + "ggml-large-v3-turbo-q5_0.bin"),

        new("ggml-large-v3-turbo-q8_0.bin", "Large v3 Turbo (q8_0)",
            ModelKind.Recognition, ModelTier.Best, 874_000_000,
            WhisperRepository + "ggml-large-v3-turbo-q8_0.bin"),

        new("ggml-large-v3-turbo.bin", "Large v3 Turbo (f16)",
            ModelKind.Recognition, ModelTier.Best, 1_620_000_000,
            WhisperRepository + "ggml-large-v3-turbo.bin"),

        new("ggml-medium-q5_0.bin", "Medium (q5_0)",
            ModelKind.Recognition, ModelTier.Compact, 539_000_000,
            WhisperRepository + "ggml-medium-q5_0.bin"),

        new("ggml-small-q5_1.bin", "Small (q5_1)",
            ModelKind.Recognition, ModelTier.Compact, 190_000_000,
            WhisperRepository + "ggml-small-q5_1.bin"),

        new("ggml-base-q5_1.bin", "Base (q5_1)",
            ModelKind.Recognition, ModelTier.Minimal, 59_700_000,
            WhisperRepository + "ggml-base-q5_1.bin"),

        new("ggml-tiny-q5_1.bin", "Tiny (q5_1)",
            ModelKind.Recognition, ModelTier.Minimal, 32_200_000,
            WhisperRepository + "ggml-tiny-q5_1.bin"),

        new("ggml-silero-v6.2.0.bin", "Silero VAD v6.2.0",
            ModelKind.SpeechDetector, ModelTier.Recommended, 885_098,
            VadRepository + "ggml-silero-v6.2.0.bin"),

        new("ggml-silero-v5.1.2.bin", "Silero VAD v5.1.2",
            ModelKind.SpeechDetector, ModelTier.Compact, 885_098,
            VadRepository + "ggml-silero-v5.1.2.bin"),
    ];

    /// <summary>Модель по имени файла, если она из списка.</summary>
    public static CatalogModel? Find(string fileName) =>
        All.FirstOrDefault(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>Что предложить, когда моделей нет вовсе.</summary>
    public static CatalogModel DefaultRecognitionModel =>
        All.First(m => m.Kind == ModelKind.Recognition && m.Tier == ModelTier.Recommended);

    /// <summary>Детектор речи по умолчанию.</summary>
    public static CatalogModel DefaultSpeechDetectorModel =>
        All.First(m => m.Kind == ModelKind.SpeechDetector && m.Tier == ModelTier.Recommended);
}
