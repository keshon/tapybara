using Tapybara.Core.Calls;
using Xunit;

namespace Tapybara.Core.Tests;

public sealed class SpeakerCorrectionTests
{
    private static CallLine Line(int at, string voice, bool confirmed = false, int seconds = 6) =>
        new(CallChannel.Theirs, TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at + seconds), "speech", voice)
        { Id = Guid.NewGuid(), HumanEdits = confirmed ? CallLineEdits.Speaker : CallLineEdits.None };

    [Fact]
    public void ReviewStartsWithDoubtfulUsablePhrasesAndExcludesShortOrOverlappingExamples()
    {
        var clear = Line(0, "A", true);
        var doubtful = Line(20, "A") with { Fit = .3 };
        var shortLine = Line(40, "B", seconds: 2);
        var overlapping = Line(60, "B");
        var other = Line(61, "C");
        var document = new CallTranscript { Lines = [clear, doubtful, shortLine, overlapping, other] };
        Assert.Equal(new[] { doubtful, clear }, SpeakerCorrection.Examples(document));
        var confirmed = SpeakerCorrection.Confirm(document, doubtful, "B");
        Assert.Equal("B", confirmed.Lines[1].Voice);
        Assert.True(confirmed.Lines[1].HumanEdits.HasFlag(CallLineEdits.Speaker));
        Assert.Null(confirmed.Lines[1].Fit);
        Assert.Throws<InvalidOperationException>(() => SpeakerCorrection.Confirm(confirmed, doubtful, "C"));
        Assert.Throws<InvalidOperationException>(() => SpeakerCorrection.Confirm(document, overlapping, "C"));
    }

    [Fact]
    public void UsesConfirmedExamplesRatherThanMixedClusterAndPreservesManualAssignments()
    {
        var seed = Line(0, "B", true);
        var rival = Line(10, "A", true);
        var wrong = Line(20, "A");
        var right = Line(30, "A");
        var locked = Line(40, "A", true);
        var shortLine = Line(50, "A", seconds: 2);
        var transcript = new CallTranscript { Lines = [seed, rival, wrong, right, locked, shortLine] };
        float[]? Print(CallLine line) => line == seed || line == wrong ? [1, 0] : [0, 1];
        var candidates = SpeakerCorrection.Find(transcript, seed, Print);
        var candidate = Assert.Single(candidates);
        Assert.Equal(wrong, candidate.Line);
        Assert.True(candidate.Clear);
        var changed = SpeakerCorrection.Apply(transcript, seed, [wrong]);
        Assert.Equal("B", changed.Lines[2].Voice);
        Assert.Null(changed.Lines[2].Fit);
        Assert.True(changed.Lines[2].HumanEdits.HasFlag(CallLineEdits.Speaker));
        Assert.Equal(locked, changed.Lines[4]);
        Assert.Equal(transcript.Lines.Select(l => l.Text), changed.Lines.Select(l => l.Text));
    }

    [Fact]
    public void WithoutCompetingConfirmedVoiceNothingIsPreselected()
    {
        var seed = Line(0, "B", true);
        var wrong = Line(20, "A");
        var candidates = SpeakerCorrection.Find(new CallTranscript { Lines = [seed, wrong] }, seed, _ => [1, 0]);
        Assert.False(Assert.Single(candidates).Clear);
    }

    [Fact]
    public void SkipsOverlappingPhrasesAndSupportsCancellation()
    {
        var seed = Line(0, "B", true);
        var a = Line(20, "A");
        var b = Line(21, "C");
        var transcript = new CallTranscript { Lines = [seed, a, b] };
        Assert.Empty(SpeakerCorrection.Find(transcript, seed, _ => [1, 0]));
        Assert.Throws<OperationCanceledException>(() => SpeakerCorrection.Find(transcript, seed,
            _ => [1, 0], new CancellationToken(true)));
    }

    [Fact]
    public void RejectsStaleProposalsAndProtectedAssignments()
    {
        var seed = Line(0, "B", true);
        var wrong = Line(20, "A");
        var transcript = new CallTranscript { Lines = [seed, wrong with { Text = "edited" }] };
        Assert.Throws<InvalidOperationException>(() => SpeakerCorrection.Apply(transcript, seed, [wrong]));
        var locked = Line(30, "A", true);
        Assert.Throws<InvalidOperationException>(() => SpeakerCorrection.Apply(
            new CallTranscript { Lines = [seed, locked] }, seed, [locked]));
    }
}
