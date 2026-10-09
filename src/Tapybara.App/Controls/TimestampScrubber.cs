using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Tapybara.App.Localization;

namespace Tapybara.App.Controls;

/// <summary>Нативный Thumb владеет захватом: щелчок ищет позицию, Alt-жест предлагает сдвиг.</summary>
public sealed class TimestampScrubber : Thumb
{
    public static readonly DependencyProperty DisplayStampProperty = DependencyProperty.Register(nameof(DisplayStamp), typeof(string), typeof(TimestampScrubber), new PropertyMetadata(string.Empty));
    public string DisplayStamp { get => (string)GetValue(DisplayStampProperty); set => SetValue(DisplayStampProperty, value); }
    private string _stamp = string.Empty;
    public string Stamp { get => _stamp; set { _stamp = value; if (!IsDragging) { DisplayStamp = value; } } }
    private object? _context;
    private bool _timing;
    private double _vertical;
    private double _distance;
    public event Action? SeekRequested;
    public event Action<TimeSpan>? ShiftRequested;

    public TimestampScrubber()
    {
        Height = Tokens.ControlHeight;
        Focusable = true;
        ToolTip = L.S.TimestampDragHelp;
        System.Windows.Automation.AutomationProperties.SetName(this, L.S.TimestampDragHelp);
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.PaddingProperty, new Thickness(6, 0, 6, 0));
        border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(DisplayStamp)) { Source = this });
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.FontFamilyProperty, new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"));
        text.SetValue(TextBlock.FontSizeProperty, Tokens.Caption);
        text.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        border.AppendChild(text);
        Template = new ControlTemplate(typeof(Thumb)) { VisualTree = border };
        DragStarted += (_, _) =>
        {
            Focus();
            _context = DataContext;
            _timing = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            _vertical = _distance = 0;
            Cursor = _timing ? System.Windows.Input.Cursors.SizeNS : System.Windows.Input.Cursors.Hand;
        };
        DragDelta += (_, e) =>
        {
            // Неподвижный Thumb отдаёт смещение от начала, а не приращение прошлого события.
            _vertical = -e.VerticalChange;
            _distance = Math.Max(_distance, Math.Abs(e.VerticalChange) + Math.Abs(e.HorizontalChange));
            if (_timing) { DisplayStamp = $"{(_vertical >= 0 ? "+" : "−")}{Math.Abs(_vertical / 10):0.0} s"; }
        };
        DragCompleted += (_, e) =>
        {
            bool same = ReferenceEquals(_context, DataContext);
            _context = null;
            DisplayStamp = _stamp;
            Cursor = System.Windows.Input.Cursors.Hand;
            if (e.Canceled || !same) { return; }
            if (_timing && Math.Abs(_vertical) >= 4)
            { ShiftRequested?.Invoke(TimeSpan.FromSeconds(Math.Round(_vertical / 10, 1))); }
            else if (!_timing && _distance < 4) { SeekRequested?.Invoke(); }
        };
        void CancelTimingDrag()
        {
            _context = null;
            CancelDrag();
            DisplayStamp = _stamp;
        }
        DataContextChanged += (_, _) => CancelTimingDrag();
        Unloaded += (_, _) => CancelTimingDrag();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && IsDragging) { CancelDrag(); e.Handled = true; }
            else if (e.Key is Key.Enter or Key.Space) { SeekRequested?.Invoke(); e.Handled = true; }
        };
        PreviewKeyUp += (_, e) =>
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftAlt or Key.RightAlt) { e.Handled = true; }
        };
    }
}
