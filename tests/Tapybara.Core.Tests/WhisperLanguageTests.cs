using Tapybara.Core.Settings;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Значения по умолчанию, зависящие от языка.</summary>
public class LanguageDefaultsTests
{
    [Fact]
    public void SupportedUiLanguages_AreEnglishAndRussian() =>
        Assert.Equal(["en", "ru"], LanguageDefaults.SupportedUiLanguages);

    [Fact]
    public void DetectUiLanguage_ReturnsSomethingSupported() =>
        Assert.Contains(LanguageDefaults.DetectUiLanguage(), LanguageDefaults.SupportedUiLanguages);

    [Theory]
    [InlineData("ru")]
    [InlineData("ru-RU")]
    [InlineData("RU")]
    public void DefaultPrompt_IsRussianForRussian(string language) =>
        Assert.Contains("диктовка", LanguageDefaults.DefaultPrompt(language), StringComparison.Ordinal);

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("auto")]
    public void DefaultPrompt_IsEnglishForEverythingElse(string language) =>
        Assert.Contains("dictation", LanguageDefaults.DefaultPrompt(language), StringComparison.Ordinal);

    /// <summary>
    /// Подсказка задаёт стиль пунктуации, а значит должна быть пунктуирована.
    /// </summary>
    /// <remarks>
    /// В этом весь смысл: стоковые модели без промпта пишут сплошным текстом
    /// без знаков препинания, и подражание оформлению подсказки — то, что
    /// заставляет их расставлять знаки самим.
    /// </remarks>
    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void DefaultPrompt_IsPunctuatedAndCapitalised(string language)
    {
        string prompt = LanguageDefaults.DefaultPrompt(language);

        Assert.True(char.IsUpper(prompt[0]), $"«{prompt}» начинается со строчной");
        Assert.EndsWith(".", prompt, StringComparison.Ordinal);
    }
}

/// <summary>Поиск моделей.</summary>
public class ModelLocatorTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "tapybara-tests", Guid.NewGuid().ToString("N"));

    public ModelLocatorTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "ggml-small-q5_1.bin"), "x");
        File.WriteAllText(Path.Combine(_directory, "ggml-large-v3-turbo-q5_0.bin"), "x");
        File.WriteAllText(Path.Combine(_directory, "ggml-silero-v6.2.0.bin"), "x");
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "x");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Временная папка — не то, из-за чего тест должен падать.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void FindModelsDirectory_PrefersTheOverride() =>
        Assert.Equal(_directory, ModelLocator.FindModelsDirectory(_directory));

    [Fact]
    public void Resolve_FindsAModelByName() =>
        Assert.NotNull(ModelLocator.Resolve("ggml-small-q5_1.bin", _directory));

    [Fact]
    public void Resolve_ReturnsNullForAMissingModel() =>
        Assert.Null(ModelLocator.Resolve("ggml-nothing.bin", _directory));

    /// <summary>
    /// Детектор речи не должен попадать в список моделей распознавания.
    /// </summary>
    /// <remarks>
    /// Лежат они в одной папке и называются одинаково — ggml-*.bin. Silero в
    /// выборе модели распознавания выглядел бы как вариант, который ничего
    /// не распознаёт.
    /// </remarks>
    [Fact]
    public void ResolveAnyAvailable_SkipsSpeechDetectors()
    {
        string? found = ModelLocator.ResolveAnyAvailable(_directory);

        Assert.NotNull(found);
        Assert.DoesNotContain("silero", found, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListVadModels_ReturnsOnlyDetectors()
    {
        IReadOnlyList<string> models = ModelLocator.ListVadModels(_directory);

        Assert.Single(models);
        Assert.Contains("silero", models[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveVadModel_FallsBackToAnyDetectorAvailable()
    {
        string? found = ModelLocator.ResolveVadModel("ggml-silero-does-not-exist.bin", _directory);

        Assert.NotNull(found);
        Assert.Contains("silero", found, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Пустая папка — пустой список.
    /// </summary>
    /// <remarks>
    /// Именно СУЩЕСТВУЮЩАЯ пустая, а не выдуманная. Несуществующий путь
    /// <see cref="ModelLocator.FindModelsDirectory"/> по документированному
    /// поведению игнорирует и уходит искать в папку по умолчанию — а там, на
    /// машине разработчика, вполне может лежать настоящая модель. Тест,
    /// написанный на несуществующем пути, проходил ровно до того дня, когда
    /// кто-то скачал детектор себе в %APPDATA%.
    /// </remarks>
    [Fact]
    public void ListVadModels_IsEmptyForAnEmptyDirectory()
    {
        string empty = Path.Combine(_directory, "empty");
        Directory.CreateDirectory(empty);

        Assert.Empty(ModelLocator.ListVadModels(empty));
    }

    [Fact]
    public void FindModelsDirectory_IgnoresAnOverrideThatDoesNotExist()
    {
        string missing = Path.Combine(_directory, "nope");

        Assert.NotEqual(missing, ModelLocator.FindModelsDirectory(missing));
    }
}
