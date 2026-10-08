using Tapybara.Core.Speech;
using Xunit;

namespace Tapybara.Core.Tests;

public sealed class SpeakerCoverageTests
{
    private static SpeakerSpan Span(double start, double end, int speaker) => new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), speaker);
    private static SpeakerCoverageResult Measure(params SpeakerSpan[] spans) => SpeakerCoverage.Measure(TimeSpan.Zero, TimeSpan.FromSeconds(10), spans);

    [Fact]
    public void SequentialVoicesRemainVisibleDespiteSingleWinningAssignment()
    {
        SpeakerSpan[] spans = [Span(0, 7, 0), Span(7, 10, 1)];
        Assert.Equal(0, SpeakerAssignment.For(TimeSpan.Zero, TimeSpan.FromSeconds(10), spans));
        SpeakerCoverageResult coverage = Measure(spans);
        Assert.Equal(2, coverage.Totals.Count);
        Assert.Equal(TimeSpan.Zero, coverage.Simultaneous);
        Assert.Equal(TimeSpan.FromSeconds(10), coverage.Covered);
    }

    [Fact]
    public void SimultaneousVoicesAreDistinctFromSequentialTurns()
    {
        SpeakerCoverageResult coverage = Measure(Span(0, 7, 0), Span(5, 10, 1));
        Assert.Equal(TimeSpan.FromSeconds(2), coverage.Simultaneous);
        Assert.Equal(new[] { 0, 1 }, coverage.Intervals[1].Speakers);
        Assert.Equal(TimeSpan.FromSeconds(7), coverage.Totals[0].Duration);
        Assert.Equal(TimeSpan.FromSeconds(5), coverage.Totals[1].Duration);
    }

    [Fact]
    public void DuplicateAndOverlappingSpansOfSameVoiceCountOnce()
    {
        SpeakerCoverageResult coverage = Measure(Span(-4, 4, 0), Span(2, 8, 0), Span(2, 8, 0), Span(9, 15, 1));
        Assert.Equal(TimeSpan.FromSeconds(8), coverage.Totals[0].Duration);
        Assert.Equal(TimeSpan.FromSeconds(9), coverage.Covered);
        Assert.Equal(TimeSpan.Zero, coverage.Simultaneous);
        Assert.Equal(2, coverage.Intervals.Count);
    }

    [Fact]
    public void TouchingSpansAndUnsortedInputDoNotInventOverlap()
    {
        SpeakerCoverageResult coverage = Measure(Span(5, 10, 1), Span(2, 5, 0), Span(0, 2, 0), Span(5, 5, 2), Span(8, 3, 3));
        Assert.Equal(2, coverage.Intervals.Count);
        Assert.Equal(TimeSpan.Zero, coverage.Simultaneous);
    }

    [Fact]
    public void InvalidWindowAndAbsentSpeechReturnNoEvidence()
    {
        Assert.Empty(SpeakerCoverage.Measure(TimeSpan.FromSeconds(2), TimeSpan.Zero, [Span(0, 5, 0)]).Totals);
        Assert.Empty(Measure(Span(10, 12, 0)).Intervals);
        Assert.Empty(Measure().Totals);
    }
}
