using Tapybara.Core.Calls;
using Tapybara.Core.Settings;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Перенос словаря между машинами: выгрузить, прочитать, влить.</summary>
public sealed class DictionaryTransferTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tapybara-dictionary-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static PersonVoice Voice(params float[][] prints) => new() { Prints = prints, Updated = Now };

    [Fact]
    public void RoundTrip_KeepsEverything()
    {
        var settings = new AppSettings
        {
            Replacements = new() { ["ugel"] = "юджайл", ["U-Gile"] = "юджайл" },
            Prompt = "Юджайл, Tapybara.",
            KnownParticipants = ["Кирилл", "Анна"],
        };
        var voices = new Dictionary<string, PersonVoice> { ["Кирилл"] = Voice([1f, 0f]) };

        string json = DictionaryTransfer.Serialize(DictionaryTransfer.Export(settings, voices, Now));
        DictionaryFile file = DictionaryTransfer.Parse(json);

        Assert.Equal(settings.Replacements, file.Replacements);
        Assert.Equal("Юджайл, Tapybara.", file.Prompt);
        Assert.Equal(["Кирилл", "Анна"], file.People);
        Assert.Equal(settings.VoiceEmbeddingModelFileName, file.VoiceModel);
        Assert.Equal([1f, 0f], file.Voices["Кирилл"].Prints[0]);
        Assert.Contains("юджайл", json, StringComparison.Ordinal); // кириллица читаемой, не й
    }

    [Fact]
    public void Export_LeavesVoicesOut_WhenRememberingIsOff()
    {
        var settings = new AppSettings { RememberVoices = false };
        var voices = new Dictionary<string, PersonVoice> { ["Кирилл"] = Voice([1f, 0f]) };

        DictionaryFile file = DictionaryTransfer.Export(settings, voices, Now);

        Assert.Empty(file.Voices);
        Assert.Null(file.VoiceModel);
    }

    [Theory]
    [InlineData("{\"Replacements\":{\"a\":\"b\"}}")]
    [InlineData("{\"Format\":\"something-else\"}")]
    [InlineData("not json at all")]
    [InlineData("null")]
    public void Parse_RejectsForeignFiles(string json)
    {
        Assert.Throws<InvalidDataException>(() => DictionaryTransfer.Parse(json));
    }

    [Fact]
    public void Merge_AddsNew_FileWinsOnConflict_AndKeepsLocalOnes()
    {
        var current = new AppSettings
        {
            Replacements = new() { ["ugel"] = "юджил", ["local"] = "своё" },
            KnownParticipants = ["Витя"],
        };
        var file = new DictionaryFile
        {
            Replacements = new() { ["ugel"] = "юджайл", ["ugl"] = "юджайл", ["  "] = "x", ["y"] = " " },
            People = ["Кирилл", "витя"],
        };

        DictionaryMerge merge = DictionaryTransfer.Merge(current, file);

        Assert.Equal(1, merge.ReplacementsAdded);
        Assert.Equal(1, merge.ReplacementsChanged);
        Assert.Equal("юджайл", merge.Settings.Replacements["ugel"]);
        Assert.Equal("своё", merge.Settings.Replacements["local"]);
        Assert.Equal(3, merge.Settings.Replacements.Count);

        // Свои имена впереди, дубликат с другим регистром не добавляется.
        Assert.Equal(["Витя", "Кирилл"], merge.Settings.KnownParticipants);
        Assert.Equal(1, merge.PeopleAdded);
    }

    [Fact]
    public void Merge_TakesPrompt_OnlyInPlaceOfDefaultOrNothing()
    {
        var file = new DictionaryFile { Prompt = "Юджайл." };

        Assert.True(DictionaryTransfer.Merge(new AppSettings { Prompt = null }, file).PromptTaken);

        var withDefault = new AppSettings { Language = "ru", Prompt = LanguageDefaults.DefaultPrompt("ru") };
        Assert.True(DictionaryTransfer.Merge(withDefault, file).PromptTaken);

        DictionaryMerge own = DictionaryTransfer.Merge(new AppSettings { Prompt = "Своя подсказка." }, file);
        Assert.False(own.PromptTaken);
        Assert.Equal("Своя подсказка.", own.Settings.Prompt);
    }

    [Fact]
    public void Merge_RespectsTheLimitOnPeople()
    {
        var current = new AppSettings
        {
            KnownParticipants = [.. Enumerable.Range(0, KnownParticipants.MaxRemembered - 1).Select(i => $"p{i}")],
        };
        var file = new DictionaryFile { People = ["new1", "new2", "new3"] };

        DictionaryMerge merge = DictionaryTransfer.Merge(current, file);

        Assert.Equal(KnownParticipants.MaxRemembered, merge.Settings.KnownParticipants.Count);
        Assert.Equal(1, merge.PeopleAdded);
    }

    [Fact]
    public void VoicesFit_OnlyForTheSameModel()
    {
        var settings = new AppSettings();
        var file = new DictionaryFile
        {
            VoiceModel = settings.VoiceEmbeddingModelFileName,
            Voices = new() { ["Кирилл"] = Voice([1f, 0f]) },
        };

        Assert.True(DictionaryTransfer.VoicesFit(file, settings));
        Assert.False(DictionaryTransfer.VoicesFit(file with { VoiceModel = "other.onnx" }, settings));
        Assert.False(DictionaryTransfer.VoicesFit(file, settings with { RememberVoices = false }));
    }

    [Fact]
    public void VoiceBookImport_AddsPrints_WithoutDuplicates()
    {
        var book = new VoiceBook(Path.Combine(_root, "voices.json"));
        book.Learn("Кирилл", [1f, 0f, 0f]);

        int touched = book.Import(new Dictionary<string, PersonVoice>
        {
            ["кирилл"] = Voice([1f, 0f, 0f], [0f, 1f, 0f]),
            ["Анна"] = Voice([0f, 0f, 1f]),
        });

        Assert.Equal(2, touched);
        Assert.Equal(2, book.PrintsOf("Кирилл"));
        Assert.Equal(1, book.PrintsOf("Анна"));

        // Повторная загрузка того же файла ничего не меняет.
        Assert.Equal(0, book.Import(new Dictionary<string, PersonVoice> { ["Анна"] = Voice([0f, 0f, 1f]) }));

        // И переживает перезапуск.
        Assert.Equal(2, new VoiceBook(Path.Combine(_root, "voices.json")).PrintsOf("Кирилл"));
    }
}
