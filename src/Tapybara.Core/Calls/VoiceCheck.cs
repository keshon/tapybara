namespace Tapybara.Core.Calls;

/// <summary>
/// Проверка реплик собеседников по слепку голоса: та ли это реплика, тот ли человек.
/// </summary>
/// <remarks>
/// <para>
/// Разделитель голосов решает по отрезкам, а реплики режет Whisper по своим
/// паузам, и границы у них не совпадают. Реплика целиком отдаётся голосу, и
/// ошибка разделителя на её отрезке переходит в транскрипт: чужая фраза
/// уходит не тому человеку, а оттуда — в цитаты, по которым человек этот
/// голос называет. Проверка сравнивает слепок каждой реплики со «средним»
/// голосом и ловит такие реплики.
/// </para>
/// <para>
/// Центр голоса — не среднее всех его реплик, а среднее без худшей трети:
/// ошибочно отданные реплики — это и есть выбросы, и включать их в эталон,
/// по которому их ищут, значило бы подтягивать эталон к ошибке.
/// </para>
/// </remarks>
public static class VoiceCheck
{
    /// <summary>
    /// Короче этого слепок реплики не измеряем.
    /// </summary>
    /// <remarks>
    /// Замерено на записях автора (<c>bench linecheck</c>). На звонке с одним
    /// собеседником все реплики от четырёх секунд похожи на его голос не
    /// меньше чем на 0,6, а среди двух-трёхсекундных попадаются 0,43–0,5 —
    /// на такой длине слепок шумит, и сомнение было бы ложным.
    /// </remarks>
    public static readonly TimeSpan MinLine = TimeSpan.FromSeconds(4);

    /// <summary>
    /// С какого сходства со своим голосом реплика вне сомнений.
    /// </summary>
    /// <remarks>
    /// На тех же записях реплики «своего» человека — 0,8–0,93, чужого — 0,25–0,5,
    /// и между ними провал. 0,6 — в провале, ближе к чужим: лучше пропустить
    /// сомнение, чем пометить половину верных реплик.
    /// </remarks>
    public const double Sure = 0.6;

    /// <summary>
    /// На сколько реплика должна быть ближе к другому голосу, чтобы переехать к нему сама.
    /// </summary>
    /// <remarks>
    /// Переезд без спроса — только при явном перевесе. На звонке, верно
    /// разделённом на три голоса, такая реплика нашлась одна: 0,41 к своему
    /// голосу и 0,76 к другому. Спорные случаи остаются сомнением, которое
    /// видит человек, а не решением, которое за него принято.
    /// </remarks>
    public const double MoveLead = 0.15;

    /// <summary>
    /// Со скольки сомнительных реплик считать, что голосов больше, чем нашли.
    /// </summary>
    /// <remarks>
    /// Реплики, не похожие ни на один голос, — почти всегда ещё один человек:
    /// его не отметили, и разделитель искал меньше голосов, чем было. На
    /// звонке с тремя собеседниками, где отметили одного, таких было больше
    /// половины; при верном числе голосов — одна из ста.
    /// </remarks>
    private const int MissingVoiceLines = 3;

    /// <summary>И доля сомнительных среди измеренных — тот же довод.</summary>
    private const double MissingVoiceShare = 0.1;

    /// <summary>Какая доля худших реплик не входит в центр голоса.</summary>
    private const double Trim = 1.0 / 3;

    /// <summary>Сходство реплики со своим голосом и с остальными.</summary>
    public sealed record LineScore(CallLine Line, string Voice, double Own, IReadOnlyDictionary<string, double> Others)
    {
        /// <summary>Насколько реплика ближе к своему голосу, чем к ближайшему чужому.</summary>
        public double Margin => Others.Count == 0 ? Own : Own - Others.Values.Max();
    }

    /// <summary>Итог замера: реплики и центры голосов.</summary>
    public sealed record Report(IReadOnlyList<LineScore> Lines, IReadOnlyDictionary<string, float[]> Centres);

    /// <summary>Звук одной реплики.</summary>
    public static float[] Slice(float[] audio, CallLine line, int sampleRate = 16_000)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(line);

