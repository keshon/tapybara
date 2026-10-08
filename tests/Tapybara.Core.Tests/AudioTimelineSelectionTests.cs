using Tapybara.Core.Editing;
using Xunit;

namespace Tapybara.Core.Tests;

public sealed class AudioTimelineSelectionTests
{
    private static AudioTimelineSelection Selection()
    {
        var selection = new AudioTimelineSelection();
        selection.SetContext("call", TimeSpan.FromSeconds(10), true);
        return selection;
    }

    [Fact]
    public void BackwardsRangeIsOrderedClampedAndRetainsPhysicalSource()
    {
        var selection = Selection();
        Assert.True(selection.SelectRange(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(-2), "mic"));
        Assert.Equal(new AudioRangeSelection(TimeSpan.Zero, TimeSpan.FromSeconds(10), "mic"), selection.Range);
        Assert.False(selection.SelectRange(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), "system"));
        Assert.Equal("mic", selection.Range!.Source);
    }

    [Fact]
    public void ContextDurationAndAvailabilityChangesClearBothSelections()
    {
        foreach (var context in new[] { ("other", 10, true), ("call", 5, true), ("call", 10, false) })
        {
            var selection = Selection();
            selection.SelectRange(TimeSpan.Zero, TimeSpan.FromSeconds(1), null);
            selection.BeginTiming("line", TimeSpan.Zero, TimeSpan.FromSeconds(3), true);
            selection.SetContext(context.Item1, TimeSpan.FromSeconds(context.Item2), context.Item3);
            Assert.Null(selection.Range);
            Assert.Null(selection.Timing);
        }
    }

    [Fact]
    public void BoundaryPreviewCannotCrossOtherEndAndCompletesOnlyOnce()
    {
        var selection = Selection();
        Assert.True(selection.BeginTiming("line", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), true));
        selection.MoveTiming(TimeSpan.FromSeconds(9));
        Assert.Equal(TimeSpan.FromMilliseconds(3999), selection.Timing!.Start);
        var adjustment = selection.CompleteTiming();
        Assert.Equal(TimeSpan.FromSeconds(2), adjustment!.OriginalStart);
        Assert.Equal(TimeSpan.FromSeconds(4), adjustment.End);
        Assert.Null(selection.CompleteTiming());
        Assert.True(selection.BeginTiming("line", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), false));
        selection.MoveTiming(TimeSpan.FromSeconds(15));
        Assert.Equal(TimeSpan.FromSeconds(10), selection.CompleteTiming()!.End);
    }

    [Fact]
    public void StaleRemovedAndChangedIntervalsCannotCompleteTimingEdit()
    {
        var selection = Selection();
        Assert.True(selection.BeginTiming("line", TimeSpan.Zero, TimeSpan.FromSeconds(4), false));
        selection.MoveTiming(TimeSpan.FromSeconds(5));
        selection.ReconcileTiming("line", TimeSpan.Zero, TimeSpan.FromSeconds(3));
        Assert.Null(selection.CompleteTiming());
        Assert.True(selection.BeginTiming("line", TimeSpan.Zero, TimeSpan.FromSeconds(4), false));
        selection.ReconcileTiming(null, TimeSpan.Zero, TimeSpan.Zero);
        Assert.Null(selection.CompleteTiming());
        Assert.False(selection.BeginTiming("line", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(4), true));
    }

    [Fact]
    public void UnchangedBoundaryAndCanceledPreviewProduceNoEdit()
    {
        var selection = Selection();
        selection.BeginTiming("line", TimeSpan.Zero, TimeSpan.FromSeconds(4), false);
        Assert.Null(selection.CompleteTiming());
        selection.BeginTiming("line", TimeSpan.Zero, TimeSpan.FromSeconds(4), false);
        selection.MoveTiming(TimeSpan.FromSeconds(5));
        selection.ClearTiming();
        Assert.Null(selection.CompleteTiming());
    }
}
