using System.Globalization;
using System.Text.RegularExpressions;
using Tapybara.Core.Speech;

namespace Tapybara.Core.Calls;

/// <summary>
/// Правка текста готового транскрипта — без повторного распознавания.
/// </summary>
/// <remarks>
/// <para>
/// Текст уже есть в <c>transcript.json</c>; исправить слово — значит
/// исправить текст, а не запускать Whisper заново на минуты ради одного
/// слова.
/// </para>
/// <para>
/// Модель коверкает редкое слово по-разному на протяжении одного звонка —
/// «рикстат», «рекстат» — и ещё склоняет: «рекстата». Поэтому исправление
/// одного места предлагает и похожие написания из того же звонка
/// (<see cref="SimilarForms"/>): человек снимает лишние, остальное
/// меняется разом.
/// </para>
/// </remarks>
public static class TranscriptEdit
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

    /// <summary>Длиннее этого слово в кусок из нескольких слов не входит само по себе: «и», «в», «не».</summary>
    private const int MaxFunctionWord = 2;

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
    /// Из пересекающихся кусков одной реплики берётся кусок того же размера,
    /// что и искомое, а из них — самый похожий: иначе «рекстат не» считался
    /// бы наравне с «рекстат».
    /// </para>
    /// </remarks>
    public static IReadOnlyList<WordForm> SimilarForms(CallTranscript transcript, string word)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(word);

        string[] target = [.. Words.Pattern().Matches(word).Select(m => Words.Fold(m.Value))];
        if (target.Length == 0)
        {
            return [];
        }

        string self = string.Join(' ', target);
        var found = new Dictionary<string, WordForm>(StringComparer.Ordinal);
        foreach (CallLine line in transcript.Lines)
        {
            foreach (string piece in Pieces(line.Text, string.Concat(target), target.Length))
            {
                string key = Words.Fold(piece);
                found[key] = found.TryGetValue(key, out WordForm? known)
                    ? known with { Count = known.Count + 1 }
                    : new WordForm(piece.ToLower(CultureInfo.InvariantCulture), 1);
            }
        }

        return
        [
            .. found
                .OrderByDescending(f => f.Key == self)
                .ThenByDescending(f => f.Value.Count)
                .ThenBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => f.Value),
        ];
    }

    /// <summary>Куски реплики, похожие на искомое, — без пересечений.</summary>
    /// <param name="text">Текст реплики.</param>
    /// <param name="target">Искомое, сложенное и склеенное без пробелов.</param>
    /// <param name="size">Сколько в искомом слов.</param>
    private static IEnumerable<string> Pieces(string text, string target, int size)
    {
        Match[] words = Words.Pattern().Matches(text).ToArray();
        string[] folded = [.. words.Select(w => Words.Fold(w.Value))];
        bool[] spaced = [.. words.Skip(1).Select((right, i) => IsWhiteSpaceBetween(text, words[i], right))];
        var candidates = new List<(int From, int To, int Distance, int Extra)>();

        for (int from = 0; from < words.Length; from++)
        {
            for (int count = Math.Max(1, size - 1); count <= size + 1 && from + count <= words.Length; count++)
            {
                int to = from + count - 1;

                // Кусок — слова подряд через пробел. Запятая или точка между
                // ними — это уже два разных места фразы, а не одно слово.
                if (!spaced[from..to].All(s => s))
                {
                    break;
                }

                // «и», «в», «не» в кусок из нескольких слов не входят: «рекстат и»
                // на букву ближе к «Рикстати», чем «рекстат», но заменить его
                // значило бы съесть союз.
                if (count > 1 && folded[from..(to + 1)].Any(w => w.Length <= MaxFunctionWord))
                {
                    continue;
                }

                string glued = string.Concat(folded[from..(to + 1)]);

                // Несколько слов и искомое похожи, только если начинаются
                // одинаково: «в битре» — не «битре», хоть и в одну букву.
                if ((count > 1 || size > 1) && glued[0] != target[0])
                {
                    continue;
                }

                if (DistanceIfSimilar(target, glued) is { } distance)
                {
                    candidates.Add((from, to, distance, Math.Abs(count - size)));
                }
            }
        }

        var taken = new List<(int From, int To)>();
        foreach (var c in candidates.OrderBy(c => c.Extra).ThenBy(c => c.Distance).ThenBy(c => c.To - c.From))
        {
            if (!taken.Any(t => c.From <= t.To && t.From <= c.To))
            {
                taken.Add((c.From, c.To));
                yield return text[words[c.From].Index..(words[c.To].Index + words[c.To].Length)];
            }
        }
    }

    private static bool IsWhiteSpaceBetween(string text, Match left, Match right) =>
        string.IsNullOrWhiteSpace(text[(left.Index + left.Length)..right.Index]);

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
        List<Match> touched = [.. Words.Pattern().Matches(text).Where(m => m.Index < end && m.Index + m.Length > start)];
        if (touched.Count == 0)
        {
            return null;
        }

        int from = touched[0].Index;
        return (from, touched[^1].Index + touched[^1].Length - from);
    }

    /// <summary>
    /// Заменить написания во всём звонке — целыми словами, без учёта регистра и «ё».
    /// </summary>
    /// <returns>Новый транскрипт и сколько вхождений заменено.</returns>
    /// <remarks>Тем же шаблоном, что и словарь замен в диктовках (<see cref="Words.AnyOf"/>).</remarks>
    public static (CallTranscript Transcript, int Replaced) Replace(
        CallTranscript transcript,
        IReadOnlyCollection<string> forms,
        string replacement)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(forms);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacement);

        List<string> wanted = [.. forms.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase)];
        return wanted.Count == 0
            ? (transcript, 0)
            : ReplaceAll(transcript, Words.AnyOf(wanted), _ => replacement);
    }

    /// <summary>Заменить в репликах всё, что находит шаблон; отметить звонок правленым.</summary>
    internal static (CallTranscript Transcript, int Replaced) ReplaceAll(
        CallTranscript transcript,
        Regex pattern,
        Func<string, string> replacementFor)
    {
        int replaced = 0;
        var lines = new List<CallLine>(transcript.Lines.Count);
        foreach (CallLine line in transcript.Lines)
        {
            string text = pattern.Replace(line.Text, match =>
            {
                replaced++;
                return replacementFor(match.Value);
            });
            lines.Add(text == line.Text ? line : line with { Text = text });
        }

        return replaced == 0
            ? (transcript, 0)
            : (transcript with { Lines = lines, EditedByHand = true }, replaced);
    }

    /// <summary>
    /// Применить словарь замен к готовому транскрипту — к звонкам, распознанным
    /// до того, как слово попало в словарь.
    /// </summary>
    /// <returns>Новый транскрипт и сколько вхождений исправлено.</returns>
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
    /// <param name="line">Реплика, как её видит окно.</param>
    /// <param name="start">С какого символа текста реплики.</param>
    /// <param name="length">Сколько символов.</param>
    /// <param name="replacement">Чем заменить.</param>
    public static CallTranscript ReplaceAt(CallTranscript transcript, CallLine line, int start, int length, string replacement)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + length, line.Text.Length);

        return EditLine(transcript, line, line.Text[..start] + replacement + line.Text[(start + length)..]);
    }

    /// <summary>Заменить текст реплики целиком.</summary>
    /// <remarks>Реплики нет — транскрипт не меняется и правленым не отмечается.</remarks>
    public static CallTranscript EditLine(CallTranscript transcript, CallLine line, string text)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(text);

        string trimmed = text.Trim();
        if (trimmed == line.Text || !transcript.Lines.Any(l => l.IsSameAs(line)))
        {
            return transcript;
        }

        return transcript with
        {
            Lines = [.. transcript.Lines.Select(l => l.IsSameAs(line) ? l with { Text = trimmed } : l)],
            EditedByHand = true,
        };
    }

    /// <summary>Убрать реплику: вздох, эхо, выдуманное на тишине.</summary>
    /// <remarks>Реплики нет — транскрипт не меняется и правленым не отмечается.</remarks>
    public static CallTranscript RemoveLine(CallTranscript transcript, CallLine line)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(line);

        if (!transcript.Lines.Any(l => l.IsSameAs(line)))
        {
            return transcript;
        }

        return transcript with
        {
            Lines = [.. transcript.Lines.Where(l => !l.IsSameAs(line))],
            EditedByHand = true,
        };
    }

    /// <summary>Слово под позицией в тексте — для правки по двойному щелчку.</summary>
    /// <returns>Начало и длина слова, или <c>null</c>, если там не слово.</returns>
    public static (int Start, int Length)? WordAt(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Words.Pattern().Matches(text)
            .Where(m => index >= m.Index && index <= m.Index + m.Length)
            .Select(m => ((int, int)?)(m.Index, m.Length))
            .FirstOrDefault();
    }

    /// <summary>Сколько правок отделяет похожее слово; <c>null</c> — не похоже.</summary>
    private static int? DistanceIfSimilar(string a, string b)
    {
        if (a == b)
        {
            return 0;
        }

        if (Math.Min(a.Length, b.Length) < MinSimilarLength)
        {
            return null;
        }

        // Искажения и падежи меняют середину и конец слова, а не начало:
        // «битре» — «битра», но не «тире». С другой первой буквой похожим
        // считается только слово с одной опечаткой.
        int allowed = a[0] == b[0]
            ? Math.Max(1, (int)Math.Round(Math.Max(a.Length, b.Length) * SimilarShare))
            : 1;
        if (Math.Abs(a.Length - b.Length) > allowed)
        {
            return null;
        }

        int distance = Distance(a, b);
        return distance <= allowed ? distance : null;
    }

    /// <summary>Сколько букв поменять, вставить или удалить, чтобы получить одно слово из другого.</summary>
    private static int Distance(string a, string b)
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
}
