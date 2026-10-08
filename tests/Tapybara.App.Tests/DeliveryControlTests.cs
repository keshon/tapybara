using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using Tapybara.App.Controls;
using Tapybara.Core.Export;
using Xunit;

namespace Tapybara.App.Tests;

public sealed class DeliveryControlTests
{
    private static void OnSta(Action check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { check(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    [Fact]
    public void ExportHasUsableDefaultsAndLegacyOnlyOffersMarkdown() => OnSta(() =>
    {
        var panel = new TranscriptExportPanel(["Anna"], new Dictionary<string, string>(), selectionAvailable: false);
        Assert.Equal(TranscriptExportFormat.Text, panel.Options.Format);
        Assert.Equal(TranscriptSpeakerLabels.Names, panel.Options.Labels);
        Assert.True(panel.Options.Timestamps);
        Assert.False(panel.SelectionOnly);
        Assert.NotEqual("Anna", panel.Options.Aliases["Anna"]);
        var legacy = new TranscriptExportPanel([], new Dictionary<string, string>(), false, structured: false);
        Assert.Equal(TranscriptExportFormat.Markdown, legacy.Options.Format);
        Assert.False(legacy.Children.OfType<ComboBox>().Last().IsEnabled);
    });

    [Fact]
    public void FiltersCollapseWithoutHidingActiveRemovableChips() => OnSta(() =>
    {
        var bar = new SearchFilterBar();
        int removed = 0;
        bar.SetActiveFilters([("Anna", () => removed++)]);
        Assert.Equal(Visibility.Collapsed, bar.Filters.Visibility);
        WrapPanel chips = bar.Children.OfType<WrapPanel>().Single();
        Assert.Equal(Visibility.Visible, chips.Visibility);
        var chip = Assert.IsType<Wpf.Ui.Controls.Button>(Assert.Single(chips.Children.Cast<object>()));
        chip.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(1, removed);
        bar.SetActiveFilters([]);
        Assert.Equal(Visibility.Collapsed, chips.Visibility);
    });

    [Fact]
    public void TransportUpdatesDoNotSeekAndNativeSliderChangesRequestExactPosition() => OnSta(() =>
    {
        var bar = new AudioTransportBar();
        var requests = new List<TimeSpan>();
        bar.SeekRequested += requests.Add;
        bar.SetState(TimeSpan.FromSeconds(3.25), TimeSpan.FromSeconds(12), false, true);
        Assert.Empty(requests);
        Slider slider = bar.Children.OfType<Slider>().Single();
        Assert.Equal(3.25, slider.Value);
        slider.Value = 8.75;
        Assert.Equal(TimeSpan.FromSeconds(8.75), Assert.Single(requests));
        bar.SetState(TimeSpan.FromSeconds(8.8), TimeSpan.FromSeconds(12), true, true);
        Assert.Single(requests);
    });

    [Fact]
    public void TransportKeepsPreviewWhileScrubbingAndCommitsOnlyOnce() => OnSta(() =>
    {
        var bar = new AudioTransportBar();
        var requests = new List<TimeSpan>();
        bar.SeekRequested += requests.Add;
        bar.SetState(TimeSpan.Zero, TimeSpan.FromSeconds(12), true, true);
        Slider slider = bar.Children.OfType<Slider>().Single();
        slider.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
            System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        slider.Value = 6.5;
        bar.SetState(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(12), true, true);
        Assert.Equal(6.5, slider.Value);
        Assert.Empty(requests);
        slider.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
            System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        slider.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0)
            { RoutedEvent = UIElement.LostMouseCaptureEvent });
        Assert.Equal(TimeSpan.FromSeconds(6.5), Assert.Single(requests));
        Assert.False(bar.IsScrubbing);
    });

    [Fact]
    public void ResetTransportInteractionDoesNotSeekAnotherDocument() => OnSta(() =>
    {
        var bar = new AudioTransportBar();
        int requests = 0;
        bar.SeekRequested += _ => requests++;
        bar.SetState(TimeSpan.Zero, TimeSpan.FromSeconds(12), true, true);
        Slider slider = bar.Children.OfType<Slider>().Single();
        slider.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
            System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        slider.Value = 7;
        bar.ResetInteraction();
        bar.SetState(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8), false, true);
        slider.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
            System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
        Assert.Equal(0, requests);
        Assert.Equal(2, slider.Value);
    });

