using System.Globalization;
using System.Text.RegularExpressions;

namespace Tapybara.Core.Calls;

/// <summary>
/// Правка текста готового транскрипта — без повторного распознавания.
/// </summary>
/// <remarks>
/// <para>
/// Раньше исправить слово в звонке можно было только заменой в словаре, а
/// к самому звонку она не применялась: окно предлагало распознать звонок
/// заново — минуты работы Whisper ради одного слова. Текст уже есть в
/// <c>transcript.json</c>; правка — это правка текста.
/// </para>
/// <para>
/// Модель коверкает редкое слово по-разному на протяжении одного звонка —
/// «рикстат», «рекстат» — и ещё склоняет: «рекстата». Поэтому исправление
/// одного места предлагает и похожие написания из того же звонка
/// (<see cref="SimilarForms"/>): человек снимает лишние, остальное
/// меняется разом.
/// </para>
/// </remarks>
public static partial class TranscriptEdit
{
    /// <summary>Написание слова в звонке и сколько раз оно встречается.</summary>
    public sealed record WordForm(string Form, int Count);

    /// <summary>
    /// Какая доля длины слова может отличаться, чтобы оно считалось похожим.
    /// </summary>
    /// <remarks>
    /// Треть: «рикстат» и «рекстата» различаются в двух буквах из восьми —
    /// это одно слово, искажённое и склонённое. Больше — и в «похожие»
    /// начинают попадать просто другие слова той же длины.
    /// </remarks>
    private const double SimilarShare = 0.3;

