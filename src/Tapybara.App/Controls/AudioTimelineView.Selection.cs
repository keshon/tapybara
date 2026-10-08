using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Tapybara.App.Localization;
using Tapybara.Core.Editing;
using Cursors = System.Windows.Input.Cursors;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using ComboBox = System.Windows.Controls.ComboBox;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using TextBlock = System.Windows.Controls.TextBlock;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Tapybara.App.Controls;

/// <summary>Диапазон прослушивания и предпросмотр границ; запись остаётся явной командой экрана.</summary>
public sealed partial class AudioTimelineView
{
    private sealed record SourceChoice(string Label, object? Key);
    private readonly WrapPanel _rangeRow = new() { Margin = new Thickness(0, Tokens.Space1, 0, 0) };
    private readonly Wpf.Ui.Controls.Button _selectRange = new();
    private readonly Wpf.Ui.Controls.Button _playRange = new();
    private readonly Wpf.Ui.Controls.Button _clearRange = new();
    private readonly Wpf.Ui.Controls.Button _adjustTiming = new();
    private readonly TextBlock _rangeLabel = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private Popup? _rangePopup;
    private TimelineInterval? SelectedInterval => (_choices.SelectedItem as Choice)?.Interval;
    public FrameworkElement TimingAnchor => _adjustTiming;
    public AudioRangeSelection? Range => _selection.Range;
    public event Action<AudioRangeSelection>? RangePlayRequested;
    public event Action<AudioTimingAdjustment>? TimingAdjustmentRequested;

    private void InitializeRangeControls()
    {
        _mode.Margin = new Thickness(0, 0, Tokens.Space1, Tokens.Space1);
        _info.Margin = new Thickness(0, 0, Tokens.Space2, Tokens.Space1);
        _rangeRow.Children.Add(_mode);
        _rangeRow.Children.Add(_info);
        foreach (var button in new[] { _selectRange, _playRange, _clearRange, _adjustTiming })
        {
            ControlSizing.Apply(button);
            button.Margin = new Thickness(0, 0, Tokens.Space2, Tokens.Space1);
            _rangeRow.Children.Add(button);
        }
        _rangeRow.Children.Add(_rangeLabel);
        SetRow(_rangeRow, 4);
        Children.Add(_rangeRow);
        _selectRange.Click += (_, _) => OpenRangeEditor();
        _playRange.Click += (_, _) => PlayRange();
        _clearRange.Click += (_, _) => ClearRange();
        _adjustTiming.Click += (_, _) => RequestTiming();
        Unloaded += (_, _) => ResetInteraction();
    }

    private void ApplyRangeLanguage()
    {
        _selectRange.Content = L.S.TimelineSelectRange;
        _playRange.Content = L.S.TimelinePlayRange;
        _clearRange.Content = L.S.TimelineClearRange;
        _adjustTiming.Content = L.S.EditTiming;
        AutomationProperties.SetName(_rangeLabel, L.S.PlaybackRange);
    }

    private void RefreshRangeControls()
    {
        _selectRange.IsEnabled = _canPlay;
        _playRange.Visibility = _clearRange.Visibility = Range is null ? Visibility.Collapsed : Visibility.Visible;
        _playRange.IsEnabled = _canPlay && Range is not null;
        _adjustTiming.Visibility = SpeakersMode ? Visibility.Visible : Visibility.Collapsed;
        _adjustTiming.IsEnabled = _canPlay && SelectedInterval is not null;
        _rangeLabel.Text = Range is { } range
            ? SourceLabel(range.Source) + " · " + PreciseStamp(range.Start) + "–" + PreciseStamp(range.End) : string.Empty;
        _surface.InvalidateVisual();
    }

    private string SourceLabel(object? key) => key is null ? L.S.PlaybackWholeCall
        : _sourceLanes.FirstOrDefault(l => Equals(l.Key, key))?.Label ?? string.Empty;
    private bool ValidSource(object? key) => key is null || _sourceLanes.Any(l => l.Key is not null && Equals(l.Key, key));

    public bool SelectRange(TimeSpan from, TimeSpan to, object? source = null)
    {
        if (!ValidSource(source)) { return false; }
        TimeSpan maximum = _sourceLanes.FirstOrDefault(l => source is not null && Equals(l.Key, source))?.Waveform?.Duration
            ?? _surface.Duration;
        if (source is not null && maximum > TimeSpan.Zero)
        {
            from = from > maximum ? maximum : from;
            to = to > maximum ? maximum : to;
        }
        bool selected = _selection.SelectRange(from, to, source);
        RefreshRangeControls();
        return selected;
    }