    [Fact]
    public void RepeatedKeyboardSeekCommitsOnReleaseInsteadOfReopeningEachStep() => OnSta(() =>
    {
        var bar = new AudioTransportBar();
        var requests = new List<TimeSpan>();
        bar.SeekRequested += requests.Add;
        bar.SetState(TimeSpan.Zero, TimeSpan.FromSeconds(20), true, true);
        Slider slider = bar.Children.OfType<Slider>().Single();
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("transport-test"));
        slider.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0,
            System.Windows.Input.Key.Right) { RoutedEvent = UIElement.PreviewKeyDownEvent });
        slider.Value = 1;
        slider.Value = 2;
        slider.Value = 3;
        Assert.Empty(requests);
        slider.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0,
            System.Windows.Input.Key.Right) { RoutedEvent = UIElement.PreviewKeyUpEvent });
        slider.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, 0, slider, null)
            { RoutedEvent = UIElement.LostKeyboardFocusEvent });
        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(requests));
    });
    [Fact]
    public void TimelineRejectsStaleIntervalsAndPreservesCurrentSelection() => OnSta(() =>
    {
        var timeline = new AudioTimelineView();
        var requests = new List<object>();
        timeline.IntervalRequested += requests.Add;
        object key = Guid.NewGuid();
        var span = new TimelineInterval(key, TimeSpan.Zero, TimeSpan.FromSeconds(2), "Anna · system", Estimated: true);
        TimelineLane[] lanes = [new("Anna", System.Windows.Media.Brushes.Blue, [span])];
        timeline.SetData(TimeSpan.FromSeconds(10), [], lanes, true);
        timeline.ShowSpeakers(true);
        timeline.Activate(key);
        Assert.Equal(key, timeline.SelectedKey);
        Assert.Equal(key, Assert.Single(requests));
        timeline.SetData(TimeSpan.FromSeconds(10), [], lanes, true);
        Assert.Equal(key, timeline.SelectedKey);
        timeline.SetData(TimeSpan.FromSeconds(10), [], [], true);
        timeline.Activate(key);
        Assert.Null(timeline.SelectedKey);
        Assert.Single(requests);
        timeline.SetData(TimeSpan.FromSeconds(10), [], lanes, false);
        timeline.Activate(key);
        Assert.Single(requests);
    });

    [Fact]
    public void TimelineRendersOverlapAndKeyboardSeekCommitsOnce() => OnSta(() =>
    {
        var timeline = new AudioTimelineView();
        var requests = new List<TimeSpan>();
        timeline.SeekRequested += requests.Add;
        timeline.SetData(TimeSpan.FromSeconds(10), [],
            [new("Anna", System.Windows.Media.Brushes.Blue,
                [new("a", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), "Anna"),
                 new("b", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), "Anna estimated", Estimated: true)])], true);
        timeline.ShowSpeakers(true);
        timeline.Activate("a");
        timeline.SelectRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        timeline.Measure(new Size(640, 300));
        timeline.Arrange(new Rect(0, 0, 640, timeline.DesiredSize.Height));
        timeline.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(640, 300, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(timeline);
        var scroll = timeline.Children.OfType<ScrollViewer>().Single();
        var surface = Assert.IsAssignableFrom<Control>(scroll.Content);
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("timeline-test"));
        for (int step = 0; step < 3; step++)
        {
            surface.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0,
                System.Windows.Input.Key.Right) { RoutedEvent = UIElement.KeyDownEvent });
        }
        timeline.SetPosition(TimeSpan.FromSeconds(.5));
        Assert.Empty(requests);
        surface.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0,
            System.Windows.Input.Key.Right) { RoutedEvent = UIElement.KeyUpEvent });
        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(requests));
        surface.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0,
            System.Windows.Input.Key.End) { RoutedEvent = UIElement.KeyDownEvent });
        timeline.SetData(TimeSpan.FromSeconds(2), [], [], true);
        surface.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0,
            System.Windows.Input.Key.End) { RoutedEvent = UIElement.KeyUpEvent });
        Assert.Single(requests);
    });
    [Fact]
    public void TimelineRendersRealPeaksAtCompactAndWideWidths() => OnSta(() =>
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "timeline-" + Guid.NewGuid() + ".wav");
        try
        {
            using (var writer = new NAudio.Wave.WaveFileWriter(path, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(1000, 1)))
            {
                float[] samples = [0, .2f, -.6f, 0, .8f, 0, .4f];
                writer.WriteSamples(samples, 0, samples.Length);
            }
            var wave = Tapybara.Core.Audio.WaveformEnvelope.Read(path, 3);
            var timeline = new AudioTimelineView();
            timeline.SetData(TimeSpan.FromMilliseconds(10),
                [new("Microphone", System.Windows.Media.Brushes.Blue, [], wave),
                 new("System", System.Windows.Media.Brushes.Green, [], Status: "Unavailable")], [], true);
            foreach (int width in new[] { 80, 260, 900 })
            {
                timeline.Measure(new Size(width, 300));
                timeline.Arrange(new Rect(0, 0, width, timeline.DesiredSize.Height));
                timeline.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, 300, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(timeline);
            }
        }
        finally { System.IO.File.Delete(path); }
    });
    [Fact]
    public void TimelineRangeRemainsSourceSpecificAndCannotLeakToAnotherCall() => OnSta(() =>
    {
        var timeline = new AudioTimelineView();
        var requests = new List<Tapybara.Core.Editing.AudioRangeSelection>();
        timeline.RangePlayRequested += requests.Add;
        TimelineLane[] sources = [new("Mic", System.Windows.Media.Brushes.Blue, [], Key: "mic")];
        timeline.SetData(TimeSpan.FromSeconds(10), sources, [], true, "call-a");
        Assert.True(timeline.SelectRange(TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(2), "mic"));
        timeline.PlayRange();
        Assert.Equal("mic", Assert.Single(requests).Source);
        Assert.Equal(TimeSpan.FromSeconds(2), requests[0].Start);
        timeline.SetData(TimeSpan.FromSeconds(10), sources, [], true, "call-a");
        Assert.NotNull(timeline.Range);
        Assert.False(timeline.SelectRange(TimeSpan.Zero, TimeSpan.FromSeconds(2), "deleted-source"));
        timeline.SetData(TimeSpan.FromSeconds(10), sources, [], true, "call-b");
        timeline.PlayRange();
        Assert.Null(timeline.Range);
        Assert.Single(requests);
    });

    [Fact]
    public void TimelineKeyboardTimingUsesCurrentIntervalAndDoesNotMutateIt() => OnSta(() =>
    {
        var timeline = new AudioTimelineView();
        var requests = new List<Tapybara.Core.Editing.AudioTimingAdjustment>();
        timeline.TimingAdjustmentRequested += requests.Add;
        var span = new TimelineInterval("a", TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), "Anna");
        timeline.SetData(TimeSpan.FromSeconds(10), [], [new("Anna", System.Windows.Media.Brushes.Blue, [span])], true);
        timeline.Activate("a");
        timeline.RequestTiming();
        Assert.Equal(span.Start, Assert.Single(requests).OriginalStart);
        Assert.Equal(span.End, requests[0].End);
        timeline.SetData(TimeSpan.FromSeconds(10), [], [], true);
        timeline.RequestTiming();
        Assert.Single(requests);
        timeline.ResetInteraction();
        Assert.Null(timeline.Range);
    });
    [Fact]
    public void BoundaryFieldsAcceptRoundedEndpointButEmitExactSourceLimit() => OnSta(() =>
    {
        TimeSpan maximum = TimeSpan.FromTicks(69999);
        var editor = new AudioBoundaryEditor(TimeSpan.Zero, maximum, maximum);
        var requests = new List<(TimeSpan Start, TimeSpan End)>();
        editor.PlayRequested += (from, to) => requests.Add((from, to));
        var row = editor.Children.OfType<StackPanel>().Single();
        var play = row.Children.OfType<Wpf.Ui.Controls.Button>().First();
        Assert.True(play.IsEnabled);
        play.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(maximum, Assert.Single(requests).End);
        editor.Children.OfType<Wpf.Ui.Controls.TextBox>().Last().Text = "0.008";
        Assert.False(play.IsEnabled);
    });

    [Fact]
    public void ForwardCommandAndFollowToggleAreDirectAndBoundedByAvailability() => OnSta(() =>
    {
        var bar = new AudioTransportBar();
        int requests = 0; bar.ForwardRequested += () => requests++;
        var buttons = bar.Children.OfType<StackPanel>().First().Children.OfType<Wpf.Ui.Controls.Button>().ToArray();
        Assert.Equal(4, buttons.Length);
        bar.SetState(TimeSpan.Zero, TimeSpan.FromSeconds(10), false, true);
        buttons[^1].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(1, requests);
        bar.SetState(TimeSpan.Zero, TimeSpan.FromSeconds(10), false, false);
        Assert.False(buttons[^1].IsEnabled);
        var follow = bar.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Single();
        follow.IsChecked = false;
        Assert.False(bar.Follow);
        bar.Follow = true;
        Assert.True(follow.IsChecked);
    });

    [Fact]
    public void TimeFieldsAcceptClockAndSecondsButRejectInvalidComponents() => OnSta(() =>
    {
        Assert.True(AudioTime.TryParse("14:22.390", out TimeSpan value));
        Assert.Equal(TimeSpan.FromSeconds(862.39), value);
        Assert.True(AudioTime.TryParse("862,39", out TimeSpan seconds));
        Assert.Equal(value, seconds);
        Assert.True(AudioTime.TryParse("1:02:03.5", out TimeSpan hours));
        Assert.Equal(TimeSpan.FromSeconds(3723.5), hours);
        foreach (string invalid in new[] { "1:60", "1:60:00", "NaN", "-1", "00:", "1.5:00", "1:2:3:4" })
        { Assert.False(AudioTime.TryParse(invalid, out _)); }
    });

    [Fact]
    public void FindButtonsKeepNativeEnterAndInlineMatchesRemainHighlighted() => OnSta(() =>
    {
        var bar = new FindReplaceBar();
        int navigations = 0; bar.NavigateRequested += _ => navigations++;
        bar.SetMatches(0, 2);
        bar.ShowReplace(true);
        Wpf.Ui.Controls.Button button = bar.Children.OfType<Grid>().First().Children.OfType<StackPanel>().Single()
            .Children.OfType<Wpf.Ui.Controls.Button>().Last();
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("key-probe") { Width = 1, Height = 1, WindowStyle = 0 });
        var key = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
            source,
            0, System.Windows.Input.Key.Enter) { RoutedEvent = UIElement.PreviewKeyDownEvent };
        button.RaiseEvent(key);
        Assert.False(key.Handled);
        Assert.Equal(0, navigations);
        Assert.True(new InlineTextEditor().IsInactiveSelectionHighlightEnabled);
    });

    [Fact]
    public void SearchClearsOnlyItsOwnHighlightsAndDoesNotEraseManualSelection() => OnSta(() =>
    {
        var highlights = new TextSelectionHighlight();
        var first = new InlineTextEditor { Text = "first match", DataContext = new object() };
        var second = new InlineTextEditor { Text = "second match", DataContext = new object() };
        highlights.Show(first, 6, 5);
        Assert.Equal(5, first.SelectionLength);
        highlights.Show(second, 7, 5);
        Assert.Equal(0, first.SelectionLength);
        Assert.Equal(5, second.SelectionLength);
        highlights.Clear();
        Assert.Equal(0, second.SelectionLength);
        highlights.Show(first, 6, 5);
        first.Select(0, 5);
        highlights.Clear();
        Assert.Equal("first", first.SelectedText);
        highlights.Show(second, 7, 5);
        second.DataContext = new object();
        second.Select(0, 6);
        highlights.Clear();
        Assert.Equal("second", second.SelectedText);
    });
}
