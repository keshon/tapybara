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
/// знает каталог, а какая используется — <see cref="ModelShelf"/>, по тому же
/// правилу, по которому отмечен кружок на странице моделей.
/// </para>
/// </remarks>
internal static class ModelNeeds
{
    /// <summary>Что нужно, чтобы разделять голоса собеседников.</summary>
    public static readonly IReadOnlyList<ModelKind> SplitVoices = [ModelKind.VoiceSegmentation, ModelKind.VoiceEmbedding];

    /// <summary>Каких типов из <paramref name="kinds"/> сейчас нечем выполнить.</summary>
    public static IReadOnlyList<ModelKind> Missing(AppSettings settings, IReadOnlyList<ModelKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string directory = ModelLocator.FindModelsDirectory(settings.ModelsDirectory) ?? AppPaths.DefaultModelsDirectory;
        return ModelShelf.Missing(settings, kinds, ModelStorage.List(directory));
    }
}
