using Tapybara.Core.Models;
using Tapybara.Core.Settings;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Страница моделей: что скачано, что используется, что своё.</summary>
public sealed class ModelShelfTests
{
    private static InstalledModel File(string name) =>
        new(name, @"C:\models\" + name, 1000, ModelStorage.KindOf(name));

    [Theory]
    [InlineData("ggml-large-v3-turbo-q5_0.bin", ModelKind.Recognition)]
    [InlineData("ggml-silero-v6.2.0.bin", ModelKind.SpeechDetector)]
    [InlineData("pyannote-segmentation-3.0.onnx", ModelKind.VoiceSegmentation)]
    [InlineData("campplus-multilingual.onnx", ModelKind.VoiceEmbedding)]
    public void KindOf_TellsModelsApartByName(string name, ModelKind kind) =>
        Assert.Equal(kind, ModelStorage.KindOf(name));

    /// <summary>
    /// Тип модели каталога совпадает с тем, что скажет о её файле диск: иначе
    /// скачанная модель показалась бы нескачанной и попала бы не в свою группу.
    /// </summary>
    [Fact]
    public void KindOf_AgreesWithTheCatalog() =>
        Assert.All(ModelCatalog.All, m => Assert.Equal(m.Kind, ModelStorage.KindOf(m.FileName)));

    [Fact]
    public void Missing_CountsAFallbackAsPresent()
    {
        var settings = new AppSettings { ModelFileName = "ggml-gone.bin", VoiceEmbeddingModelFileName = "gone.onnx" };
        InstalledModel[] installed = [File("ggml-small-q5_1.bin"), File("campplus-voxceleb.onnx")];

        Assert.Equal(
            [ModelKind.VoiceEmbedding],
            ModelShelf.Missing(settings, [ModelKind.Recognition, ModelKind.VoiceEmbedding], installed));
    }

    [Fact]
    public void Of_ListsCatalogFirstThenOwnFiles_AndMarksTheChosenOne()
    {
        var settings = new AppSettings { ModelFileName = "ggml-small-q5_1.bin" };
        InstalledModel[] installed = [File("ggml-small-q5_1.bin"), File("ggml-ru-finetune.bin"), File("ggml-silero-v6.2.0.bin")];

        IReadOnlyList<ShelfModel> shelf = ModelShelf.Of(ModelKind.Recognition, installed, settings);

        Assert.Equal(ModelCatalog.All.Count(m => m.Kind == ModelKind.Recognition) + 1, shelf.Count);
        Assert.Equal("ggml-ru-finetune.bin", shelf[^1].FileName);
        Assert.True(shelf[^1].IsCustom && shelf[^1].IsInstalled);
        Assert.Equal(["ggml-small-q5_1.bin"], shelf.Where(m => m.InUse).Select(m => m.FileName));
        Assert.DoesNotContain(shelf, m => m.FileName.Contains("silero", StringComparison.Ordinal));
    }

    /// <summary>
    /// Выбранной нет на диске — распознавание работает первой попавшейся,
    /// и отмечена должна быть она.
    /// </summary>
    [Fact]
    public void Of_MarksTheFallbackWhenTheChosenFileIsGone()
    {
        var settings = new AppSettings { ModelFileName = "ggml-large-v3-turbo-q5_0.bin" };

        IReadOnlyList<ShelfModel> shelf = ModelShelf.Of(
            ModelKind.Recognition, [File("ggml-small-q5_1.bin"), File("ggml-base-q5_1.bin")], settings);

        Assert.Equal(["ggml-base-q5_1.bin"], shelf.Where(m => m.InUse).Select(m => m.FileName));
    }

    /// <summary>Голосовым моделям замены нет: не скачана выбранная — не отмечена ни одна.</summary>
    [Fact]
    public void Of_VoiceModelsHaveNoFallback()
    {
        var settings = new AppSettings { VoiceEmbeddingModelFileName = "campplus-multilingual.onnx" };

        IReadOnlyList<ShelfModel> shelf = ModelShelf.Of(
            ModelKind.VoiceEmbedding, [File("campplus-voxceleb.onnx")], settings);

        Assert.DoesNotContain(shelf, m => m.InUse);
        Assert.Contains(shelf, m => m.FileName == "campplus-voxceleb.onnx" && m.IsInstalled);
    }

    [Theory]
    [InlineData(ModelKind.Recognition)]
    [InlineData(ModelKind.SpeechDetector)]
    [InlineData(ModelKind.VoiceSegmentation)]
    [InlineData(ModelKind.VoiceEmbedding)]
    public void Choose_WritesWhatChosenReads(ModelKind kind) =>
        Assert.Equal("x.bin", ModelShelf.Chosen(ModelShelf.Choose(new AppSettings(), kind, "x.bin"), kind));
}
