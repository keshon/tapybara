namespace Tapybara.Core.Editing;

/// <summary>Окно времени: масштабирование вокруг точки и ограниченная прокрутка.</summary>
public sealed class AudioViewport
{
    public TimeSpan Duration { get; private set; }
    public TimeSpan Start { get; private set; }
    public TimeSpan Length { get; private set; }
    public TimeSpan End => Start + Length;
    public bool IsZoomed => Length < Duration;

    public void SetDuration(TimeSpan duration, bool reset = false)
    {
        bool fit = !IsZoomed;
        Duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        if (reset || fit) { Fit(); }
        else
        {
            Length = Length > Duration ? Duration : Length;
            PanTo(Start);
        }
    }

    public void Fit() { Start = TimeSpan.Zero; Length = Duration; }
    public TimeSpan TimeAt(double fraction) => Start + Length * Math.Clamp(fraction, 0, 1);
    public double FractionAt(TimeSpan time) => Length > TimeSpan.Zero ? (time - Start).TotalSeconds / Length.TotalSeconds : 0;

    public void Zoom(double factor, double anchor = 0.5)
    {
        if (!double.IsFinite(factor) || factor <= 0 || !double.IsFinite(anchor)) { return; }
        anchor = Math.Clamp(anchor, 0, 1);
        TimeSpan time = TimeAt(anchor);
        double minimum = Math.Min(1, Duration.TotalSeconds);
        Length = TimeSpan.FromSeconds(Math.Clamp(Length.TotalSeconds / factor, minimum, Duration.TotalSeconds));
        PanTo(time - Length * anchor);
    }

    public void PanTo(TimeSpan start) => Start = TimeSpan.FromTicks(Math.Clamp(start.Ticks, 0, (Duration - Length).Ticks));
}
