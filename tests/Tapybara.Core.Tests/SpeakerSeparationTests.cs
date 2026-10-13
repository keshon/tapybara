using Tapybara.Core.Calls;
using Tapybara.Core.Speech;
using Xunit;

namespace Tapybara.Core.Tests;

public sealed class SpeakerSeparationTests
{
    private static CallLine Line(int n, string? voice = null) => new(CallChannel.Theirs, TimeSpan.FromSeconds(n * 10), TimeSpan.FromSeconds(n * 10 + 6), "speech " + n, voice) { Id = Guid.NewGuid() };
    private static SpeakerSpan Span(int n, int speaker) => new(TimeSpan.FromSeconds(n * 10), TimeSpan.FromSeconds(n * 10 + 6), speaker);

    [Fact]
    public void FragmentedAutomaticResultDoesNotPublishGroupsOrOverwriteHumanCorrections()
    {
        var lines = Enumerable.Range(0, 60).Select(n => Line(n, "old")).ToList();
        lines[0] = lines[0] with { Voice = "confirmed", HumanEdits = CallLineEdits.Speaker };
        var doc = new CallTranscript { Lines = lines, OriginalLines = lines };
        var result = SpeakerSeparation.Apply(doc, Enumerable.Range(0, 60).Select(n => Span(n, n)).ToList(), 0);
        Assert.True(SpeakerSeparation.NeedsCount(result));
        Assert.False(result.VoicesSplit);
        Assert.Equal(lines[0], result.Lines[0]);
        Assert.All(result.Lines.Skip(1), l => Assert.Null(l.Voice));
        Assert.Equal(lines.Select(l => (l.Id, l.Text, l.Start, l.End)), result.Lines.Select(l => (l.Id, l.Text, l.Start, l.End)));
        Assert.Equal(doc.OriginalLines, result.OriginalLines);
    }

    [Fact]
    public void ExplicitCountIsNotSilentlyCappedAtAutomaticSafetyBoundary()
    {
        var doc = new CallTranscript { Lines = Enumerable.Range(0, 16).Select(n => Line(n)).ToList() };
        var result = SpeakerSeparation.Apply(doc, Enumerable.Range(0, 16).Select(n => Span(n, n)).ToList(), 16);
        Assert.False(SpeakerSeparation.NeedsCount(result));
        Assert.Equal(16, result.Voices.Count);
    }

    [Fact]
    public void RetryReusesConfirmedIdentityForOtherTurnsAndPreservesTextAndNames()
    {
        var confirmed = Line(0, "Z") with { HumanEdits = CallLineEdits.Speaker };
        var doc = new CallTranscript { Lines = [confirmed, Line(1, "wrong"), Line(2, "X")] };
        var result = SpeakerSeparation.Apply(doc, [Span(0, 3), Span(1, 3), Span(2, 5)], 2);
        Assert.Equal(confirmed, result.Lines[0]);
        Assert.Equal("Z", result.Lines[1].Voice);
        Assert.NotEqual("Z", result.Lines[2].Voice);
        Assert.Equal(doc.Lines.Select(l => l.Text), result.Lines.Select(l => l.Text));
    }

    [Fact]
    public void ConflictingConfirmedPeopleInOneClusterDoNotSpreadEitherIdentity()
    {
        var doc = new CallTranscript { Lines = [Line(0, "A") with { HumanEdits = CallLineEdits.Speaker },
            Line(1, "B") with { HumanEdits = CallLineEdits.Speaker }, Line(2)] };
        var result = SpeakerSeparation.Apply(doc, [Span(0, 1), Span(1, 1), Span(2, 1)], 1);
        Assert.NotEqual("A", result.Lines[2].Voice);
        Assert.NotEqual("B", result.Lines[2].Voice);
        Assert.Equal(doc.Lines.Take(2), result.Lines.Take(2));
    }

    [Fact]
    public void RetryRefreshesStaleMixedFlagsAndRetainsTheConfirmedSpeaker()
    {
        var confirmed = Line(0, "Z") with { HumanEdits = CallLineEdits.Speaker, SpeechMix = SpeechMix.Overlap };
        var result = SpeakerSeparation.Apply(new CallTranscript { Lines = [confirmed, Line(1)] }, [Span(0, 2), Span(1, 2)], 1);
        Assert.Equal("Z", result.Lines[0].Voice);
        Assert.Equal(SpeechMix.None, result.Lines[0].SpeechMix);
        Assert.Equal("Z", result.Lines[1].Voice);
    }

    [Fact]
    public void SameCallSimilarGroupsSuggestKnownNameWithoutChangingAnyAssignments()
    {
        var doc = new CallTranscript { Lines = [Line(0, "A") with { HumanEdits = CallLineEdits.Speaker }, Line(1, "B")],
            VoicePrints = new Dictionary<string, float[]> { ["A"] = [1, 0], ["B"] = [1, 0] } };
        var session = new CallSession { Directory = "unused", StartedAt = DateTimeOffset.Now, VoiceNames = new Dictionary<string, string> { ["A"] = "Kirill" } };
        Assert.Equal("Kirill", SpeakerNameSuggestions.Find(session, doc)["B"].Name);
        Assert.Equal("B", doc.Lines[1].Voice);
        var named = session with { VoiceNames = new Dictionary<string, string> { ["A"] = "Kirill", ["B"] = "Kirill" } };
        Assert.Single(CallPeople.Of(named, doc), p => !p.IsMe);
    }

    [Fact]
    public void AmbiguousSimilarityDoesNotSuggestAName()
    {
        var doc = new CallTranscript { Lines = [Line(0, "A") with { HumanEdits = CallLineEdits.Speaker }, Line(1, "B") with { HumanEdits = CallLineEdits.Speaker }, Line(2, "C")],
            VoicePrints = new Dictionary<string, float[]> { ["A"] = [1, 0], ["B"] = [1, 0], ["C"] = [1, 0] } };
        var session = new CallSession { Directory = "unused", StartedAt = DateTimeOffset.Now, VoiceNames = new Dictionary<string, string> { ["A"] = "Kirill", ["B"] = "Victor" } };
        Assert.Empty(SpeakerNameSuggestions.Find(session, doc));
    }
}