    public void ClearRange() { _selection.ClearRange(); RefreshRangeControls(); }
    public void PlayRange()
    {
        if (_canPlay && Range is { } range && ValidSource(range.Source)) { RangePlayRequested?.Invoke(range); }
    }

    public void RequestTiming()
    {
        if (!_canPlay || SelectedInterval is not { } span) { return; }
        TimingAdjustmentRequested?.Invoke(new(span.Key, span.Start, span.End, span.Start, span.End));
    }

    public void ResetInteraction()
    {
        _surface.CancelSeek();
        _surface.CancelPointer();
        _selection.Clear();
        if (_rangePopup is not null) { _rangePopup.IsOpen = false; _rangePopup = null; }
        if (_helpPopup is not null) { _helpPopup.IsOpen = false; _helpPopup = null; }
        RefreshRangeControls();
    }

    private void OpenRangeEditor()
    {
        if (!_canPlay) { return; }
        if (_rangePopup is not null) { _rangePopup.IsOpen = false; }
        TimeSpan from = Range?.Start ?? (_surface.Position < _surface.Duration ? _surface.Position : TimeSpan.Zero);
        TimeSpan to = Range?.End ?? (from + TimeSpan.FromSeconds(5) < _surface.Duration ? from + TimeSpan.FromSeconds(5) : _surface.Duration);
        var editor = new AudioBoundaryEditor(from, to, _surface.Duration,
            applyLabel: L.S.TimelineSelectRange, help: L.S.TimelineRangeHelp) { Width = double.NaN, MinWidth = 220, MaxWidth = 360 };
        var sources = new ComboBox { DisplayMemberPath = nameof(SourceChoice.Label), Margin = new Thickness(0, 0, 0, Tokens.Space2) };
        SourceChoice[] choices = [new(L.S.PlaybackWholeCall, null), .. _sourceLanes.Where(l => l.Key is not null).Select(l => new SourceChoice(l.Label, l.Key))];
        ControlSizing.Apply(sources);
        sources.ItemsSource = choices;
        sources.SelectedItem = choices.First(c => Equals(c.Key, Range?.Source));
        AutomationProperties.SetName(sources, L.S.TimelineSources);
        editor.Children.Insert(0, sources);
        var card = new PopoverSurface { Child = editor, Padding = new Thickness(Tokens.Space3), CornerRadius = Tokens.CardRadius };
        var popup = new Popup { Child = card, PlacementTarget = _selectRange, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true };
        object? Source() => (sources.SelectedItem as SourceChoice)?.Key;
        editor.PlayRequested += (begin, end) => { if (SelectRange(begin, end, Source())) { PlayRange(); } };
        editor.ApplyRequested += (begin, end, _) => { if (SelectRange(begin, end, Source())) { popup.IsOpen = false; } };
        popup.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(card) is System.Windows.Interop.HwndSource source) { NativeFocus.Set(source.Handle); }
            editor.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        };
        popup.Closed += (_, _) => { if (ReferenceEquals(_rangePopup, popup)) { _rangePopup = null; } };
        _rangePopup = popup;
        AnchoredPopup.Open(popup, _selectRange, above: true);
    }

    private static string PreciseStamp(TimeSpan value) => string.Create(CultureInfo.InvariantCulture,
        $"{(int)value.TotalMinutes:00}:{value.Seconds:00}.{value.Milliseconds:000}");

    private sealed partial class Surface
    {
        private Point? _pointerStart;
        private object? _pointerSource;
        private bool _boundaryDrag;
        private bool _rangeDrag;
        private AudioRangeSelection? _rangeBeforeDrag;

        public void CancelPointer()
        {
            if (_pointerStart is not null && _rangeDrag)
            {
                owner._selection.ClearRange();
                if (_rangeBeforeDrag is { } before) { owner._selection.SelectRange(before.Start, before.End, before.Source); }
                owner.RefreshRangeControls();
            }
            _pointerStart = null;
            _boundaryDrag = _rangeDrag = false;
            owner._selection.ClearTiming();
            if (IsMouseCaptured) { ReleaseMouseCapture(); }
            InvalidateVisual();
        }

        private void BeginPointer(Point point)
        {
            CancelSeek();
            _pointerStart = point;
            _rangeBeforeDrag = owner.Range;
            if (owner.SpeakersMode && SelectedHit(point) is { } hit)
            {
                bool start = Math.Abs(point.X - hit.Rect.Left) <= Math.Abs(point.X - hit.Rect.Right);
                _boundaryDrag = owner._selection.BeginTiming(hit.Interval.Key, hit.Interval.Start, hit.Interval.End, start);
            }
            int row = (int)((point.Y - RulerHeight) / LaneHeight);
            _pointerSource = !owner.SpeakersMode && point.Y >= RulerHeight && row >= 0 && row < _lanes.Count ? _lanes[row].Key : null;
            if (!CaptureMouse()) { CancelPointer(); }
        }

        private (Rect Rect, TimelineInterval Interval)? SelectedHit(Point point)
        {
            foreach (var hit in _rects.Where(r => Equals(r.Interval.Key, owner.SelectedKey)))
            {
                if (point.Y >= hit.Rect.Top - 3 && point.Y <= hit.Rect.Bottom + 3
                    && (Math.Abs(point.X - hit.Rect.Left) <= 5 || Math.Abs(point.X - hit.Rect.Right) <= 5)) { return hit; }
            }
            return null;
        }

        private bool MovePointer(Point point)
        {
            if (_pointerStart is not { } origin) { return false; }
            if (_boundaryDrag)
            {
                owner._selection.MoveTiming(Time(point.X));
                InvalidateVisual();
            }
            else if (_rangeDrag || Math.Abs(point.X - origin.X) >= SystemParameters.MinimumHorizontalDragDistance)
            {
                _rangeDrag = true;
                owner.SelectRange(Time(origin.X), Time(point.X), _pointerSource);
            }
            return true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_pointerStart is null) { return; }
            Point point = e.GetPosition(this);
            MovePointer(point);
            bool click = !_boundaryDrag && !_rangeDrag;
            AudioTimingAdjustment? timing = _boundaryDrag ? owner._selection.CompleteTiming() : null;
            _pointerStart = null;
            _boundaryDrag = _rangeDrag = false;
            if (IsMouseCaptured) { ReleaseMouseCapture(); }
            if (timing is not null) { owner.TimingAdjustmentRequested?.Invoke(timing); }
            else if (click) { ClickAt(point); }
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (_pointerStart is not null)
            {
                owner._selection.ClearRange();
                if (_rangeBeforeDrag is { } before) { owner.SelectRange(before.Start, before.End, before.Source); }
                CancelPointer();
                owner.RefreshRangeControls();
            }
        }

        private void ClickAt(Point point)
        {
            TimelineInterval[] hits = [.. _rects.Where(r => r.Rect.Contains(point)).Select(r => r.Interval)];
            if (hits.Length == 1) { owner.Activate(hits[0].Key); }
            else if (hits.Length > 1)
            {
                var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.MousePoint };
                foreach (TimelineInterval interval in hits)
                {
                    var item = new MenuItem { Header = interval.Hint };
                    item.Click += (_, _) => owner.Activate(interval.Key);
                    menu.Items.Add(item);
                }
                menu.IsOpen = true;
            }
            else { owner.SeekRequested?.Invoke(Time(point.X)); }
        }

        private void UpdatePointerCursor(Point point) => Cursor = SelectedHit(point) is not null ? Cursors.SizeWE : Cursors.Arrow;

        private void DrawSelection(DrawingContext drawing)
        {
            if (owner.Range is not { } range) { return; }
            double y = RulerHeight;
            double height = Math.Max(0, ActualHeight - y);
            if (!owner.SpeakersMode && range.Source is not null)
            {
                int row = _lanes.ToList().FindIndex(l => Equals(l.Key, range.Source));
                if (row < 0) { return; }
                y += row * LaneHeight;
                height = LaneHeight;
            }
            drawing.PushOpacity(.15);
            drawing.DrawRectangle(Foreground, null, new Rect(X(range.Start), y, Math.Max(0, X(range.End) - X(range.Start)), height));
            drawing.Pop();
        }

        private void DrawHandles(DrawingContext drawing, (Rect Rect, TimelineInterval Interval) hit)
        {
            if (!owner.SpeakersMode) { return; }
            TimeSpan from = owner._selection.Timing?.Start ?? hit.Interval.Start;
            TimeSpan to = owner._selection.Timing?.End ?? hit.Interval.End;
            foreach (double x in new[] { X(from), X(to) })
            {
                drawing.DrawRectangle(Foreground, null, new Rect(x - 2, hit.Rect.Top - 3, 4, hit.Rect.Height + 6));
            }
            if (owner._selection.Timing is not null)
            {
                drawing.DrawRectangle(null, new Pen(Foreground, 1), new Rect(X(from), hit.Rect.Top, Math.Max(0, X(to) - X(from)), hit.Rect.Height));
                Label(drawing, PreciseStamp(from) + "–" + PreciseStamp(to), LabelWidth, 1, PlotWidth);
            }
        }
    }
}
