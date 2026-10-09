using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Tapybara.App.Localization;
using Tapybara.Core.Editing;

namespace Tapybara.App.Controls;

/// <summary>Масштаб и прокрутка физического времени не меняют транскрипт или позицию транспорта.</summary>
public sealed partial class AudioTimelineView
{
    private readonly AudioViewport _viewport = new();
    private readonly System.Windows.Controls.Primitives.ScrollBar _pan = new() { Orientation = System.Windows.Controls.Orientation.Horizontal, Minimum = 0 };
    private readonly Wpf.Ui.Controls.Button _zoomIn = new() { Content = "+" };
    private readonly Wpf.Ui.Controls.Button _zoomOut = new() { Content = "−" };
    private readonly Wpf.Ui.Controls.Button _fit = new();
    private object? _viewportContext;
    private bool _updatingPan;
    public TimeSpan ViewStart => _viewport.Start;
    public TimeSpan ViewEnd => _viewport.End;

    private void InitializeViewport()
    {
        foreach (var button in new[] { _fit, _zoomIn, _zoomOut })
        {
            ControlSizing.Apply(button, iconOnly: button != _fit);
            button.Margin = new Thickness(0, 0, button == _fit ? Tokens.Space3 : Tokens.Space1, Tokens.Space1);
            _rangeRow.Children.Insert(2, button);
        }
        _zoomIn.Click += (_, _) => Zoom(2);
        _zoomOut.Click += (_, _) => Zoom(0.5);
        _fit.Click += (_, _) => FitView();
        _pan.Margin = new Thickness(96, Tokens.Space1, 8, Tokens.Space1);
        SetRow(_pan, 3); Children.Add(_pan);
        _pan.ValueChanged += (_, _) => { if (!_updatingPan) { PanTo(TimeSpan.FromSeconds(_pan.Value)); } };
        UpdateViewport();
    }

    public void Zoom(double factor, double anchor = 0.5) { _surface.CancelPointer(); _viewport.Zoom(factor, anchor); UpdateViewport(); }
    public void PanTo(TimeSpan start) { _viewport.PanTo(start); UpdateViewport(); }
    public void FitView() { _surface.CancelPointer(); _viewport.Fit(); UpdateViewport(); }

    private void UpdateViewport()
    {
        _updatingPan = true;
        try
        {
            _pan.Maximum = Math.Max(0, (_viewport.Duration - _viewport.Length).TotalSeconds);
            _pan.ViewportSize = _viewport.Length.TotalSeconds;
            _pan.SmallChange = Math.Max(.1, _viewport.Length.TotalSeconds / 10);
            _pan.LargeChange = _viewport.Length.TotalSeconds;
            _pan.Value = _viewport.Start.TotalSeconds;
            _pan.Visibility = _viewport.IsZoomed ? Visibility.Visible : Visibility.Collapsed;
            _zoomIn.IsEnabled = _viewport.Length.TotalSeconds > Math.Min(1, _viewport.Duration.TotalSeconds);
            _zoomOut.IsEnabled = _fit.IsEnabled = _viewport.IsZoomed;
            _surface.RefreshDrawing();
        }
        finally { _updatingPan = false; }
    }

    private sealed partial class Surface
    {
        private double? _panOrigin;
        private TimeSpan _panTime;
        public void RefreshDrawing() { _drawing = null; InvalidateVisual(); }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                owner.Zoom(e.Delta > 0 ? 2 : 0.5, (e.GetPosition(this).X - LabelWidth) / Math.Max(1, PlotWidth));
                e.Handled = true;
            }
            else if (owner._viewport.IsZoomed)
            {
                owner.PanTo(owner.ViewStart - owner._viewport.Length * (e.Delta / 120.0 * .15));
                e.Handled = true;
            }
            else { base.OnMouseWheel(e); }
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.ChangedButton != MouseButton.Middle || !owner._viewport.IsZoomed) { return; }
            CancelPointer();
            _panOrigin = e.GetPosition(this).X;
            _panTime = owner.ViewStart;
            if (!CaptureMouse()) { _panOrigin = null; }
            e.Handled = true;
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.ChangedButton != MouseButton.Middle || _panOrigin is null) { return; }
            _panOrigin = null;
            ReleaseMouseCapture(); e.Handled = true;
        }
    }
}
