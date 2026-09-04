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
    /// Именно «дольше всех», а не «тот, что был в начале». Реплика часто
    /// начинается на хвосте чужой фразы — люди перебивают друг друга, — и
    /// выбор по первому касанию отдавал бы такие реплики не тому человеку.
    /// </remarks>
    public static int? For(TimeSpan start, TimeSpan end, IReadOnlyList<SpeakerSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        if (spans.Count == 0 || end <= start)
        {
            return null;
        }

        int best = -1;
        TimeSpan bestOverlap = TimeSpan.Zero;

        foreach (SpeakerSpan span in spans)
        {
            TimeSpan overlap = Overlap(start, end, span.Start, span.End);
            if (overlap <= bestOverlap)
            {
                continue;
            }

            bestOverlap = overlap;
            best = span.Speaker;
        }

        return best < 0 ? null : best;
    }

    /// <summary>
    /// Подписать каждый найденный голос.
    /// </summary>
    /// <param name="spans">Что нашёл разделитель.</param>
    /// <param name="names">Имена участников в том порядке, в каком их назвали.</param>
    /// <param name="unknown">Слово для голоса, которому имени не досталось.</param>
    /// <remarks>
    /// Соответствие номеров именам произвольно: разделитель нумерует голоса
    /// в порядке, который знает только он, а человек называл участников в
    /// своём. Единственное честное сопоставление — по времени первого
    /// появления: кто заговорил раньше, тот получает первое имя. Это
    /// угадывание, и оно на то и рассчитано, что подпись потом поправят.
    /// <para>
    /// Голосам сверх числа имён достаётся «Собеседник N», и N здесь — тоже
    /// номер появления, а не внутренний номер кластера. Кластеры нумеруются
    /// с дырами: на записи с единственным голосом разделитель вернул номер 2,
    /// и пользователь увидел бы «Собеседник 2» там, где собеседник один.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<int, string> Label(
        IReadOnlyList<SpeakerSpan> spans,
        IReadOnlyList<string> names,
        string unknown)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(names);

        var order = new List<int>();
        foreach (SpeakerSpan span in spans.OrderBy(s => s.Start))
        {
            if (!order.Contains(span.Speaker))
            {
                order.Add(span.Speaker);
            }
        }

        var result = new Dictionary<int, string>();
        for (int i = 0; i < order.Count; i++)
        {
            result[order[i]] = i < names.Count
                ? names[i]
                : $"{unknown} {(i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture)}";
        }

        return result;
    }

    private static TimeSpan Overlap(TimeSpan aStart, TimeSpan aEnd, TimeSpan bStart, TimeSpan bEnd)
    {
        TimeSpan start = aStart > bStart ? aStart : bStart;
        TimeSpan end = aEnd < bEnd ? aEnd : bEnd;
        return end > start ? end - start : TimeSpan.Zero;
    }
}
