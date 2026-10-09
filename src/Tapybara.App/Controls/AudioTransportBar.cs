using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Tapybara.App.Localization;
using Wpf.Ui.Controls;

using Slider = System.Windows.Controls.Slider;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Tapybara.App.Controls;

/// <summary>Общий транспорт: нативный ползунок, предварительная позиция и явные запросы без аудио/файлов.</summary>
public sealed class AudioTransportBar : Grid
{
    private readonly Slider _seek = new() { Minimum = 0, SmallChange = 1, LargeChange = 5, IsMoveToPointEnabled = true };
    private readonly Wpf.Ui.Controls.Button _play = new() { Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 } };
    private readonly Wpf.Ui.Controls.Button _stop = new() { Icon = new SymbolIcon { Symbol = SymbolRegular.Stop24 } };
    private readonly Wpf.Ui.Controls.Button _rewind = new() { Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowCounterclockwise24 } };
    private readonly Wpf.Ui.Controls.Button _range = new();
    private readonly Wpf.Ui.Controls.Button _forward = new() { Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowClockwise24 } };
    private readonly ContentControl _tools = new();
    private (TimeSpan Start, TimeSpan End)? _excerpt;
    public UIElement? Tools { get => _tools.Content as UIElement; set => _tools.Content = value; }
    private readonly TextBlock _rangeText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _clock = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ToggleButton _follow = new() { IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleButton _loop = new();
    public bool Loop { get => _loop.IsChecked == true; set => _loop.IsChecked = value; }
    private bool _updating;
    private bool _scrubbing;
    private bool _playing;
    private TimeSpan _duration;

    public event Action? ToggleRequested;
    public event Action? StopRequested;
    public event Action? RewindRequested;
    public event Action? ForwardRequested;
    public event Action? WholeCallRequested;
    public event Action<TimeSpan>? SeekRequested;
    public event Action? FollowRequested;
    public bool Follow { get => _follow.IsChecked == true; set => _follow.IsChecked = value; }
    public bool IsScrubbing => _scrubbing;

    public AudioTransportBar()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (bool stretch in new[] { false, false, true, false, false, false })
        { ColumnDefinitions.Add(new ColumnDefinition { Width = stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto }); }
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        foreach (Wpf.Ui.Controls.Button button in new[] { _play, _stop, _rewind, _forward })
        {
            ControlSizing.Apply(button, iconOnly: true);
            button.Margin = new Thickness(0, 0, Tokens.Space1, 0);
            buttons.Children.Add(button);
        }
        ControlSizing.Apply(_loop);
        _loop.Margin = new Thickness(0, 0, Tokens.Space1, 0);
        buttons.Children.Add(_loop);
        Children.Add(buttons);
        _clock.FontSize = Tokens.Caption;
        _clock.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
        _clock.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        _clock.Margin = new Thickness(Tokens.Space1, 0, Tokens.Space2, 0);
        SetColumn(_clock, 1);
        Children.Add(_clock);
        _seek.MinWidth = 48;
        _seek.VerticalAlignment = VerticalAlignment.Center;
        _seek.Margin = new Thickness(0, 0, Tokens.Space2, 0);
        SetColumn(_seek, 2);
        Children.Add(_seek);
        SetColumn(_tools, 3);
        Children.Add(_tools);
        ControlSizing.Apply(_follow);
        _follow.Margin = new Thickness(Tokens.Space1, 0, 0, 0);
        SetColumn(_follow, 4);
        Children.Add(_follow);
        ControlSizing.Apply(_range, iconOnly: true);
        _range.Icon = new SymbolIcon { Symbol = SymbolRegular.Dismiss24, FontSize = 16 };
        _range.Margin = new Thickness(Tokens.Space1, 0, 0, 0);
        SetColumn(_range, 5);
        Children.Add(_range);
        _range.Visibility = _status.Visibility = Visibility.Collapsed;
        SetRow(_status, 2);
        SetColumnSpan(_status, 6);
        Children.Add(_status);
        SizeChanged += (_, _) => ArrangeRail();
        _play.Click += (_, _) => ToggleRequested?.Invoke();
        _stop.Click += (_, _) => StopRequested?.Invoke();
        _rewind.Click += (_, _) => RewindRequested?.Invoke();
        _forward.Click += (_, _) => ForwardRequested?.Invoke();
        _range.Click += (_, _) => WholeCallRequested?.Invoke();
        _follow.Checked += (_, _) => FollowRequested?.Invoke();
        _seek.PreviewMouseLeftButtonDown += (_, _) => _scrubbing = true;
        _seek.PreviewMouseLeftButtonUp += (_, _) => CommitScrub();
        _seek.LostMouseCapture += (_, _) => CommitScrub();
        _seek.PreviewKeyDown += (_, e) =>
        {
            if (IsSeekKey(e.Key)) { _scrubbing = true; }
        };
        _seek.PreviewKeyUp += (_, e) =>
        {
            if (IsSeekKey(e.Key)) { CommitScrub(); }
        };
        _seek.LostKeyboardFocus += (_, _) => CommitScrub();
        _seek.ValueChanged += (_, _) =>
        {
            if (_updating) { return; }
            UpdateClock(TimeSpan.FromSeconds(_seek.Value));
            if (!_scrubbing) { SeekRequested?.Invoke(TimeSpan.FromSeconds(_seek.Value)); }
        };
        ApplyLanguage();
        SetState(TimeSpan.Zero, TimeSpan.Zero, false, false);
    }

    public void ResetInteraction()
    {
        _scrubbing = false;
        if (_seek.IsMouseCaptureWithin) { Mouse.Capture(null); }
    }

    private static bool IsSeekKey(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down
        or Key.Home or Key.End or Key.PageUp or Key.PageDown;

    private void CommitScrub()
    {
        if (!_scrubbing) { return; }
        _scrubbing = false;
        SeekRequested?.Invoke(TimeSpan.FromSeconds(_seek.Value));
    }

    public void SetState(TimeSpan position, TimeSpan duration, bool playing, bool available,
        (TimeSpan Start, TimeSpan End)? range = null, string? status = null)
    {
        _updating = true;
        try
        {
            _duration = duration;
            bool changed = _playing != playing;
            _playing = playing;
            _seek.Maximum = Math.Max(0, duration.TotalSeconds);
            if (!_scrubbing) { _seek.Value = Math.Clamp(position.TotalSeconds, 0, _seek.Maximum); }
            _seek.IsEnabled = _play.IsEnabled = _stop.IsEnabled = _rewind.IsEnabled = _forward.IsEnabled = available;
            _excerpt = range;
            _loop.IsEnabled = available && range is not null;
            UpdateClock(TimeSpan.FromSeconds(_seek.Value));
            _range.Visibility = range is null ? Visibility.Collapsed : Visibility.Visible;
            if (range is { } span) { _rangeText.Text = L.S.PlaybackRange + " " + Stamp(span.Start) + "–" + Stamp(span.End); }
            _range.ToolTip = _rangeText.Text + " · " + L.S.PlaybackWholeCall;
            _status.Text = status ?? string.Empty;
            _status.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
            if (changed) { ApplyLanguage(); }
            _play.ToolTip = _playing ? L.S.PlaybackPause : range is not null ? L.S.TimelinePlayRange : L.S.PlaybackPlay;
            AutomationProperties.SetName(_play, (string)_play.ToolTip);
        }
        finally { _updating = false; }
    }

    public void ApplyLanguage()
    {
        _play.ToolTip = _playing ? L.S.PlaybackPause : L.S.PlaybackPlay;
        _play.Icon = new SymbolIcon { Symbol = _playing ? SymbolRegular.Pause24 : SymbolRegular.Play24 };
        _stop.ToolTip = L.S.PlaybackStop;
        _rewind.ToolTip = L.S.PlaybackRewind;
        _forward.ToolTip = L.S.PlaybackForward;
        _follow.ToolTip = L.S.PlaybackFollowHint;
        _follow.Content = L.S.PlaybackFollow;
        _loop.Content = L.S.PlaybackLoop;
        _loop.ToolTip = L.S.PlaybackLoopHelp;
        AutomationProperties.SetName(_loop, L.S.PlaybackLoop);
        _range.ToolTip = L.S.PlaybackWholeCall;
        AutomationProperties.SetName(_follow, L.S.PlaybackFollow);
        AutomationProperties.SetName(_forward, L.S.PlaybackForward);
        AutomationProperties.SetName(_play, (string)_play.ToolTip);
        AutomationProperties.SetName(_stop, L.S.PlaybackStop);
        AutomationProperties.SetName(_rewind, L.S.PlaybackRewind);
        AutomationProperties.SetName(_seek, L.S.PlaybackSeek);
        AutomationProperties.SetName(_range, L.S.PlaybackWholeCall);
        AutomationProperties.SetName(_clock, L.S.PlaybackPosition);
    }

    private void ArrangeRail()
    {
        bool compact = ActualWidth < 820;
        SetRow(_clock, compact ? 1 : 0);
        SetColumn(_clock, compact ? 0 : 1);
        SetColumnSpan(_clock, compact ? 6 : 1);
        _clock.Margin = compact ? new Thickness(0, Tokens.Space1, 0, 0) : new Thickness(Tokens.Space1, 0, Tokens.Space2, 0);
        SetColumn(_seek, compact ? 1 : 2);
        SetColumnSpan(_seek, compact ? 2 : 1);
    }

    private void UpdateClock(TimeSpan position)
    {
        // Резервируем всю шкалу длительности: цифры и переход минут не двигают ползунок.
        string sample = Stamp(_duration) + " / " + Stamp(_duration) + (_excerpt is null ? string.Empty : " · " + L.S.PlaybackRange);
        var measure = new System.Windows.Media.FormattedText(sample, L.S.Formatting, System.Windows.FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface(_clock.FontFamily, _clock.FontStyle, _clock.FontWeight, _clock.FontStretch),
            _clock.FontSize, System.Windows.Media.Brushes.Black, 1);
        _clock.Width = Math.Ceiling(measure.WidthIncludingTrailingWhitespace) + Tokens.Space1;
        _clock.Text = Stamp(position) + " / " + Stamp(_duration) + (_excerpt is null ? string.Empty : " · " + L.S.PlaybackRange);
        _clock.ToolTip = _excerpt is { } range ? Stamp(range.Start) + "–" + Stamp(range.End) : null;
    }
    private static string Stamp(TimeSpan value) => string.Create(L.S.Formatting,
        $"{(int)value.TotalMinutes:00}:{value.Seconds:00}.{value.Milliseconds / 100:0}");
}
