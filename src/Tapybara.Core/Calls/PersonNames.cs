using System.Globalization;
using System.Text.RegularExpressions;

namespace Tapybara.Core.Calls;

/// <summary>Упоминание человека в тексте: форма имени, её падеж и сколько раз встретилась.</summary>
/// <param name="Form">Как написано, в нижнем регистре: «кириллу».</param>
/// <param name="Case">Падеж: 0 — именительный, дальше родительный, дательный, винительный, творительный, предложный.</param>
/// <param name="Count">Сколько раз.</param>
public sealed record NameMention(string Form, int Case, int Count);

/// <summary>
/// Имя человека в тексте звонков: его падежные формы и то же в новом имени.
/// </summary>
/// <remarks>
/// <para>
/// Переименовать человека — это не только подпись над репликами. О нём
/// говорят: «скажу Кириллу», «с Кириллом», — и после переименования в
/// «Шерифа» или анонимизации в «Участника 2» старое имя в тексте выдаёт всё.
/// </para>
/// <para>
/// Похожие слова (<see cref="TranscriptEdit.SimilarForms"/>) здесь не годятся:
/// «Костя» похож на «кости», а «Витя» — на «вита». Склонение знает, какие
/// формы бывают у имени, и заодно подсказывает, во что их превратить: был
/// «Кириллу» — станет «Шерифу», а не «Шериф».
/// </para>
/// <para>
/// Склоняются однословные русские имена на согласный, «-а», «-я», «-й»,
/// «-ь», с беглой гласной («Павел» — «Павла»). Остальное — «Участник 2»,
/// «Анн-Мари», латиница — ищется и ставится как есть. Падеж по одному слову
/// бывает неоднозначен («Кирилла» — родительный или винительный): берётся
/// первый, а человек видит итог каждой формы и может её снять.
/// </para>
/// </remarks>
public static partial class PersonNames
{
    /// <summary>Окончания по падежам: им., род., дат., вин., тв., пр.</summary>
    private static readonly string[] Consonant = ["", "а", "у", "а", "ом", "е"];
    private static readonly string[] Ya = ["я", "и", "е", "ю", "ей", "е"];
    private static readonly string[] A = ["а", "ы", "е", "у", "ой", "е"];
    private static readonly string[] Iy = ["й", "я", "ю", "я", "ем", "е"];
    private static readonly string[] Soft = ["ь", "я", "ю", "я", "ем", "е"];

