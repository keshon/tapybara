using Tapybara.Core.Calls;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Список звонков и память об именах.</summary>
public class CallLibraryTests
{
    // --- разбор имени папки -------------------------------------------------

    [Fact]
    public void ParseFolderName_ReadsStampAndTrigger()
    {
        Assert.True(CallLibrary.TryParseFolderName(
            "2026-09-04 14-30-00 (Zoom)", out DateTimeOffset startedAt, out string? trigger));

        Assert.Equal(new DateTime(2026, 9, 4, 14, 30, 0), startedAt.DateTime);
        Assert.Equal("Zoom", trigger);
    }

    [Fact]
    public void ParseFolderName_AcceptsCallWithoutTrigger()
    {
        Assert.True(CallLibrary.TryParseFolderName(
            "2026-09-04 14-30-00", out DateTimeOffset startedAt, out string? trigger));

        Assert.Equal(new DateTime(2026, 9, 4, 14, 30, 0), startedAt.DateTime);
        Assert.Null(trigger);
    }

    /// <summary>
    /// Посторонняя папка внутри папки звонков не должна становиться звонком.
    /// </summary>
    /// <remarks>
    /// Рядом с записями люди держат свои папки, и список звонков предлагает
    /// «удалить». Принять чужую папку за запись — значит предложить удалить
    /// то, чего мы не создавали.
    /// </remarks>
    [Theory]
    [InlineData("Архив")]
    [InlineData("2026-09-04")]
    [InlineData("2026-13-45 99-99-99")]
    [InlineData("")]
    public void ParseFolderName_RejectsAnythingElse(string folder) =>
        Assert.False(CallLibrary.TryParseFolderName(folder, out _, out _));

    /// <summary>Имя папки, собранное нами, обязано нами же и читаться.</summary>
    [Theory]
    [InlineData("Zoom")]
    [InlineData(null)]
    [InlineData("Microsoft Teams")]
    public void ParseFolderName_RoundTripsWhatWeWrite(string? trigger)
    {
        var startedAt = new DateTimeOffset(2026, 9, 4, 14, 30, 0, TimeSpan.FromHours(3));
        string folder = CallSession.BuildFolderName(startedAt, trigger);

        Assert.True(CallLibrary.TryParseFolderName(folder, out DateTimeOffset parsed, out string? readBack));
        Assert.Equal(startedAt.DateTime, parsed.DateTime);
        Assert.Equal(trigger, readBack);
    }

    // --- список знакомых имён -----------------------------------------------

    [Fact]
    public void Touch_MovesUsedNamesToTheFront()
    {
        IReadOnlyList<string> known = ["Аня", "Борис", "Кирилл"];

        IReadOnlyList<string> updated = KnownParticipants.Touch(known, ["Кирилл"]);

        Assert.Equal(["Кирилл", "Аня", "Борис"], updated);
    }

    [Fact]
    public void Touch_DoesNotResurrectOldSpellingAlongsideTheNewOne()
    {
        IReadOnlyList<string> known = ["кирилл", "Аня"];

        IReadOnlyList<string> updated = KnownParticipants.Touch(known, ["Кирилл"]);

        Assert.Equal(["Кирилл", "Аня"], updated);
    }

    [Fact]
    public void Touch_StopsTheListFromGrowingForever()
    {
        IReadOnlyList<string> known = [.. Enumerable.Range(0, KnownParticipants.MaxRemembered).Select(i => $"Имя{i}")];

        IReadOnlyList<string> updated = KnownParticipants.Touch(known, ["Новый"]);

        Assert.Equal(KnownParticipants.MaxRemembered, updated.Count);
        Assert.Equal("Новый", updated[0]);
    }

    [Fact]
    public void Normalize_ReusesTheSpellingWeAlreadyKnow()
    {
        IReadOnlyList<string> known = ["Кирилл"];

        Assert.Equal("Кирилл", KnownParticipants.Normalize(known, "  кирилл "));
    }

    /// <summary>
    /// Похожее имя — это другое имя.
    /// </summary>
    /// <remarks>
    /// Дополнение по подстроке превратило бы «Аня» в «Таню», и в транскрипте
    /// оказался бы не тот человек.
    /// </remarks>
    [Fact]
    public void Normalize_DoesNotCompleteOneNameIntoAnother()
    {
        IReadOnlyList<string> known = ["Таня"];

        Assert.Equal("Аня", KnownParticipants.Normalize(known, "Аня"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalize_TreatsBlankInputAsNoName(string? raw) =>
        Assert.Null(KnownParticipants.Normalize([], raw));

    [Fact]
    public void Add_IgnoresSomeoneAlreadyChosen()
    {
        IReadOnlyList<string> selected = ["Кирилл"];

        Assert.Equal(selected, KnownParticipants.Add(selected, "кирилл"));
    }
}
