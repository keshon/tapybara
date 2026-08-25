using System.Text.Json;
using Tapybara.Core.Settings;
using Tapybara.Core.Windows;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Сериализация настроек: файл читает и правит человек.</summary>
public class SettingsSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, Options);

    /// <summary>
    /// Вычисляемые свойства в файл не попадают.
    /// </summary>
    /// <remarks>
    /// Они выглядят как настоящие настройки, при чтении молча игнорируются и
    /// потому обещают ровно то, чего не делают: человек правит
    /// <c>effectiveParagraphPause</c> и не понимает, почему ничего не меняется.
    /// </remarks>
    [Theory]
    [InlineData("EffectiveParagraphPause")]
    [InlineData("IsUsableAsGlobal")]
    public void Serialize_OmitsComputedProperties(string name) =>
        Assert.DoesNotContain(name, Serialize(new AppSettings()), StringComparison.Ordinal);

    /// <summary>Перечисления пишутся словом, а не номером.</summary>
    /// <remarks>
    /// <c>"theme": 2</c> в файле, который правят руками, — это загадка;
    /// <c>"theme": "Dark"</c> — это настройка.
    /// </remarks>
    [Fact]
    public void Serialize_WritesEnumsAsNames() =>
        Assert.Contains("\"Dark\"", Serialize(new AppSettings { Theme = AppTheme.Dark }), StringComparison.Ordinal);

    [Fact]
    public void Serialize_KeepsCyrillicReadable()
    {
        var settings = new AppSettings { Prompt = "Рабочая диктовка" };

        Assert.Contains("Рабочая диктовка", Serialize(settings), StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_PreservesEverythingThatMatters()
    {
        var original = new AppSettings
        {
            ModelFileName = "ggml-small-q5_1.bin",
            Language = "en",
            UiLanguage = "ru",
            Theme = AppTheme.Light,
            Prompt = "Заметки и задачи",
            Hotkey = new HotkeyCombo(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x51),
            MicrophoneDeviceId = "{0.0.1.00000000}.{guid}",
            VadThreshold = 0.45,
            MaxDictationMinutes = 30,
            MaxCallMinutes = 90,
            OverlayLeft = 120.5,
            OverlayTop = 40,
            CallRecordingAcknowledged = true,
            Replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["си шарп"] = "C#",
            },
        };

        AppSettings? restored = JsonSerializer.Deserialize<AppSettings>(Serialize(original), Options);

        Assert.NotNull(restored);
        Assert.Equal(original.ModelFileName, restored.ModelFileName);
        Assert.Equal(original.Language, restored.Language);
        Assert.Equal(original.UiLanguage, restored.UiLanguage);
        Assert.Equal(original.Theme, restored.Theme);
        Assert.Equal(original.Prompt, restored.Prompt);
        Assert.Equal(original.Hotkey, restored.Hotkey);
        Assert.Equal(original.MicrophoneDeviceId, restored.MicrophoneDeviceId);
        Assert.Equal(original.VadThreshold, restored.VadThreshold);
        Assert.Equal(original.MaxDictationMinutes, restored.MaxDictationMinutes);
        Assert.Equal(original.MaxCallMinutes, restored.MaxCallMinutes);
        Assert.Equal(original.OverlayLeft, restored.OverlayLeft);
        Assert.True(restored.CallRecordingAcknowledged);
        Assert.Equal("C#", restored.Replacements["си шарп"]);
    }

    /// <summary>Умолчания должны быть безопасны для человека, не похожего на автора.</summary>
    [Fact]
    public void Defaults_DetectLanguageInsteadOfAssumingOne()
    {
        var settings = new AppSettings();

        Assert.Equal("auto", settings.Language);
        Assert.Equal("auto", settings.OtherSideLanguage);
    }

    [Fact]
    public void Defaults_PointAtAModelThatCanActuallyBeDownloaded()
    {
        var settings = new AppSettings();

        Assert.NotNull(Core.Models.ModelCatalog.Find(settings.ModelFileName));
        Assert.NotNull(Core.Models.ModelCatalog.Find(settings.VadModelFileName));
    }

    [Fact]
    public void Defaults_KeepDictatedTextOutOfTheTrayTooltip() =>
        Assert.False(new AppSettings().ShowTextPreviewInTray);

    [Fact]
    public void EffectiveParagraphPause_IsZeroWhenSplittingIsOff() =>
        Assert.Equal(TimeSpan.Zero, new AppSettings { SplitParagraphsByPauses = false }.EffectiveParagraphPause);

    [Fact]
    public void EffectiveParagraphPause_HasAFloor() =>
        Assert.Equal(
            TimeSpan.FromSeconds(0.2),
            new AppSettings { ParagraphPauseSeconds = 0.01 }.EffectiveParagraphPause);
}

/// <summary>Каталог моделей.</summary>
public class ModelCatalogTests
{
    [Fact]
    public void All_UsesHttpsEverywhere() =>
        Assert.All(
            Core.Models.ModelCatalog.All,
            model => Assert.StartsWith("https://", model.Url, StringComparison.Ordinal));

    [Fact]
    public void All_UrlEndsWithTheFileName() =>
        Assert.All(
            Core.Models.ModelCatalog.All,
            model => Assert.EndsWith(model.FileName, model.Url, StringComparison.Ordinal));

    [Fact]
    public void All_HasNoDuplicateFileNames()
    {
        string[] names = [.. Core.Models.ModelCatalog.All.Select(m => m.FileName)];

        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Модели детектора отличаются от моделей распознавания только именем.
    /// </summary>
    /// <remarks>
    /// Разделение по подстроке «silero» — то, на чём держатся оба списка в
    /// интерфейсе. Каталог обязан этому соответствовать, иначе детектор
    /// появится в выборе модели распознавания и наоборот.
    /// </remarks>
    [Fact]
    public void SpeechDetectorModels_AreRecognisableByName() =>
        Assert.All(
            Core.Models.ModelCatalog.All.Where(m => m.Kind == Core.Models.ModelKind.SpeechDetector),
            model => Assert.Contains("silero", model.FileName, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void RecognitionModels_AreNotConfusedWithDetectors() =>
        Assert.All(
            Core.Models.ModelCatalog.All.Where(m => m.Kind == Core.Models.ModelKind.Recognition),
            model => Assert.DoesNotContain("silero", model.FileName, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void DefaultModels_AreInTheCatalog()
    {
        Assert.Contains(Core.Models.ModelCatalog.DefaultRecognitionModel, Core.Models.ModelCatalog.All);
        Assert.Contains(Core.Models.ModelCatalog.DefaultSpeechDetectorModel, Core.Models.ModelCatalog.All);
    }
}
