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
    public sealed record Report(IReadOnlyList<LineScore> Lines, IReadOnlyDictionary<string, float[]> Centres)
    {
        public IReadOnlyDictionary<CallLine, string> Suggestions { get; init; } = new Dictionary<CallLine, string>();
    }

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
    public static Report Measure(
        IReadOnlyList<CallLine> lines,
        Func<CallLine, float[]?> printOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(printOf);

        var measured = new List<(CallLine Line, string Voice, float[] Print, bool ForConfidence)>();
        var runs = new List<List<CallLine>>();
        foreach (var line in lines.Where(l => l.Channel == CallChannel.Theirs).OrderBy(l => l.Start))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.SpeechMix != Tapybara.Core.Speech.SpeechMix.None)
            {
                continue;
            }

            var run = runs.LastOrDefault();
            bool canJoin = run is not null && run[^1].Voice == line.Voice
                && !line.HumanEdits.HasFlag(CallLineEdits.Speaker)
                && !run[^1].HumanEdits.HasFlag(CallLineEdits.Speaker)
                && line.Start >= run[^1].End && line.Start - run[^1].End <= TimeSpan.FromMilliseconds(800)
                && line.End - run[0].Start <= TimeSpan.FromSeconds(12);
            if (canJoin)
            {
                run!.Add(line);
            }
            else
            {
                runs.Add([line]);
            }
        }
        var references = new List<(string Voice, float[] Print)>();
        var trusted = new List<(string Voice, float[] Print)>();
        bool hasConfirmedVoice = lines.Any(l => l.Voice is not null
            && l.HumanEdits.HasFlag(CallLineEdits.Speaker) && l.SpeechMix == Tapybara.Core.Speech.SpeechMix.None
            && l.End - l.Start >= MinLine);
        foreach (var run in runs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double speechSeconds = run.Sum(l => (l.End - l.Start).TotalSeconds);
            // Short samples may support a review suggestion, never a confidence verdict.
            if (speechSeconds < (hasConfirmedVoice ? 2.5 : MinLine.TotalSeconds))
            {
                continue;
            }

            var sample = run.Count == 1 ? run[0] : run[0] with
                { End = run[^1].End, Text = string.Join(" ", run.Select(l => l.Text)) };
            if (printOf(sample) is { Length: > 0 } print)
            {
                string voice = sample.Voice ?? CallVoices.WholeOtherSide;
                var normalized = VoiceBook.Normalize(print);
                if (speechSeconds >= MinLine.TotalSeconds)
                {
                    references.Add((voice, normalized));
                }

                if (speechSeconds >= MinLine.TotalSeconds && sample.Voice is not null
                    && sample.HumanEdits.HasFlag(CallLineEdits.Speaker))
                {
                    trusted.Add((voice, normalized));
                }

                foreach (var line in run)
                {
                    measured.Add((line, voice, normalized, speechSeconds >= MinLine.TotalSeconds));
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<string, float[]> centres = references
            .GroupBy(m => m.Voice)
            .ToDictionary(g => g.Key, g => Centre([.. g.Select(m => m.Print)]));

        var referenceCounts = references.GroupBy(m => m.Voice).ToDictionary(g => g.Key, g => g.Count());

        List<LineScore> scores =
        [
            // A lone window must not validate itself as a perfect voice match.
            .. measured.Where(m => referenceCounts.GetValueOrDefault(m.Voice) >= 2 && m.ForConfidence).Select(m => new LineScore(
                m.Line,
                m.Voice,
                VoiceBook.Similarity(m.Print, centres[m.Voice]),
                centres.Where(c => c.Key != m.Voice).ToDictionary(c => c.Key, c => VoiceBook.Similarity(m.Print, c.Value)))),
        ];

        var suggestions = new Dictionary<CallLine, string>();
        foreach (var m in measured.Where(m => !m.Line.HumanEdits.HasFlag(CallLineEdits.Speaker)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = trusted.GroupBy(t => t.Voice)
                .Select(g => (Voice: g.Key, Score: g.Max(t => VoiceBook.Similarity(m.Print, t.Print))))
                .OrderByDescending(t => t.Score).ToList();
            if (matches.Count == 0)
            {
                continue;
            }

            var best = matches[0];
            double alternative = matches.Count > 1 ? matches[1].Score : 0;
            if (referenceCounts.GetValueOrDefault(m.Voice) >= 2 && centres.TryGetValue(m.Voice, out var own))
            {
                alternative = Math.Max(alternative, VoiceBook.Similarity(m.Print, own));
            }

            if (best.Voice != m.Voice && best.Score >= VoiceBook.MatchThreshold
                && best.Score - alternative >= VoiceBook.MatchMargin)
            {
                suggestions[m.Line] = best.Voice;
            }
        }
        return new Report(scores, centres) { Suggestions = suggestions };
    }

    /// <summary>
    /// Проверить реплики собеседников и отметить сомнительные без смены подписей.
    /// </summary>
    /// <param name="transcript">Транскрипт с разметкой голосов (или без неё — весь чужой канал один голос).</param>
    /// <param name="printOf">Слепок реплики.</param>
    /// <remarks>
    /// Проверка не меняет личности: непохожие реплики остаются под сомнением.
    /// </remarks>
    public static CallTranscript Check(
        CallTranscript transcript,
        Func<CallLine, float[]?> printOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        Report report = Measure(transcript.Lines, printOf, cancellationToken);
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
                    if (line.HumanEdits.HasFlag(CallLineEdits.Speaker))
                    {
                        return line;
                    }
                    if (!scores.TryGetValue(line, out LineScore? score))
                    {
                        return line with { Fit = null, SuggestedVoice = report.Suggestions.GetValueOrDefault(line) };
                    }

                    return line with { Fit = Round(score.Own), SuggestedVoice = report.Suggestions.GetValueOrDefault(line) };
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
