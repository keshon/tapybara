using System.Text.Json;
using Tapybara.Core.Calls;
using Tapybara.Core.Export;
using Xunit;

namespace Tapybara.Core.Tests;

public sealed class CallDeliveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tapybara-tests", Guid.NewGuid().ToString("N"));
    public CallDeliveryTests() => Directory.CreateDirectory(_root);
    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
    private CallSession Session => new() { Directory = Path.Combine(_root, "call"), StartedAt = DateTimeOffset.Now,
        Participants = ["Анна"], Voices = ["A"] };
    private static CallLine Line(string text) => new(CallChannel.Theirs, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), text, "A")
    { Id = Guid.NewGuid() };
    private static string Render(CallSession session, CallTranscript doc, TranscriptExportOptions options) =>
        CallExport.Render(session, doc, options, "Павел", "Собеседник", "Голос");

    [Fact]
    public void JsonContainsCurrentTextWithoutRecognitionBaselineOrPrivateMetadata()
    {
        var doc = new CallTranscript { Revision = 9, Lines = [Line("Правка\nвторая строка")], OriginalLines = [Line("secret baseline")] };
        string output = Render(Session, doc, new() { Format = TranscriptExportFormat.Json });
        using JsonDocument json = JsonDocument.Parse(output);
        Assert.Equal("tapybara.transcript-export", json.RootElement.GetProperty("Schema").GetString());
        Assert.Equal(9, json.RootElement.GetProperty("Revision").GetInt64());
        Assert.Equal("Правка\nвторая строка", json.RootElement.GetProperty("Lines")[0].GetProperty("Text").GetString());
        Assert.DoesNotContain("secret baseline", output, StringComparison.Ordinal);
        Assert.DoesNotContain(Session.Directory, output, StringComparison.Ordinal);
        Assert.DoesNotContain("OriginalLines", output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonHonorsOmittedLabelsAndTimestamps()
    {
        using JsonDocument json = JsonDocument.Parse(Render(Session, new() { Lines = [Line("text")] },
            new() { Format = TranscriptExportFormat.Json, Labels = TranscriptSpeakerLabels.None, Timestamps = false }));
        Assert.Equal(new[] { "Text" }, json.RootElement.GetProperty("Lines")[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void SelectionExportsExactCharactersAndMarksWholeSegmentTimingEstimated()
    {
        CallLine line = Line("before selected after");
        using JsonDocument json = JsonDocument.Parse(Render(Session, new() { Lines = [line, Line("other")] },
            new() { Format = TranscriptExportFormat.Json, Selection = [new(line.Id, 7, 8)] }));
        JsonElement lines = json.RootElement.GetProperty("Lines");
        Assert.Equal(1, lines.GetArrayLength());
        Assert.Equal("selected", lines[0].GetProperty("Text").GetString());
        Assert.True(lines[0].GetProperty("Estimated").GetBoolean());
        Assert.Equal("00:00:04", lines[0].GetProperty("Start").GetString());
    }

    [Fact]
    public void InvalidSelectionCannotSilentlyExportAnotherRange()
    {
        CallLine line = Line("short");
        Assert.Throws<ArgumentOutOfRangeException>(() => Render(Session, new() { Lines = [line] },
            new() { Selection = [new(line.Id, 4, 2)] }));
    }

    [Fact]
    public void AliasesReplaceSpeakerAndKnownMentionWithoutChangingSource()
    {
        var doc = new CallTranscript { Lines = [Line("Анна, привет")] };
        string output = Render(Session, doc, new() { Labels = TranscriptSpeakerLabels.Aliases,
            Aliases = new Dictionary<string, string> { ["анна"] = "Participant 1" } });
        Assert.DoesNotContain("Анна", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Participant 1", output, StringComparison.Ordinal);
        Assert.Equal("Анна, привет", doc.Lines[0].Text);
    }

    [Fact]
    public void AliasInventoryIncludesMentionedOwnerAndMetadataOnlyPeople()
    {
        CallSession session = Session with { Participants = ["Анна", "Кирилл"], VoiceNames = new Dictionary<string, string> { ["A"] = "Анна" } };
        var doc = new CallTranscript { Lines = [Line("Павел и Кирилл слушают Анну")] };
        IReadOnlyList<string> names = CallExport.AliasNames(session, doc, [], "Павел", "Собеседник", "Голос");
        Assert.Contains("Павел", names);
        Assert.Contains("Кирилл", names);
        string output = Render(session, doc, new() { Labels = TranscriptSpeakerLabels.Aliases,
            Aliases = names.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => $"Person {p.i + 1}") });
        Assert.DoesNotContain("Павел", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Кирилл", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateAliasesCannotCollapseDifferentIdentities()
    {
        var doc = new CallTranscript { Lines = [Line("text")] };
        Assert.Throws<ArgumentException>(() => Render(Session, doc, new() { Labels = TranscriptSpeakerLabels.Aliases,
            Aliases = new Dictionary<string, string> { ["Анна"] = "Person", ["Павел"] = "Person" } }));
    }

    [Fact]
    public void PreviewSelectionRetainsFullUnnamedSpeakerContext()
    {
        CallSession session = Session with { Participants = [], Voices = ["A", "B"] };
        CallLine first = Line("first");
        var doc = new CallTranscript { Lines = [first, Line("second") with { Voice = "B" }] };
        IReadOnlyList<string> names = CallExport.AliasNames(session, doc, [], "Павел", "Собеседник", "Голос");
        string output = Render(session, doc, new() { Labels = TranscriptSpeakerLabels.Aliases,
            Aliases = names.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => $"Person {p.i + 1}"),
            Selection = [new(first.Id, 0, first.Text.Length)] });
        Assert.Contains("first", output, StringComparison.Ordinal);
        Assert.DoesNotContain("second", output, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownEscapesTranscriptFormatting()
    {
        string output = Render(Session, new() { Lines = [Line("**literal** [link] <tag>")] },
            new() { Format = TranscriptExportFormat.Markdown, Timestamps = false });
        Assert.Contains("\\*\\*literal\\*\\* \\[link\\] \\<tag\\>", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("transcript.json")]
    [InlineData("TRANSCRIPT.MD")]
    [InlineData("meta.json.previous")]
    [InlineData("mic.wav")]
    [InlineData("note.md")]
    public void ExportCannotOverwriteRecordingData(string filename) =>
        Assert.Throws<ArgumentException>(() => CallExport.ValidateDestination(Session, Path.Combine(Session.Directory, filename)));

    [Fact]
    public void ExportAllowsIndependentCopy() => CallExport.ValidateDestination(Session, Path.Combine(_root, "transcript.json"));

    [Fact]
    public void IndexSearchesEditedTextCachesUnchangedCallsAndRemovesDeletedCalls()
    {
        CallSession session = Session;
        Directory.CreateDirectory(session.Directory);
        CallMeta.Save(session);
        CallTranscriptStore.Save(session.Directory, new() { Lines = [Line("old phrase")] });
        var index = new CallLibraryIndex();
        CallLibraryItem first = Assert.Single(index.Scan(_root));
        Assert.Same(first, Assert.Single(index.Scan(_root)));
        CallTranscriptStore.Save(session.Directory, new() { Lines = [Line("new edited phrase")] });
        CallLibraryItem changed = Assert.Single(index.Scan(_root));
        Assert.True(new CallLibraryFilter("NEW EDITED").Matches(changed));
        Assert.False(new CallLibraryFilter("old phrase").Matches(changed));
        Directory.Delete(session.Directory, recursive: true);
        Assert.Empty(index.Scan(_root));
    }

    [Fact]
    public void FilterCombinesPersonStateQueryAndInclusiveLocalDate()
    {
        CallSession session = Session with { StartedAt = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(3)) };
        var item = new CallLibraryItem(new CallEntry { Session = session, State = CallState.Ready }, "searched text");
        DateOnly date = DateOnly.FromDateTime(session.StartedAt.LocalDateTime);
        Assert.True(new CallLibraryFilter("searched", "анна", CallState.Ready, date, date).Matches(item));
        Assert.False(new CallLibraryFilter(Person: "Павел").Matches(item));
        Assert.False(new CallLibraryFilter(State: CallState.Transcribing).Matches(item));
        Assert.False(new CallLibraryFilter(From: date.AddDays(1)).Matches(item));
    }

    [Fact]
    public void ScanHonorsCancellation() => Assert.Throws<OperationCanceledException>(() =>
        new CallLibraryIndex().Scan(_root, new CancellationToken(canceled: true)));
}
