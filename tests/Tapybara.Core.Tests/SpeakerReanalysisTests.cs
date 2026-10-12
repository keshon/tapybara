using Tapybara.Core.Calls;
using Xunit;

namespace Tapybara.Core.Tests;

public sealed class SpeakerReanalysisTests
{
    private static CallLine Line(int at, string voice = "A", bool confirmed = false) =>
        new(CallChannel.Theirs, TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at + 6), "speech", voice, .88)
        { Id = Guid.NewGuid(), HumanEdits = confirmed ? CallLineEdits.Speaker : CallLineEdits.None };

    [Fact]
    public void MixedVoiceCorrectionScopesProposalsButKeepsOtherConfirmedReferences()
    {
        var a = Line(0); var b = Line(10, "B"); var confirmed = Line(20, "C", true);
        var scoped = SpeakerReanalysis.Scope(new CallTranscript { Lines = [a, b, confirmed] }, ["A"]);
        Assert.Equal(new[] { a, confirmed }, scoped.Lines);
        var report = SpeakerReanalysis.Analyse(scoped, _ => [1, 0], v => v == "C" ? "Константин" : null, _ => null);
        Assert.Equal(a, Assert.Single(Assert.Single(report.Groups).Lines));
        Assert.Equal("Константин", report.Groups[0].SuggestedName);
        var overlap = Line(1, "B");
        var mixed = SpeakerReanalysis.Scope(new CallTranscript { Lines = [a, overlap] }, ["A"]);
        Assert.Empty(SpeakerReanalysis.Analyse(mixed, _ => [1, 0], _ => null, _ => null).Groups);
    }

    [Fact]
    public void AttentionFilterHidesAgreementButPreservesUnknownConflictingAndDoubtfulGroups()
    {
        var line = Line(0);
        string? Name(string voice) => voice == "A" ? "Кирилл" : "Константин";
        Assert.False(SpeakerReanalysis.NeedsAttention(new([line], [line], "Кирилл"), Name));
        Assert.True(SpeakerReanalysis.NeedsAttention(new([line], [line], null), Name));
        Assert.True(SpeakerReanalysis.NeedsAttention(new([line], [line], "Константин"), Name));
        Assert.True(SpeakerReanalysis.NeedsAttention(new([line with { Fit = .3 }], [line], "Кирилл"), Name));
        Assert.True(SpeakerReanalysis.NeedsAttention(new([line, Line(10, "B")], [line], "Кирилл"), Name));
    }

    [Fact]
    public void SeparatesPeopleEvenWhenAllTheirMachineLabelsAndHighScoresAreTheSame()
    {
        var lines = new[] { Line(0), Line(10), Line(20), Line(30) };
        var transcript = new CallTranscript { Lines = lines };
        var report = SpeakerReanalysis.Analyse(transcript, l => l.Start.TotalSeconds < 20 ? [1, 0] : [0, 1],
            _ => "Кирилл", _ => null);
        Assert.Equal(2, report.Groups.Count);
        Assert.All(report.Groups, g => { Assert.Equal(2, g.Lines.Count); Assert.Null(g.SuggestedName); });
        var changed = SpeakerReanalysis.Apply(transcript, [new(report.Groups[1].Lines, "B")]);
        Assert.Equal(2, changed.Lines.Count(l => l.Voice == "B"));
        Assert.Equal(transcript.Lines.Select(l => l.Text), changed.Lines.Select(l => l.Text));
    }

    [Fact]
    public void ConfirmedExampleSuggestsNameButIsNeverMoved()
    {
        var seed = Line(0, "B", true);
        var wrong = Line(10);
        var transcript = new CallTranscript { Lines = [seed, wrong] };
        var group = Assert.Single(SpeakerReanalysis.Analyse(transcript, _ => [1, 0],
            v => v == "B" ? "Константин" : "Кирилл", _ => "wrong remembered name").Groups);
        Assert.Equal("Константин", group.SuggestedName);
        Assert.Equal(wrong, Assert.Single(group.Lines));
        Assert.Throws<InvalidOperationException>(() => SpeakerReanalysis.Apply(transcript, [new([seed], "C")]));
    }

    [Fact]
    public void NoSimilarityChainingBetweenDifferentVoices()
    {
        var a = Line(0); var bridge = Line(10); var b = Line(20);
        var transcript = new CallTranscript { Lines = [a, bridge, b] };
        float[] Print(CallLine l) => l == a ? [1, 0] : l == bridge ? [.8f, .6f] : [.3f, .954f];
        var groups = SpeakerReanalysis.Analyse(transcript, Print, _ => null, _ => null).Groups;
        Assert.Equal(2, groups.Count);
        Assert.DoesNotContain(groups, g => g.Lines.Contains(a) && g.Lines.Contains(b));
    }

    [Fact]
    public void StaleAndDuplicateAssignmentsAreRejectedAndCancellationIsSupported()
    {
        var line = Line(0);
        var transcript = new CallTranscript { Lines = [line] };
        Assert.Throws<InvalidOperationException>(() => SpeakerReanalysis.Apply(transcript, [new([line with { Text = "old" }], "B")]));
        Assert.Throws<InvalidOperationException>(() => SpeakerReanalysis.Apply(transcript, [new([line], "B"), new([line], "C")]));
        Assert.Throws<OperationCanceledException>(() => SpeakerReanalysis.Analyse(transcript, _ => [1, 0], _ => null, _ => null,
            token: new CancellationToken(true)));
    }

    [Fact]
    public void ReportsShortOverlappingAndUnmeasurableSpeechAsSkipped()
    {
        var shortLine = Line(0) with { End = TimeSpan.FromSeconds(2) };
        var a = Line(10); var b = Line(11); var bad = Line(30);
        var report = SpeakerReanalysis.Analyse(new CallTranscript { Lines = [shortLine, a, b, bad] },
            _ => null, _ => null, _ => null);
        Assert.Empty(report.Groups);
        Assert.Equal(4, report.Skipped);
    }
}