    [GeneratedRegex(@"^\p{IsCyrillic}+$")]
    private static partial Regex CyrillicWord();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’\-]*", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    /// <summary>Имя в падеже; не склоняемое — как есть.</summary>
    public static string Inflect(string name, int grammaticalCase)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Forms(name.Trim()) is { } forms && grammaticalCase is >= 0 and < 6
            ? forms[grammaticalCase]
            : name.Trim();
    }

    /// <summary>
    /// Упоминания имени в репликах: какие формы встречаются и сколько раз.
    /// </summary>
    public static IReadOnlyList<NameMention> Find(IEnumerable<CallTranscript> calls, string name)
    {
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(name);

        string[] forms = Forms(name.Trim()) ?? [name.Trim()];
        var byForm = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = forms.Length - 1; i >= 0; i--)
        {
            byForm[Fold(forms[i])] = i; // одинаковые окончания — у первого падежа
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (CallTranscript call in calls)
        {
            foreach (CallLine line in call.Lines)
            {
                foreach (Match word in WordPattern().Matches(line.Text))
                {
                    string folded = Fold(word.Value);
                    if (byForm.ContainsKey(folded))
                    {
                        counts[folded] = counts.GetValueOrDefault(folded) + 1;
                    }
                }
            }
        }

        return
        [
            .. counts
                .OrderBy(c => byForm[c.Key])
                .Select(c => new NameMention(c.Key, byForm[c.Key], c.Value)),
        ];
    }

    /// <summary>Заменить упоминания старого имени новым — в том же падеже.</summary>
    /// <returns>Новый транскрипт и сколько мест заменено.</returns>
    public static (CallTranscript Transcript, int Replaced) Replace(
        CallTranscript transcript,
        IEnumerable<NameMention> mentions,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(mentions);

        int total = 0;
        foreach (NameMention mention in mentions)
        {
            (transcript, int replaced) = TranscriptEdit.Replace(transcript, [mention.Form], Inflect(newName, mention.Case));
            total += replaced;
        }

        return (transcript, total);
    }

    /// <summary>Есть ли человек в мете звонка — отмеченным или названным голосом.</summary>
    public static bool IsIn(CallSession session, string name)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Participants.Concat(session.VoiceNames.Values)
            .Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Переименовать человека в мете: в участниках и в именах голосов.</summary>
    /// <remarks>
    /// Новое имя может совпасть с кем-то, кто уже есть на звонке, — тогда это
    /// один человек (<see cref="CallPeople"/>), и в участниках он один.
    /// </remarks>
    public static CallSession Rename(CallSession session, string from, string to)
    {
        ArgumentNullException.ThrowIfNull(session);

        string Swap(string name) => string.Equals(name, from, StringComparison.OrdinalIgnoreCase) ? to : name;

        return session with
        {
            Participants = [.. session.Participants.Select(Swap).Distinct(StringComparer.OrdinalIgnoreCase)],
            VoiceNames = session.VoiceNames.ToDictionary(p => p.Key, p => Swap(p.Value)),
        };
    }

    /// <summary>
    /// Звонок, в котором люди подписаны псевдонимами, — для копии, которую отдают.
    /// </summary>
    /// <param name="session">Мета звонка.</param>
    /// <param name="transcript">Реплики.</param>
    /// <param name="aliases">Настоящее имя → псевдоним. Кого нет — остаётся как есть.</param>
    /// <param name="mentions">Заменить и упоминания в тексте реплик.</param>
    /// <remarks>
    /// Файлы звонка не трогаются: это правда, а меняется только то, что уходит
    /// наружу. Своё имя — такой же ключ: его подменяет тот, кто собирает
    /// транскрипт, передав псевдоним вместо имени владельца.
    /// </remarks>
    public static (CallSession Session, CallTranscript Transcript) Anonymize(
        CallSession session,
        CallTranscript transcript,
        IReadOnlyDictionary<string, string> aliases,
        bool mentions)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(aliases);

        foreach ((string name, string alias) in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.Equals(name, alias, StringComparison.Ordinal))
            {
                continue;
            }

            session = Rename(session, name, alias);
            if (mentions)
            {
                transcript = transcript with
                {
                    Lines = [.. transcript.Lines.Select(l => l with { Text = ReplaceIn(l.Text, name, alias) })],
                };
                session = session with { Title = session.Title is { } title ? ReplaceIn(title, name, alias) : null };
            }
        }

        return (session, transcript);
    }

    /// <summary>Заменить в строке все формы имени формами нового — в тех же падежах.</summary>
    private static string ReplaceIn(string text, string name, string newName)
    {
        string[] forms = Forms(name.Trim()) ?? [name.Trim()];
        return WordPattern().Replace(text, word =>
        {
            int grammaticalCase = Array.FindIndex(forms, f => Fold(f) == Fold(word.Value));
            return grammaticalCase < 0 ? word.Value : Inflect(newName, grammaticalCase);
        });
    }

    /// <summary>Все шесть падежных форм имени, или <c>null</c>, если имя не склоняется.</summary>
    private static string[]? Forms(string name)
    {
        if (!CyrillicWord().IsMatch(name) || name.Length < 2)
        {
            return null;
        }

        char last = char.ToLower(name[^1], CultureInfo.InvariantCulture);
        string stem = name[..^1];
        return last switch
        {
            'я' => [.. Ya.Select(e => stem + e)],
            'а' => [.. A.Select((e, i) => stem + AfterStem(stem, i, e))],
            'й' => [.. Iy.Select(e => stem + e)],
            'ь' => [.. Soft.Select(e => stem + e)],
            _ when "аеёиоуыэюя".Contains(last) => null,
            _ => [.. Consonant.Select((e, i) => (i == 0 ? name : Oblique(name)) + e)],
        };
    }

    /// <summary>
    /// Окончание имени на «-а» с поправкой на правописание: «Саши», не
    /// «Сашы»; «Машей», не «Машой».
    /// </summary>
    private static string AfterStem(string stem, int grammaticalCase, string ending)
    {
        char last = char.ToLower(stem[^1], CultureInfo.InvariantCulture);
        return grammaticalCase switch
        {
            1 when "гкхжшщч".Contains(last) => "и",
            4 when "жшщчц".Contains(last) => "ей",
            _ => ending,
        };
    }

    /// <summary>Основа косвенных падежей: у «Павел» — «Павл», у «Кирилл» — та же.</summary>
    private static string Oblique(string name) =>
        name.Length >= 4 && (name.EndsWith("ел", StringComparison.OrdinalIgnoreCase) || name.EndsWith("ек", StringComparison.OrdinalIgnoreCase))
            ? name[..^2] + name[^1]
            : name;

    private static string Fold(string word) =>
        word.ToLower(CultureInfo.InvariantCulture).Replace('ё', 'е');
}
