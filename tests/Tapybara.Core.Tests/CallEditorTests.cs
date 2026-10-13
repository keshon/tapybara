using System.Text.Json;
using Tapybara.Core.Calls;
using Tapybara.Core.Editing;
using Tapybara.Core.Speech;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Миграция, ручные блокировки и восстановление сложной правки без потери соседних данных.</summary>
public sealed class CallEditorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tapybara-tests", Guid.NewGuid().ToString("N"));
    public CallEditorTests() => Directory.CreateDirectory(_directory);
    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
    private static CallLine Line(string text, double from = 0, double to = 10, string? voice = "A") =>
        new(CallChannel.Theirs, TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), text, voice) { Id = Guid.NewGuid() };
    private CallEditSession Session(params CallLine[] lines)
    {
        CallTranscriptStore.Save(_directory, new CallTranscript { Lines = lines });
        return new CallEditSession(_directory, CallTranscriptStore.Load(_directory)!);
    }

    [Fact]
    public void BackgroundSuggestionsKeepUndoAndBatchAssignmentsUndoTogether()
    {
        var first = Line("first", 0, 6);
        var second = Line("second", 10, 16);
        var session = Session(first, second);
        Assert.True(session.Execute(t => TranscriptEdit.EditLine(t, first, "edited")));
        var refreshed = session.Document with { Lines = session.Document.Lines
            .Select(l => l with { SuggestedVoice = "B" }).ToList() };
        var saved = CallTranscriptStore.TrySave(_directory, session.Document.Revision, refreshed)!;
        session.Synchronize(saved);
        Assert.True(session.CanUndo);
        Assert.True(session.Execute(t => CallVoices.Reassign(CallVoices.Reassign(t, first, "B"), second, "B")));
        Assert.All(session.Document.Lines, l => Assert.Equal("B", l.Voice));
        Assert.True(session.Undo());
        Assert.All(session.Document.Lines, l => Assert.Equal("A", l.Voice));
        Assert.Equal("edited", session.Document.Lines[0].Text);
        Assert.True(session.Undo());
        Assert.Equal("first", session.Document.Lines[0].Text);
    }

    [Fact]
    public void TimingChangesDiscardPreviousAudioSuggestion()
    {
        var line = Line("speech") with { SuggestedVoice = "B" };
        var doc = new CallTranscript { Lines = [line] };
        Assert.Null(CallDocumentCommands.SetTiming(doc, line.Id, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9))
            .Lines[0].SuggestedVoice);
    }

    [Fact]
    public void RenamingPersonAcrossMultipleVoicesPreservesCorrectionsAndUndoesTheName()
    {
        CallMeta.Save(new CallSession { Directory = _directory, StartedAt = DateTimeOffset.UtcNow,
            VoiceNames = new Dictionary<string, string> { ["A"] = "Кирилл", ["B"] = "Кирилл", ["C"] = "Константин" } });
        var first = Line("first", 0, 6); var second = Line("second", 10, 16, "B");
        var corrected = Line("corrected", 20, 26, "C") with { HumanEdits = CallLineEdits.Speaker };
        var session = Session(first, second, corrected);
        Assert.True(session.Execute(t => t, s => s with
        {
            VoiceNames = s.VoiceNames.ToDictionary(p => p.Key, p => p.Value == "Кирилл" ? "Кирилл Иванов" : p.Value),
        }));
        Assert.Equal(new[] { first, second, corrected }, session.Document.Lines);
        Assert.Equal("Константин", CallMeta.Load(_directory)!.VoiceNames["C"]);
        Assert.Equal("Кирилл Иванов", CallMeta.Load(_directory)!.VoiceNames["B"]);
        Assert.True(session.Undo());
        Assert.Equal("Кирилл", CallMeta.Load(_directory)!.VoiceNames["A"]);
        Assert.Equal(corrected, session.Document.Lines[2]);
    }

    [Fact]
    public void LegacyMigrationKeepsBytesAndPersistsUniqueStableIds()
    {
        string path = Path.Combine(_directory, CallSession.TranscriptDataFileName);
        string raw = JsonSerializer.Serialize(new CallTranscript { Version = 1, Lines = [Line("one") with { Id = Guid.Empty }, Line("two") with { Id = Guid.Empty }] });
        File.WriteAllText(path, raw);
        CallTranscript migrated = CallTranscriptStore.Load(_directory)!;
        Assert.Equal(2, migrated.Version);
        Assert.Equal(2, migrated.Lines.Select(l => l.Id).Distinct().Count());
        Assert.All(migrated.Lines, l => Assert.NotEqual(Guid.Empty, l.Id));
        Assert.Equal(raw, File.ReadAllText(path + ".previous"));
        Assert.Equal(migrated.Lines, CallTranscriptStore.Load(_directory)!.Lines);
        Assert.Equal(migrated.Lines, migrated.OriginalLines);
    }

    [Fact]
    public void OldEditedTranscriptDoesNotPretendItIsOriginalRecognition()
    {
        string path = Path.Combine(_directory, CallSession.TranscriptDataFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(new CallTranscript { Version = 1, EditedByHand = true, Lines = [Line("corrected")] }));
        CallTranscript loaded = CallTranscriptStore.Load(_directory)!;
        Assert.True(loaded.OriginalWasEdited);
        Assert.All(loaded.Lines, l => Assert.True(l.HumanEdits.HasFlag(CallLineEdits.Speaker)));
    }

    [Fact]
    public void FutureSchemaCannotBeOverwritten()
    {
        string path = Path.Combine(_directory, CallSession.TranscriptDataFileName);
        File.WriteAllText(path, "{\"Version\":999,\"Lines\":[]}");
        Assert.Null(CallTranscriptStore.Load(_directory));
        Assert.Throws<InvalidDataException>(() => CallTranscriptStore.Save(_directory, new CallTranscript()));
        Assert.Contains("999", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void InterruptionRepairUndoRedoRestoresAllFieldsAndNeighbors()
    {
        CallLine original = Line("Pavel speaks Anna interrupts") with { Fit = 0.87 };
        CallLine neighbor = Line("unrelated", 15, 18, "C");
        CallEditSession session = Session(original, neighbor);
        Assert.True(session.Execute(t => CallDocumentCommands.Split(t, original.Id, 13)));
        CallLine second = session.Document.Lines[1];
        Assert.True(session.Execute(t => CallVoices.Reassign(t, second, "B")));
        Assert.True(session.Document.Lines[1].HumanEdits.HasFlag(CallLineEdits.Speaker));
        Assert.True(session.Undo());
        Assert.Equal("A", session.Document.Lines[1].Voice);
        Assert.True(session.Undo());
        Assert.Equal([original, neighbor], session.Document.Lines);
        Assert.False(session.Document.EditedByHand);
        Assert.Equal([original, neighbor], session.Document.OriginalLines);
        Assert.True(session.Redo());
        Assert.True(session.Redo());
        Assert.Equal(second.Id, session.Document.Lines[1].Id);
        Assert.Equal("B", session.Document.Lines[1].Voice);
        Assert.Equal(neighbor, session.Document.Lines[2]);
        Assert.Equal(session.Document.Lines, CallTranscriptStore.Load(_directory)!.Lines);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(7)]
    public void SplitRejectsEdgesAndInsideWords(int caret)
    {
        CallLine line = Line("one two");
        Assert.False(CallDocumentCommands.CanSplit(line.Text, caret));
        Assert.Throws<ArgumentException>(() => CallDocumentCommands.Split(new CallTranscript { Lines = [line] }, line.Id, caret));
    }

    [Fact]
    public void SplitMarksEstimatedTimingAndRetainsSpeakerAndSource()
    {
        CallLine line = Line("Привет   Анна", 2, 12);
        CallTranscript split = CallDocumentCommands.Split(new CallTranscript { Lines = [line] }, line.Id, 8);
        Assert.Equal(["Привет", "Анна"], split.Lines.Select(l => l.Text));
        Assert.Equal(line.Id, split.Lines[0].Id);
        Assert.NotEqual(line.Id, split.Lines[1].Id);
        Assert.Equal(line.Id, split.Lines[1].OriginId);
        Assert.All(split.Lines, l => { Assert.True(l.TimingEstimated); Assert.Equal("A", l.Voice); Assert.Equal(line.Channel, l.Channel); });
        Assert.Equal(split.Lines[0].End, split.Lines[1].Start);
    }

    [Fact]
    public void SelectionAssignmentUsesExplicitTimingAndOneUndoTransaction()
    {
        CallLine line = Line("first middle last");
        CallEditSession session = Session(line);
        Assert.True(session.Execute(t => CallDocumentCommands.AssignSelection(t, line.Id, 6, 6, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(7), "B")));
        Assert.Equal(["first", "middle", "last"], session.Document.Lines.Select(l => l.Text));
        Assert.Equal("B", session.Document.Lines[1].Voice);
        Assert.False(session.Document.Lines[1].TimingEstimated);
        Assert.True(session.Undo());
        Assert.Equal([line], session.Document.Lines);
    }

    [Fact]
    public void IdentitySurvivesTimingChangesAndDistinguishesOverlap()
    {
        CallLine a = Line("one");
        CallLine b = Line("two");
        CallEditSession session = Session(a, b);
        Assert.False(a.IsSameAs(b));
        Assert.True(session.Execute(t => CallDocumentCommands.SetTiming(t, a.Id, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8))));
        Assert.True(session.Execute(t => TranscriptEdit.EditLine(t, a, "edited")));
        Assert.Equal("two", session.Document.Lines.Single(l => l.Id == b.Id).Text);
        Assert.Equal(TimeSpan.FromSeconds(1), session.Document.Lines.Single(l => l.Id == a.Id).Start);
    }

    [Fact]
    public void StaleAnalysisAndStaleUndoCannotOverwriteNewerHumanEdit()
    {
        CallLine line = Line("original");
        CallEditSession first = Session(line);
        var second = new CallEditSession(_directory, first.Document);
        CallTranscript stale = first.Document;
        Assert.True(first.Execute(t => TranscriptEdit.EditLine(t, line, "first edit")));
        Assert.Null(CallTranscriptStore.TrySave(_directory, stale.Revision, stale with { Lines = [] }));
        second.Synchronize(first.Document);
        Assert.True(second.Execute(t => TranscriptEdit.EditLine(t, line, "second edit")));
        Assert.False(first.Undo());
        Assert.Equal("second edit", CallTranscriptStore.Load(_directory)!.Lines[0].Text);
    }

    [Fact]
    public void HumanSpeakerLockSurvivesVerificationAndResplit()
    {
        CallLine line = Line("human assignment") with { HumanEdits = CallLineEdits.Speaker };
        CallLine other = Line("machine assignment", 10, 20, "B");
        IReadOnlyList<CallLine> split = CallVoices.Assign([line, other], [new SpeakerSpan(TimeSpan.Zero, TimeSpan.FromSeconds(20), 0)]);
        Assert.Equal(line, split[0]);
        Assert.NotEqual("A", split[1].Voice);
        CallTranscript verified = VoiceCheck.Check(new CallTranscript { Lines = split }, _ => [1f, 0f]);
        Assert.Equal(line, verified.Lines[0]);
    }

    [Fact]
    public void MissingSpeechAndOverlapSurviveSaveAndMergeUndo()
    {
        CallLine line = Line("one", 0, 5);
        CallEditSession session = Session(line);
        Assert.True(session.Execute(t => CallDocumentCommands.Insert(t, line.Channel, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8), "two", "A")));
        Guid second = session.Document.Lines[1].Id;
        Assert.True(session.Execute(t => CallDocumentCommands.Merge(t, line.Id, second)));
        Assert.Equal("one two", session.Document.Lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(8), session.Document.Lines[0].End);
        Assert.True(session.Undo());
        Assert.Equal(second, session.Document.Lines[1].Id);
        Assert.Equal(TimeSpan.FromSeconds(3), session.Document.Lines[1].Start);
    }

    [Fact]
    public void NewEditAfterUndoClearsRedoAndDoesNotChangeRecognitionBaseline()
    {
        CallLine line = Line("original");
        CallEditSession session = Session(line);
        Assert.True(session.Execute(t => TranscriptEdit.EditLine(t, line, "edit one")));
        Assert.True(session.Undo());
        Assert.True(session.Execute(t => TranscriptEdit.EditLine(t, line, "edit two")));
        Assert.False(session.CanRedo);
        Assert.Equal("original", session.Document.OriginalLines![0].Text);
    }

    [Fact]
    public void FailedStorageRestoreKeepsHistoryAvailableForRetry()
    {
        var history = new DocumentHistory<string>(2);
        history.Record("a", "b");
        Assert.False(history.Undo(_ => false));
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.True(history.Undo(s => s == "a"));
        Assert.True(history.Redo(s => s == "b"));
    }

    [Fact]
    public void UndoRestoresAssignmentNamesAndKeepsUnrelatedMetadataEdits()
    {
        CallMeta.Save(new CallSession { Directory = _directory, StartedAt = DateTimeOffset.UtcNow });
        CallLine line = Line("first middle last", voice: null);
        CallEditSession session = Session(line);
        Assert.True(session.Execute(t => CallDocumentCommands.AssignSelection(t, line.Id, 6, 6,
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(7), "A"), meta => meta with
            {
                VoiceNames = new Dictionary<string, string>(meta.VoiceNames) { ["A"] = CallSession.Me },
            }));
        CallMeta.Update(_directory, meta => meta with { Title = "renamed later", VoiceNames = new Dictionary<string, string>(meta.VoiceNames) { ["Z"] = "unrelated" } });
        Assert.True(session.Undo());
        Assert.False(CallMeta.Load(_directory)!.VoiceNames.ContainsKey("A"));
        Assert.Equal("unrelated", CallMeta.Load(_directory)!.VoiceNames["Z"]);
        Assert.Equal("renamed later", CallMeta.Load(_directory)!.Title);
        Assert.True(session.Redo());
        Assert.Equal(CallSession.Me, CallMeta.Load(_directory)!.VoiceNames["A"]);
        Assert.Equal("unrelated", CallMeta.Load(_directory)!.VoiceNames["Z"]);
        Assert.True(session.Undo());
        Assert.True(session.Execute(t => CallDocumentCommands.AssignSelection(t, line.Id, 6, 6,
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(7), "A")));
        Assert.NotEqual(CallSession.Me, CallSpeakers.Label(session.Document.Lines[1], CallMeta.Load(_directory)!, 1, "Me", "Someone", "Voice"));
    }
    [Fact]
    public void BoundaryAdjustmentIsOneUndoableEditAndDoesNotRippleOverlapOrChangeText()
    {
        CallLine first = Line("first", from: 0, to: 4) with { TimingEstimated = true };
        CallLine second = Line("second", from: 3, to: 6);
        CallEditSession session = Session(first, second);
        Assert.True(session.Execute(t => CallDocumentCommands.SetTiming(t, first.Id, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5))));
        CallLine adjusted = session.Document.Lines.Single(l => l.Id == first.Id);
        Assert.Equal("first", adjusted.Text);
        Assert.Equal(first.Channel, adjusted.Channel);
        Assert.False(adjusted.TimingEstimated);
        Assert.True(adjusted.HumanEdits.HasFlag(CallLineEdits.Timing));
        Assert.Equal(second, session.Document.Lines.Single(l => l.Id == second.Id));
        Assert.True(session.Undo());
        Assert.Equal(first, session.Document.Lines.Single(l => l.Id == first.Id));
        Assert.True(session.Redo());
        Assert.Equal(adjusted, session.Document.Lines.Single(l => l.Id == first.Id));
    }
}
