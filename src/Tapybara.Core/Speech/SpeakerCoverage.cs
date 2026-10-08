namespace Tapybara.Core.Speech;

/// <summary>Непересекающийся участок с набором активных кластеров, без догадки о словах.</summary>
public sealed record SpeakerCoverageInterval(TimeSpan Start, TimeSpan End, IReadOnlyList<int> Speakers);

/// <summary>Покрытие одного кластера, без двойного учёта дубликатов его участков.</summary>
public sealed record SpeakerCoverageTotal(int Speaker, TimeSpan Duration);

/// <summary>Сырые свидетельства о голосах; длительности не являются вероятностями.</summary>
public sealed record SpeakerCoverageResult(
    IReadOnlyList<SpeakerCoverageInterval> Intervals,
    IReadOnlyList<SpeakerCoverageTotal> Totals,
    TimeSpan Covered,
    TimeSpan Simultaneous);

/// <summary>
/// Отличает смену голосов внутри реплики от их одновременного звучания.
/// Не режет текст: для этого нужны проверенные границы слов.
/// </summary>
public static class SpeakerCoverage
{
    /// <summary>Обрезать свидетельства по окну и измерить объединение участков.</summary>
    public static SpeakerCoverageResult Measure(TimeSpan start, TimeSpan end, IReadOnlyList<SpeakerSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        if (end <= start) { return new([], [], TimeSpan.Zero, TimeSpan.Zero); }

        var events = new SortedDictionary<TimeSpan, Dictionary<int, int>>();
        foreach (SpeakerSpan span in spans)
        {
            TimeSpan from = span.Start > start ? span.Start : start;
            TimeSpan to = span.End < end ? span.End : end;
            if (to <= from) { continue; }
            Add(from, span.Speaker, 1);
            Add(to, span.Speaker, -1);
        }

        var active = new Dictionary<int, int>();
        var totals = new Dictionary<int, TimeSpan>();
        var intervals = new List<SpeakerCoverageInterval>();
        TimeSpan previous = start;
        TimeSpan covered = TimeSpan.Zero;
        TimeSpan simultaneous = TimeSpan.Zero;
        foreach ((TimeSpan at, Dictionary<int, int> changes) in events)
        {
            if (at > previous && active.Count > 0)
            {
                TimeSpan duration = at - previous;
                int[] speakers = [.. active.Keys.Order()];
                covered += duration;
                if (speakers.Length > 1) { simultaneous += duration; }
                foreach (int speaker in speakers) { totals[speaker] = totals.GetValueOrDefault(speaker) + duration; }
                if (intervals.Count > 0 && intervals[^1].End == previous && intervals[^1].Speakers.SequenceEqual(speakers))
                { intervals[^1] = intervals[^1] with { End = at }; }
                else { intervals.Add(new(previous, at, speakers)); }
            }
            foreach ((int speaker, int delta) in changes)
            {
                int count = active.GetValueOrDefault(speaker) + delta;
                if (count == 0) { active.Remove(speaker); }
                else { active[speaker] = count; }
            }
            previous = at;
        }
        return new(intervals, [.. totals.OrderBy(t => t.Key).Select(t => new SpeakerCoverageTotal(t.Key, t.Value))], covered, simultaneous);

        void Add(TimeSpan at, int speaker, int delta)
        {
            if (!events.TryGetValue(at, out Dictionary<int, int>? changes)) { events[at] = changes = []; }
            changes[speaker] = changes.GetValueOrDefault(speaker) + delta;
        }
    }
}
