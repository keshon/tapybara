using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tapybara.App.Localization;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;

namespace Tapybara.App.Controls;

/// <summary>Команды строки: переход, ограниченное/свободное прослушивание и правка.</summary>
public sealed class TranscriptLineActions : StackPanel
{
    private readonly TimestampScrubber _stamp = new();
    private readonly Button _locked = new();
    private readonly Button _more = new();
    public bool ShowMenuButton
    {
        get => _more.Visibility == Visibility.Visible;
        set => _more.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
    public static readonly DependencyProperty StampProperty = DependencyProperty.Register(nameof(Stamp), typeof(string), typeof(TranscriptLineActions),
        new PropertyMetadata(string.Empty, (target, args) => ((TranscriptLineActions)target)._stamp.Stamp = (string)args.NewValue));
    public string Stamp { get => (string)GetValue(StampProperty); set => SetValue(StampProperty, value); }
    public static readonly DependencyProperty IsExcerptPlayingProperty = DependencyProperty.Register(nameof(IsExcerptPlaying), typeof(bool), typeof(TranscriptLineActions),
        new PropertyMetadata(false, (target, args) => ((TranscriptLineActions)target).UpdateIcon(args.NewValue is true)));
    public bool IsExcerptPlaying { get => (bool)GetValue(IsExcerptPlayingProperty); set => SetValue(IsExcerptPlayingProperty, value); }
    public event Action<TranscriptLineActions, TimeSpan>? TimingShiftRequested;
    public event RoutedEventHandler? SeekRequested;
    public event RoutedEventHandler? LockedPlayRequested;
    public event RoutedEventHandler? MenuRequested;

    public TranscriptLineActions()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Top;
        Height = Tokens.ControlHeight;
        _stamp.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
        _stamp.FontSize = Tokens.Caption;
        foreach (var button in new[] { _locked, _more })
        {
            button.SetResourceReference(StyleProperty, "FlatLinkButton");
            button.Height = Tokens.ControlHeight;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
            button.Width = 22;
            Children.Add(button);
        }
        Children.Insert(1, _stamp);
        UpdateIcon(false);
        _more.Content = new SymbolIcon { Symbol = SymbolRegular.MoreHorizontal24, FontSize = 14 };
        _stamp.ToolTip = L.S.TimestampDragHelp;
        _locked.ToolTip = L.S.PlaybackLineOnly;
        _more.ToolTip = L.S.EditLineActions;
        foreach (var button in new[] { _locked, _more })
        { AutomationProperties.SetName(button, (string)button.ToolTip); }
        _stamp.SeekRequested += () => SeekRequested?.Invoke(this, new RoutedEventArgs());
        _stamp.ShiftRequested += shift => TimingShiftRequested?.Invoke(this, shift);
        _locked.Click += (_, _) => LockedPlayRequested?.Invoke(this, new RoutedEventArgs());
        _more.Click += (_, _) => MenuRequested?.Invoke(this, new RoutedEventArgs());
    }

    private void UpdateIcon(bool playing)
    {
        if (playing) { _locked.Content = new SymbolIcon { Symbol = SymbolRegular.Stop24, FontSize = 14 }; return; }
        _locked.Content = new SymbolIcon { Symbol = SymbolRegular.Play24, FontSize = 14 };
    }
}
