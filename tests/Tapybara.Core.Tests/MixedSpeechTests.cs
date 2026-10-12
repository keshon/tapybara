using Tapybara.Core.Calls;
using Tapybara.Core.Speech;
using Xunit;
namespace Tapybara.Core.Tests;
public sealed class MixedSpeechTests
{
    private static SpeakerSpan Span(double start, double end, int speaker) =>
        new(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), speaker);
    [Fact]
    public void DistinguishesSpeakerChangesFromOverlapAndIgnoresTinyTails()
    {
        Assert.Equal(SpeechMix.SpeakerChange, SpeakerAssignment.Mix(TimeSpan.Zero, TimeSpan.FromSeconds(10), [Span(0, 5, 0), Span(5, 10, 1)]));
        Assert.Equal(SpeechMix.Overlap, SpeakerAssignment.Mix(TimeSpan.Zero, TimeSpan.FromSeconds(10), [Span(0, 7, 0), Span(5, 10, 1)]));
        Assert.Equal(SpeechMix.None, SpeakerAssignment.Mix(TimeSpan.Zero, TimeSpan.FromSeconds(10), [Span(0, 10, 0), Span(9.8, 10, 1)]));
    }
    [Fact]
    public void RealReportedIntervalsContainMixedSpeechAccordingToReadOnlyProbe()
    {
        var spans = new[] { Span(2684.35,2691.37,1), Span(2692.01,2694.81,2), Span(2693.33,2693.90,1),
            Span(2694.81,2702.24,1), Span(2698.15,2698.96,2), Span(2698.96,2699.44,4),
            Span(2702.85,2704.75,4), Span(2703.42,2704.38,1), Span(2704.92,2706.96,1),
            Span(2707.15,2719.97,4) };
        Assert.Equal(SpeechMix.Overlap, SpeakerAssignment.Mix(TimeSpan.FromSeconds(2686.74), TimeSpan.FromSeconds(2694.14), spans));
        Assert.Equal(SpeechMix.Overlap, SpeakerAssignment.Mix(TimeSpan.FromSeconds(2694.14), TimeSpan.FromSeconds(2712.86), spans));
    }
    [Fact]
    public void MixedLinesAreNeverUsedAsVoiceReferencesOrBulkMatchCandidates()
    {
        var line = new CallLine(CallChannel.Theirs, TimeSpan.Zero, TimeSpan.FromSeconds(8), "mixed", "A")
            { Id = Guid.NewGuid(), SpeechMix = SpeechMix.Overlap };
        var doc = new CallTranscript { Lines = [line] };
        var report = VoiceCheck.Measure(doc.Lines, _ => throw new InvalidOperationException("must not embed"));
        Assert.Empty(report.Lines);
    }
}
