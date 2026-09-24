namespace Tapybara.Core.Speech;

/// <summary>
/// Кто произнёс распознанную реплику.
/// </summary>
/// <remarks>
/// Разделитель голосов и распознавание режут запись по-разному и независимо:
/// первый — по смене говорящего, второе — по паузам и длине окна. Границы
/// не совпадают почти никогда, поэтому реплику надо не «найти в списке», а
/// сопоставить по перекрытию.
/// </remarks>
public static class SpeakerAssignment
{
    /// <summary>
    /// Номер голоса, звучавшего на отрезке дольше остальных,
    /// или <c>null</c>, если ни один не перекрывается.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Именно «дольше всех», а не «тот, что был в начале». Реплика часто
    /// начинается на хвосте чужой фразы — люди перебивают друг друга, — и
    /// выбор по первому касанию отдавал бы такие реплики не тому человеку.
    /// </para>
    /// <para>
    /// «Дольше всех» — в сумме по всем отрезкам голоса, а не по одному самому
    /// длинному. Разделитель режет речь одного человека на куски; раньше
    /// реплика, где человек говорил тремя кусками по две секунды, доставалась
    /// тому, кто вставил одну фразу на четыре.
    /// </para>
    /// </remarks>
    public static int? For(TimeSpan start, TimeSpan end, IReadOnlyList<SpeakerSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        if (spans.Count == 0 || end <= start)
        {
            return null;
        }

        var totals = new Dictionary<int, TimeSpan>();
        foreach (SpeakerSpan span in spans)
        {
            TimeSpan overlap = Overlap(start, end, span.Start, span.End);
            if (overlap > TimeSpan.Zero)
            {
                totals[span.Speaker] = totals.GetValueOrDefault(span.Speaker) + overlap;
            }
        }

        return totals.Count == 0 ? null : totals.MaxBy(t => t.Value).Key;
    }

    private static TimeSpan Overlap(TimeSpan aStart, TimeSpan aEnd, TimeSpan bStart, TimeSpan bEnd)
    {
        TimeSpan start = aStart > bStart ? aStart : bStart;
        TimeSpan end = aEnd < bEnd ? aEnd : bEnd;
        return end > start ? end - start : TimeSpan.Zero;
    }
}