    /// <summary>Короче этого слово похожим не бывает — только тем же самым.</summary>
    /// <remarks>У «кот» и «код» одна буква разницы, и это разные слова.</remarks>
    private const int MinSimilarLength = 4;

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’\-]*", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    /// <summary>
    /// Написания в звонке, похожие на <paramref name="word"/>: оно само и его искажения.
    /// </summary>
    /// <returns>Само слово первым, дальше — чаще встречающиеся раньше.</returns>
    /// <remarks>
    /// <para>
    /// <paramref name="word"/> может быть и несколькими словами. Whisper
    /// режет незнакомое слово на два — «рек стат», «рек стады», — а в другом
    /// месте того же звонка пишет слитно. Поэтому сравниваются куски текста
    /// на слово короче и длиннее искомого, склеенные без пробелов: «Рикстат»
    /// находит и «рек стат», и «рекстата».
    /// </para>
    /// <para>
    /// Из пересекающихся кусков одной реплики берётся самый похожий: иначе
    /// «рекстат не» считался бы наравне с «рекстат».
    /// </para>
    /// </remarks>
    public static IReadOnlyList<WordForm> SimilarForms(CallTranscript transcript, string word)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(word);

        int size = WordPattern().Matches(word).Count;
        string self = Fold(word.Trim());
        string target = Glued(word);
        if (size == 0 || target.Length == 0)
        {
            return [];
        }

        var found = new Dictionary<string, (string Form, int Count)>(StringComparer.Ordinal);
        foreach (CallLine line in transcript.Lines)
        {
            foreach (string piece in Pieces(line.Text, target, size))
            {
                string folded = Fold(piece);
                found[folded] = found.TryGetValue(folded, out var known)
                    ? (known.Form, known.Count + 1)
                    : (piece.ToLower(CultureInfo.InvariantCulture), 1);
            }
        }

        return
        [
            .. found
                .OrderByDescending(f => f.Key == self)
                .ThenByDescending(f => f.Value.Count)
                .ThenBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => new WordForm(f.Value.Form, f.Value.Count)),
        ];
    }

    /// <summary>Куски реплики, похожие на искомое, — без пересечений.</summary>
    private static IEnumerable<string> Pieces(string text, string target, int size)
    {
        MatchCollection words = WordPattern().Matches(text);
        var candidates = new List<(int From, int To, int Distance, int Extra)>();

        for (int from = 0; from < words.Count; from++)
        {
            for (int count = Math.Max(1, size - 1); count <= size + 1 && from + count <= words.Count; count++)
            {
                int to = from + count - 1;

                // Кусок — слова подряд через пробел. Запятая или точка между
                // ними — это уже два разных места фразы, а не одно слово.
                bool joined = true;
                for (int i = from; i < to && joined; i++)
                {
                    int gap = words[i].Index + words[i].Length;
                    joined = string.IsNullOrWhiteSpace(text[gap..words[i + 1].Index]);
                }

                if (!joined)
                {
                    break;
                }

                // «и», «в», «не» в кусок из нескольких слов не входят: «рекстат и»
                // на букву ближе к «Рикстати», чем «рекстат», но заменить его
                // значило бы съесть союз.
                if (count > 1 && Enumerable.Range(from, count).Any(i => words[i].Length <= 2))
                {
                    continue;
                }

                string glued = string.Concat(Enumerable.Range(from, count).Select(i => Fold(words[i].Value)));

                // Несколько слов и искомое похожи, только если начинаются
                // одинаково: «в битре» — не «битре», хоть и в одну букву.
                if ((count > 1 || size > 1) && glued[0] != target[0])
                {
                    continue;
                }

                if (IsSimilar(target, glued))
                {
                    candidates.Add((from, to, Distance(target, glued), Math.Abs(count - size)));
                }
            }
        }

        var taken = new List<(int From, int To)>();
        // Сначала куски в столько же слов, сколько в искомом, потом — самые
        // похожие: лишнее слово берётся, только если без него похожего нет.
        foreach (var c in candidates.OrderBy(c => c.Extra).ThenBy(c => c.Distance).ThenBy(c => c.To - c.From))
        {
            if (taken.Any(t => c.From <= t.To && t.From <= c.To))
            {
                continue;
            }

            taken.Add((c.From, c.To));
            int start = words[c.From].Index;
            yield return text[start..(words[c.To].Index + words[c.To].Length)];
        }
    }

    /// <summary>Слова подряд, без пробелов, в сложенном виде: «Рек стат» → «рекстат».</summary>
    private static string Glued(string text) =>
        string.Concat(WordPattern().Matches(text).Select(m => Fold(m.Value)));

    /// <summary>
    /// Целые слова, которых касается выделение, — для правки нескольких слов сразу.
    /// </summary>
    /// <returns>Начало и длина, или <c>null</c>, если слов там нет.</returns>
    /// <remarks>Выделение редко ложится ровно по словам: полслова справа — всё равно это слово.</remarks>
    public static (int Start, int Length)? PhraseAt(string text, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (length <= 0)
        {
            return WordAt(text, start);
        }

        int end = start + length;
        List<Match> touched = [.. WordPattern().Matches(text).Where(m => m.Index < end && m.Index + m.Length > start)];
        if (touched.Count == 0)
        {
            return null;
        }

        int from = touched[0].Index;
        return (from, touched[^1].Index + touched[^1].Length - from);
    }

    /// <summary>
    /// Заменить написания во всём звонке — целыми словами, без учёта регистра.
    /// </summary>
    /// <returns>Новый транскрипт и сколько вхождений заменено.</returns>
    /// <remarks>Так же, как применяется словарь замен, — чтобы звонок и словарь не расходились.</remarks>
    public static (CallTranscript Transcript, int Replaced) Replace(
        CallTranscript transcript,
        IReadOnlyCollection<string> forms,
        string replacement)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(forms);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacement);

        List<Regex> patterns =
        [
            .. forms
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(f => f.Length) // длинное первым: «рекстата» раньше «рекстат»
                .Select(Whole),
        ];

        int replaced = 0;
        List<CallLine> lines =
        [
            .. transcript.Lines.Select(line =>
            {
                string text = line.Text;
                foreach (Regex pattern in patterns)
                {
                    text = pattern.Replace(text, _ =>
                    {
                        replaced++;
                        return replacement;
                    });
                }

                return text == line.Text ? line : line with { Text = text };
            }),
        ];

        return replaced == 0
            ? (transcript, 0)
            : (transcript with { Lines = lines, EditedByHand = true }, replaced);
    }

    /// <summary>
    /// Применить словарь замен к готовому транскрипту.
    /// </summary>
    /// <returns>Новый транскрипт и сколько вхождений исправлено.</returns>
    /// <remarks>
    /// Для звонков, распознанных до того, как слово попало в словарь.
    /// Раньше единственным способом было распознать звонок заново.
    /// </remarks>
    public static (CallTranscript Transcript, int Replaced) ApplyDictionary(
        CallTranscript transcript,
        IReadOnlyDictionary<string, string> replacements)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(replacements);

        int total = 0;
        foreach (IGrouping<string, KeyValuePair<string, string>> target in replacements
                     .Where(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
                     .GroupBy(p => p.Value, StringComparer.Ordinal))
        {
            // Вариант, совпадающий с правильным словом, — не ошибка: «Regstat»
            // → «Regstat» посчитался бы исправлением на каждом месте.
            List<string> forms = [.. target.Select(p => p.Key).Where(k => !k.Equals(target.Key, StringComparison.OrdinalIgnoreCase))];
            (transcript, int replaced) = Replace(transcript, forms, target.Key);
            total += replaced;
        }

        return (transcript, total);
    }

    /// <summary>Заменить одно место одной реплики — «только здесь».</summary>
    /// <param name="transcript">Транскрипт.</param>
    /// <param name="line">Реплика, как она лежит в транскрипте.</param>
    /// <param name="start">С какого символа текста реплики.</param>
    /// <param name="length">Сколько символов.</param>
    /// <param name="replacement">Чем заменить.</param>
    public static CallTranscript ReplaceAt(CallTranscript transcript, CallLine line, int start, int length, string replacement)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + length, line.Text.Length);

        return EditLine(transcript, line, line.Text[..start] + replacement + line.Text[(start + length)..]);
    }

    /// <summary>Заменить текст реплики целиком.</summary>
    public static CallTranscript EditLine(CallTranscript transcript, CallLine line, string text)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(text);

        string trimmed = text.Trim();
        if (trimmed == line.Text)
        {
            return transcript;
        }

        return transcript with
        {
            Lines = [.. transcript.Lines.Select(l => l == line ? l with { Text = trimmed } : l)],
            EditedByHand = true,
        };
    }

    /// <summary>Слово под позицией в тексте — для правки по двойному щелчку.</summary>
    /// <returns>Начало и длина слова, или <c>null</c>, если там не слово.</returns>
    public static (int Start, int Length)? WordAt(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (Match match in WordPattern().Matches(text))
        {
            if (index >= match.Index && index <= match.Index + match.Length)
            {
                return (match.Index, match.Length);
            }
        }

        return null;
    }

    internal static bool IsSimilar(string a, string b)
    {
        if (a == b)
        {
            return true;
        }

        if (Math.Min(a.Length, b.Length) < MinSimilarLength)
        {
            return false;
        }

        // Искажения и падежи меняют середину и конец слова, а не начало:
        // «битре» — «битра», но не «тире». С другой первой буквой похожим
        // считается только слово с одной опечаткой.
        int allowed = a[0] == b[0]
            ? Math.Max(1, (int)Math.Round(Math.Max(a.Length, b.Length) * SimilarShare))
            : 1;
        return Math.Abs(a.Length - b.Length) <= allowed && Distance(a, b) <= allowed;
    }

    /// <summary>Сколько букв поменять, вставить или удалить, чтобы получить одно слово из другого.</summary>
    internal static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>Регистр и «ё» не делают слово другим.</summary>
    private static string Fold(string word) =>
        word.ToLower(CultureInfo.InvariantCulture).Replace('ё', 'е');

    private static Regex Whole(string form) => new(
        $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(form)}(?![\p{{L}}\p{{N}}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
}
