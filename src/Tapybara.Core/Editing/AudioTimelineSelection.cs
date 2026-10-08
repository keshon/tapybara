namespace Tapybara.Core.Editing;

public sealed record AudioRangeSelection(TimeSpan Start, TimeSpan End, object? Source);
public sealed record AudioTimingAdjustment(object Key, TimeSpan OriginalStart, TimeSpan OriginalEnd, TimeSpan Start, TimeSpan End);

/// <summary>Предпросмотр интервалов; не меняет документ и не запускает аудио.</summary>
public sealed class AudioTimelineSelection
{
    private object? _context;
    private TimeSpan _duration;
    private bool _enabled;
    private bool _startBoundary;
    public AudioRangeSelection? Range { get; private set; }
    public AudioTimingAdjustment? Timing { get; private set; }

    public void SetContext(object? context, TimeSpan duration, bool enabled)
    {
        if (!Equals(context, _context) || duration != _duration || !enabled) { Clear(); }
        _context = context;
        _duration = duration;
        _enabled = enabled && duration > TimeSpan.Zero;
    }

    public bool SelectRange(TimeSpan from, TimeSpan to, object? source)
    {
        if (!_enabled) { return false; }
        from = Clamp(from);
        to = Clamp(to);
        if (from > to) { (from, to) = (to, from); }
        if (from == to) { return false; }
        Range = new(from, to, source);
        return true;
    }

    public bool BeginTiming(object key, TimeSpan from, TimeSpan to, bool startBoundary)
    {
        if (!_enabled || from < TimeSpan.Zero || to > _duration || to - from < TimeSpan.FromMilliseconds(1)) { return false; }
        Timing = new(key, from, to, from, to);
        _startBoundary = startBoundary;
        return true;
    }

    public void MoveTiming(TimeSpan position)
    {
        if (Timing is not { } timing) { return; }
        TimeSpan minimum = TimeSpan.FromMilliseconds(1);
        Timing = _startBoundary
            ? timing with { Start = Clamp(position) < timing.End - minimum ? Clamp(position) : timing.End - minimum }
            : timing with { End = Clamp(position) > timing.Start + minimum ? Clamp(position) : timing.Start + minimum };
    }

    public void ReconcileTiming(object? key, TimeSpan from, TimeSpan to)
    {
        if (Timing is { } timing && (!Equals(timing.Key, key) || timing.OriginalStart != from || timing.OriginalEnd != to))
        { Timing = null; }
    }

    public AudioTimingAdjustment? CompleteTiming()
    {
        AudioTimingAdjustment? timing = Timing;
        Timing = null;
        return timing is not null && (timing.Start != timing.OriginalStart || timing.End != timing.OriginalEnd) ? timing : null;
    }

    public void ClearTiming() => Timing = null;
    public void ClearRange() => Range = null;
    public void Clear() { ClearRange(); ClearTiming(); }
    private TimeSpan Clamp(TimeSpan time) => time < TimeSpan.Zero ? TimeSpan.Zero : time > _duration ? _duration : time;
}