        int from = Math.Clamp((int)(line.Start.TotalSeconds * sampleRate), 0, audio.Length);
        int to = Math.Clamp((int)(line.End.TotalSeconds * sampleRate), from, audio.Length);
        return audio[from..to];
    }

    /// <summary>Снять слепки реплик собеседников и сравнить их с голосами.</summary>
    /// <param name="lines">Реплики транскрипта.</param>
    /// <param name="printOf">Слепок реплики, или <c>null</c>, если речи в ней слишком мало.</param>
    public static Report Measure(IReadOnlyList<CallLine> lines, Func<CallLine, float[]?> printOf)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(printOf);

        var measured = new List<(CallLine Line, string Voice, float[] Print)>();
        foreach (CallLine line in lines)
        {
            if (line.Channel != CallChannel.Theirs || line.End - line.Start < MinLine)
            {
                continue;
            }

            if (printOf(line) is { Length: > 0 } print)
            {
                measured.Add((line, line.Voice ?? CallVoices.WholeOtherSide, VoiceBook.Normalize(print)));
            }
        }

        Dictionary<string, float[]> centres = measured
            .GroupBy(m => m.Voice)
            .ToDictionary(g => g.Key, g => Centre([.. g.Select(m => m.Print)]));

        List<LineScore> scores =
        [
            .. measured.Select(m => new LineScore(
                m.Line,
                m.Voice,
                VoiceBook.Similarity(m.Print, centres[m.Voice]),
                centres.Where(c => c.Key != m.Voice).ToDictionary(c => c.Key, c => VoiceBook.Similarity(m.Print, c.Value)))),
        ];

        return new Report(scores, centres);
    }

    /// <summary>
    /// Проверить реплики собеседников: отметить сомнительные, явно чужие — переселить.
    /// </summary>
    /// <param name="transcript">Транскрипт с разметкой голосов (или без неё — весь чужой канал один голос).</param>
    /// <param name="printOf">Слепок реплики.</param>
    /// <remarks>
    /// Переселяются реплики только между уже найденными голосами. Если
    /// голосов не разделяли, переселять некуда, и сомнение — повод
    /// предложить разделение (<see cref="MoreVoicesLikely"/>).
    /// </remarks>
    public static CallTranscript Check(CallTranscript transcript, Func<CallLine, float[]?> printOf)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        Report report = Measure(transcript.Lines, printOf);
        // Реплики — записи и сравниваются по значению: две одинаковые
        // (тот же текст в ту же секунду) сошлись бы в один ключ.
        Dictionary<CallLine, LineScore> scores = report.Lines
            .GroupBy(s => s.Line)
            .ToDictionary(g => g.Key, g => g.First());

        return transcript with
        {
            VoicesChecked = true,
            Lines =
            [
                .. transcript.Lines.Select(line =>
                {
                    if (!scores.TryGetValue(line, out LineScore? score))
                    {
                        return line.Fit is null ? line : line with { Fit = null };
                    }

                    if (line.Voice is not null && score.Others.Count > 0)
                    {
                        KeyValuePair<string, double> best = score.Others.MaxBy(o => o.Value);
                        if (best.Value >= Sure && best.Value - score.Own >= MoveLead)
                        {
                            return line with { Voice = best.Key, Fit = Round(best.Value) };
                        }
                    }

                    return line with { Fit = Round(score.Own) };
                }),
            ],
        };
    }

    /// <summary>
    /// Похоже ли, что на той стороне было больше людей, чем нашли голосов.
    /// </summary>
    /// <param name="transcript">Проверенный транскрипт.</param>
    /// <param name="doubtful">Сколько речи под сомнением.</param>
    public static bool MoreVoicesLikely(CallTranscript transcript, out TimeSpan doubtful)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        List<CallLine> measured = [.. transcript.Lines.Where(l => l.Channel == CallChannel.Theirs && l.Fit is not null)];
        List<CallLine> unsure = [.. measured.Where(l => l.IsDoubtful)];

        doubtful = TimeSpan.FromTicks(unsure.Sum(l => (l.End - l.Start).Ticks));
        return unsure.Count >= MissingVoiceLines && unsure.Count >= measured.Count * MissingVoiceShare;
    }

    private static double Round(double value) => Math.Round(value, 2);

    /// <summary>Центр голоса: среднее слепков без худшей трети.</summary>
    internal static float[] Centre(IReadOnlyList<float[]> prints)
    {
        float[] mean = Mean(prints);
        if (prints.Count < 3)
        {
            return mean;
        }

        int keep = Math.Max(1, (int)Math.Ceiling(prints.Count * (1 - Trim)));
        return Mean([.. prints.OrderByDescending(p => VoiceBook.Similarity(p, mean)).Take(keep)]);
    }

    private static float[] Mean(IReadOnlyList<float[]> prints)
    {
        var sum = new float[prints[0].Length];
        foreach (float[] print in prints)
        {
            for (int i = 0; i < sum.Length; i++)
            {
                sum[i] += print[i];
            }
        }

        return VoiceBook.Normalize(sum);
    }
}
