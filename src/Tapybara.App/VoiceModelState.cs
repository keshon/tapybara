using Tapybara.Core.Models;
using Tapybara.Core.Settings;

namespace Tapybara.App;

/// <summary>
/// Каких моделей не хватает для возможности — по типам, а не по файлам.
/// </summary>
/// <remarks>
/// <para>
/// Разделению голосов нужны два типа: модель разделения находит, где сменился
/// говорящий, модель слепков — чей это голос. Кто скачал одну, получал половину
/// возможностей и ни слова о второй.
/// </para>
/// <para>
/// Здесь нет имён моделей и размеров: какие модели каждого типа бывают,
/// знает каталог, а какую из них выбрал человек — настройки. Появится в
/// каталоге новая модель разделения — подсказки и кнопки «скачать» её
/// подхватят, ничего не меняя здесь.
/// </para>
/// </remarks>
internal static class ModelNeeds
{
    /// <summary>Что нужно, чтобы разделять голоса собеседников.</summary>
    public static readonly IReadOnlyList<ModelKind> SplitVoices = [ModelKind.VoiceSegmentation, ModelKind.VoiceEmbedding];

    /// <summary>Каких типов из <paramref name="kinds"/> сейчас нет на диске.</summary>
    public static IReadOnlyList<ModelKind> Missing(AppSettings settings, IReadOnlyList<ModelKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return [.. kinds.Where(kind => ModelLocator.Resolve(ChosenFile(settings, kind), settings.ModelsDirectory) is null)];
    }

    /// <summary>Какой файл этого типа выбран в настройках.</summary>
    private static string ChosenFile(AppSettings settings, ModelKind kind) => kind switch
    {
        ModelKind.VoiceSegmentation => settings.VoiceSegmentationModelFileName,
        ModelKind.VoiceEmbedding => settings.VoiceEmbeddingModelFileName,
        ModelKind.SpeechDetector => settings.VadModelFileName ?? string.Empty,
        _ => settings.ModelFileName ?? string.Empty,
    };
}
