using Tapybara.Core.Editing;
using Xunit;

namespace Tapybara.Core.Tests;

public sealed class AudioViewportTests
{
    [Fact]
    public void ZoomPreservesPointerTimeAndPanClampsToCall()
    {
        var view = new AudioViewport();
        view.SetDuration(TimeSpan.FromSeconds(100));
        view.Zoom(2, .75);
        Assert.Equal(TimeSpan.FromSeconds(75), view.TimeAt(.75));
        Assert.Equal(TimeSpan.FromSeconds(50), view.Length);
        view.PanTo(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromSeconds(50), view.Start);
        Assert.Equal(TimeSpan.FromSeconds(100), view.End);
        view.PanTo(TimeSpan.FromSeconds(-100));
        Assert.Equal(TimeSpan.Zero, view.Start);
    }

    [Fact]
    public void SourceRefreshKeepsZoomButContextChangeAndFitResetIt()
    {
        var view = new AudioViewport();
        view.SetDuration(TimeSpan.FromSeconds(100)); view.Zoom(4); view.PanTo(TimeSpan.FromSeconds(70));
        view.SetDuration(TimeSpan.FromSeconds(100));
        Assert.Equal(TimeSpan.FromSeconds(70), view.Start);
        Assert.Equal(TimeSpan.FromSeconds(25), view.Length);
        view.SetDuration(TimeSpan.FromSeconds(80));
        Assert.Equal(TimeSpan.FromSeconds(55), view.Start);
        view.SetDuration(TimeSpan.FromSeconds(20), reset: true);
        Assert.Equal(TimeSpan.Zero, view.Start);
        Assert.Equal(TimeSpan.FromSeconds(20), view.Length);
        view.Zoom(2); view.Fit();
        Assert.False(view.IsZoomed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.1)]
    [InlineData(120)]
    public void EmptyAndShortCallsStayValidAtExtremeZoom(double seconds)
    {
        var view = new AudioViewport();
        view.SetDuration(TimeSpan.FromSeconds(seconds)); view.Zoom(100000);
        Assert.True(view.Length >= TimeSpan.Zero && view.End <= view.Duration);
        Assert.Equal(TimeSpan.FromSeconds(Math.Min(seconds, 1)), view.Length);
        view.Zoom(double.NaN); view.Zoom(-1); view.Zoom(double.PositiveInfinity);
        Assert.True(view.End <= view.Duration);
        view.Zoom(.0000001);
        Assert.False(view.IsZoomed);
    }
}
