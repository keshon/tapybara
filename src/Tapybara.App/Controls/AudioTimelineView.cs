using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Tapybara.App.Localization;
using Tapybara.Core.Audio;
using Tapybara.Core.Editing;

using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using ComboBox = System.Windows.Controls.ComboBox;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Tapybara.App.Controls;

public sealed record TimelineInterval(object Key, TimeSpan Start, TimeSpan End, string Hint, bool Estimated = false, bool Uncertain = false);
public sealed record TimelineLane(string Label, Brush Brush, IReadOnlyList<TimelineInterval> Intervals,
    WaveformEnvelope? Waveform = null, string? Status = null, object? Key = null);

/// <summary>Источники и интервалы речи; ключи непрозрачны, аудио/документы остаются у экрана.</summary>
public sealed partial class AudioTimelineView : Grid
{
    private sealed record Choice(string Hint, TimelineInterval Interval);
    private readonly ComboBox _mode = new() { MinWidth = 120 };
    private readonly Wpf.Ui.Controls.Button _info = new() { Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Info24, FontSize = 16 } };
    private readonly TextBlock _help = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, Tokens.Space1, 0, Tokens.Space1) };
    private readonly ComboBox _choices = new() { DisplayMemberPath = nameof(Choice.Hint), MinWidth = 0 };
    private readonly Wpf.Ui.Controls.Button _play = new() { Margin = new Thickness(Tokens.Space2, 0, 0, 0) };
    private readonly Grid _choiceRow = new();
    private readonly Surface _surface;
    private readonly AudioTimelineSelection _selection = new();
    private IReadOnlyList<TimelineLane> _sourceLanes = [];
    private IReadOnlyList<TimelineLane> _speakerLanes = [];
    private bool _updating;
    private bool _canPlay;
    private Popup? _helpPopup;

    public event Action<TimeSpan>? SeekRequested;
    public event Action<object>? IntervalRequested;
    public bool SpeakersMode => _mode.SelectedIndex == 1;
    public object? SelectedKey => (_choices.SelectedItem as Choice)?.Interval.Key;

    public AudioTimelineView()
    {
        _surface = new Surface(this);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ControlSizing.Apply(_mode);
        ControlSizing.Apply(_info, iconOnly: true);
        ControlSizing.Apply(_choices);
        ControlSizing.Apply(_play);
        var scroll = new ScrollViewer { Content = _surface, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
        SetRow(scroll, 2);
        Children.Add(scroll);
        _choiceRow.ColumnDefinitions.Add(new ColumnDefinition());
        _choiceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _choiceRow.Margin = new Thickness(0, Tokens.Space1, 0, Tokens.Space2);
        _choiceRow.Children.Add(_choices);
        SetColumn(_play, 1);
        _choiceRow.Children.Add(_play);
        SetRow(_choiceRow, 3);
        Children.Add(_choiceRow);
        _mode.SelectionChanged += (_, _) => ApplyMode();
        _choices.SelectionChanged += (_, _) =>
        {
            if (!_updating) { _selection.ClearTiming(); _surface.InvalidateVisual(); }
            RefreshRangeControls();
            _play.IsEnabled = _canPlay && SelectedKey is not null;
        };
        _play.Click += (_, _) =>
        {
            if (SelectedKey is { } key) { Activate(key); }
        };
        _info.Click += (_, _) =>
        {
            if (_helpPopup is { IsOpen: true }) { _helpPopup.IsOpen = false; return; }
            var text = new TextBlock { Text = _help.Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
            var popup = new Popup { Child = new PopoverSurface { Child = text }, StaysOpen = false, AllowsTransparency = true };
            _helpPopup = popup;
            AnchoredPopup.Open(popup, _info, above: true);
        };
        InitializeRangeControls();
        _help.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        ApplyLanguage();
        ApplyMode();
    }

    public void SetData(TimeSpan duration, IReadOnlyList<TimelineLane> sources, IReadOnlyList<TimelineLane> speakers, bool canPlay, object? context = null)
    {
        if (_rangePopup is not null) { _rangePopup.IsOpen = false; }
        if (_helpPopup is not null) { _helpPopup.IsOpen = false; }
        object? key = SelectedKey;
        _sourceLanes = sources;
        _speakerLanes = speakers;
        _canPlay = canPlay;
        _surface.CancelPointer();
        _surface.CancelSeek();
        duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        _selection.SetContext(context, duration, canPlay);
        if (_selection.Range is { } range && !ValidSource(range.Source)) { _selection.ClearRange(); }
        _surface.Duration = duration;
        _updating = true;
        try
        {
            Choice[] choices = [.. speakers.SelectMany(lane => lane.Intervals
                .Where(span => span.Start >= TimeSpan.Zero && span.End > span.Start && span.Start < duration)
                .Select(span => new Choice(lane.Label + " · " + span.Hint, span))).OrderBy(c => c.Interval.Start)];
            _choices.ItemsSource = choices;
            _choices.SelectedItem = choices.FirstOrDefault(c => Equals(c.Interval.Key, key));
        }
        finally { _updating = false; }
        TimelineInterval? selected = SelectedInterval;
        _selection.ReconcileTiming(selected?.Key, selected?.Start ?? default, selected?.End ?? default);
        ApplyMode();
    }

    public void SetPosition(TimeSpan position) { _surface.Position = position; _surface.InvalidateVisual(); }
    public void ShowSpeakers(bool speakers) { _mode.SelectedIndex = speakers ? 1 : 0; ApplyMode(); }

    private void ApplyMode()
    {
        _surface.CancelPointer();
        _surface.SetLanes(SpeakersMode ? _speakerLanes : _sourceLanes);
        _choiceRow.Visibility = SpeakersMode ? Visibility.Visible : Visibility.Collapsed;
        _play.IsEnabled = _canPlay && SelectedKey is not null;
        _help.Text = SpeakersMode ? L.S.TimelineSpeakersHelp : L.S.TimelineSourcesHelp;
        _help.MaxWidth = 360;
        _info.ToolTip = _help;
        AutomationProperties.SetHelpText(_mode, _help.Text);
        RefreshRangeControls();
    }

    public void ApplyLanguage()
    {
        bool speakers = SpeakersMode;
        _mode.ItemsSource = new[] { L.S.TimelineSources, L.S.TimelineSpeakers };
        _mode.SelectedIndex = speakers ? 1 : 0;
        _play.Content = L.S.PlaybackFragment;
        ApplyRangeLanguage();
        AutomationProperties.SetName(_mode, L.S.TimelineTitle);
        AutomationProperties.SetName(_info, L.S.TimelineTitle);
        AutomationProperties.SetName(_choices, L.S.TimelineIntervals);
        AutomationProperties.SetName(_surface, L.S.PlaybackSeek);
        ApplyMode();
    }

    /// <summary>Повторно ищем текущий ключ: меню/выбор не могут отправить удалённый интервал.</summary>
    public void Activate(object key)
    {
        if (!_canPlay) { return; }
        Choice? choice = _choices.Items.Cast<Choice>().FirstOrDefault(c => Equals(c.Interval.Key, key));
        if (choice is null) { return; }
        _choices.SelectedItem = choice;
        IntervalRequested?.Invoke(key);
    }

    private sealed partial class Surface(AudioTimelineView owner) : System.Windows.Controls.Control
    {
        private const double LabelWidth = 96;
        private const double RulerHeight = 24;
        private const double LaneHeight = 34;
        private IReadOnlyList<TimelineLane> _lanes = [];
        private DrawingGroup? _drawing;
        private readonly List<(Rect Rect, TimelineInterval Interval)> _rects = [];
        public TimeSpan Duration { get; set; }
        public TimeSpan Position { get; set; }
        private TimeSpan? _pendingSeek;
        public void CancelSeek() { _pendingSeek = null; }
        private void CommitSeek()
        {
            TimeSpan? pending = _pendingSeek;
            _pendingSeek = null;
            if (pending is { } target && owner._canPlay) { owner.SeekRequested?.Invoke(target); }
        }
        private double PlotWidth => Math.Max(0, ActualWidth - LabelWidth - 8);

        public void SetLanes(IReadOnlyList<TimelineLane> lanes)
        {
            _lanes = lanes;
            _rects.Clear();
            Height = RulerHeight + Math.Max(1, lanes.Count) * LaneHeight;
            _drawing = null;
            InvalidateVisual();
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == ActualWidthProperty || e.Property == ForegroundProperty || e.Property == FontSizeProperty)
            {
                _drawing = null;
                InvalidateVisual();
            }
        }

        private double X(TimeSpan time) => LabelWidth + PlotWidth * Math.Clamp(time.TotalSeconds / Math.Max(Duration.TotalSeconds, 0.001), 0, 1);
        private TimeSpan Time(double x) => TimeSpan.FromSeconds(Math.Clamp((x - LabelWidth) / Math.Max(PlotWidth, 1), 0, 1) * Duration.TotalSeconds);
        private void Label(DrawingContext drawing, string text, double x, double y, double width)
        {
            if (width <= 0) { return; }
            var formatted = new FormattedText(text, L.S.Formatting, System.Windows.FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), Math.Min(FontSize, 12), Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = width, MaxTextHeight = 22, Trimming = TextTrimming.CharacterEllipsis };
            drawing.DrawText(formatted, new Point(x, y));
        }

        private Point? _hover;

        private DrawingGroup Build()
        {
            var group = new DrawingGroup();
            _rects.Clear();
            using DrawingContext drawing = group.Open();
            var grid = new Pen(Foreground, 0.5);
            for (int tick = 0; tick <= 4; tick++)
            {
                TimeSpan time = Duration * (tick / 4.0);
                double x = X(time);
                drawing.DrawLine(grid, new Point(x, RulerHeight), new Point(x, Height));
                Label(drawing, Stamp(time), tick == 4 ? Math.Max(LabelWidth, x - 48) : x, 1, Math.Min(52, PlotWidth / 4));
            }
            for (int row = 0; row < _lanes.Count; row++)
            {
                TimelineLane lane = _lanes[row];
                double y = RulerHeight + row * LaneHeight;
                Label(drawing, lane.Label, 0, y + 8, LabelWidth - 6);
                if (lane.Waveform is { Peaks.Count: > 0 } waveform && PlotWidth > 0)
                {
                    int columns = Math.Max(1, (int)Math.Ceiling(PlotWidth * Math.Min(1, waveform.Duration.TotalSeconds / Math.Max(Duration.TotalSeconds, 0.001))));
                    var pen = new Pen(lane.Brush, 1);
                    for (int column = 0; column < columns; column++)
                    {
                        double secondsPerPixel = Duration.TotalSeconds / PlotWidth;
                        int from = Math.Min(waveform.Peaks.Count - 1, (int)(column * secondsPerPixel / waveform.BinDuration.TotalSeconds));
                        int to = Math.Min(waveform.Peaks.Count, Math.Max(from + 1, (int)Math.Ceiling((column + 1) * secondsPerPixel / waveform.BinDuration.TotalSeconds)));
                        float peak = 0;
                        for (int bin = from; bin < to; bin++) { peak = Math.Max(peak, waveform.Peaks[bin]); }
                        double x = LabelWidth + column;
                        drawing.DrawLine(pen, new Point(x, y + LaneHeight / 2 - peak * 14), new Point(x, y + LaneHeight / 2 + peak * 14));
                    }
                }
                else if (lane.Status is { } status) { Label(drawing, status, LabelWidth, y + 8, PlotWidth); }
                foreach (TimelineInterval span in lane.Intervals.Where(s => s.Start >= TimeSpan.Zero && s.End > s.Start && s.Start < Duration))
                {
                    var rect = new Rect(X(span.Start), y + 6, Math.Max(1, X(span.End) - X(span.Start)), LaneHeight - 12);
                    var border = new Pen(lane.Brush, 1) { DashStyle = span.Estimated || span.Uncertain ? DashStyles.Dot : DashStyles.Solid };
                    drawing.PushOpacity(0.35);
                    drawing.DrawRoundedRectangle(lane.Brush, null, rect, 3, 3);
                    drawing.Pop();
                    drawing.DrawRoundedRectangle(null, border, rect, 3, 3);
                    if ((span.Estimated || span.Uncertain) && rect.Width > 12)
                    {
                        Label(drawing, span.Estimated ? "~" : "?", rect.Left + 2, y + 7, Math.Min(14, rect.Width - 2));
                    }
                    _rects.Add((rect, span));
                }
            }
            return group;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            _drawing ??= Build();
            drawingContext.DrawDrawing(_drawing);
            DrawSelection(drawingContext);
            foreach (var hit in _rects.Where(r => Equals(r.Interval.Key, owner.SelectedKey)))
            {
                drawingContext.DrawRoundedRectangle(null, new Pen(Foreground, 2), hit.Rect, 3, 3);
                DrawHandles(drawingContext, hit);
            }
            if (Duration > TimeSpan.Zero)
            {
                double x = X(_pendingSeek ?? Position);
                drawingContext.DrawLine(new Pen(Foreground, 1.5), new Point(x, RulerHeight), new Point(x, Height));
            }
            if (_hover is { } hover)
            {
                double x = Math.Clamp(hover.X + Tokens.Space3, LabelWidth, Math.Max(LabelWidth, ActualWidth - 86));
                double y = Math.Clamp(hover.Y - 30, 0, Math.Max(0, ActualHeight - 26));
                Brush background = TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush ?? Brushes.White;
                drawingContext.DrawRoundedRectangle(background, new Pen(Foreground, 0.5), new Rect(x, y, 84, 26), 4, 4);
                Label(drawingContext, AudioTime.Format(Time(hover.X)), x + 4, y + 4, 76);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point point = e.GetPosition(this);
            _hover = point.X >= LabelWidth && Duration > TimeSpan.Zero ? point : null;
            if (MovePointer(point)) { ToolTip = null; e.Handled = true; return; }
            UpdatePointerCursor(point);
            TimelineInterval? span = _rects.FirstOrDefault(r => r.Rect.Contains(point)).Interval;
            ToolTip = span?.Hint;
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = null;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
            Point point = e.GetPosition(this);
            if (point.X < LabelWidth || Duration <= TimeSpan.Zero || !owner._canPlay) { return; }
            BeginPointer(point);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape)
            {
                CancelSeek();
                bool dragging = _pointerStart is not null;
                CancelPointer();
                if (!dragging) { owner.ClearRange(); }
                e.Handled = true; return;
            }
            if (!owner._canPlay) { return; }
            TimeSpan? target = e.Key switch
            {
                Key.Left => (_pendingSeek ?? Position) - TimeSpan.FromSeconds(1), Key.Right => (_pendingSeek ?? Position) + TimeSpan.FromSeconds(1),
                Key.Home => TimeSpan.Zero, Key.End => Duration, _ => null,
            };
            if (target is { } time)
            {
                _pendingSeek = time < TimeSpan.Zero ? TimeSpan.Zero : time > Duration ? Duration : time;
                InvalidateVisual();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && owner.SelectedKey is { } key) { owner.Activate(key); e.Handled = true; }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.Key is Key.Left or Key.Right or Key.Home or Key.End) { CommitSeek(); e.Handled = true; }
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            CommitSeek();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            Focusable = true;
            KeyboardNavigation.SetIsTabStop(this, true);
            SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
        }

        private static string Stamp(TimeSpan value) => string.Create(CultureInfo.InvariantCulture,
            $"{(int)value.TotalMinutes:00}:{value.Seconds:00}");
    }
}
