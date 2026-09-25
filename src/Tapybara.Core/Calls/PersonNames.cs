using System.Globalization;
using System.Text.RegularExpressions;
using Tapybara.Core.Speech;

namespace Tapybara.Core.Calls;

/// <summary>Падеж — в каком виде имя стоит в тексте.</summary>
public enum GrammaticalCase
{
    Nominative,
    Genitive,
    Dative,
    Accusative,
    Instrumental,
    Prepositional,
}

/// <summary>Упоминание человека в тексте: форма имени, её падеж и сколько раз встретилась.</summary>
/// <param name="Form">Как написано, в нижнем регистре: «кириллу».</param>
/// <param name="Case">Падеж формы.</param>
/// <param name="Count">Сколько раз.</param>
public sealed record NameMention(string Form, GrammaticalCase Case, int Count);

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
    /// <summary>Окончания по падежам в порядке <see cref="GrammaticalCase"/>.</summary>
    private static readonly string[] Consonant = ["", "а", "у", "а", "ом", "е"];
    private static readonly string[] Ya = ["я", "и", "е", "ю", "ей", "е"];
    private static readonly string[] Iya = ["я", "и", "и", "ю", "ей", "и"];
    private static readonly string[] A = ["а", "ы", "е", "у", "ой", "е"];
    private static readonly string[] Iy = ["й", "я", "ю", "я", "ем", "е"];
    private static readonly string[] Iiy = ["й", "я", "ю", "я", "ем", "и"];
    private static readonly string[] Soft = ["ь", "я", "ю", "я", "ем", "е"];

    /// <summary>Имя на другую гласную — «Анри», «Нико» — не склоняется.</summary>
    private const string Vowels = "аеёиоуыэюя";

    [GeneratedRegex(@"^\p{IsCyrillic}+$")]
    private static partial Regex CyrillicWord();

    /// <summary>Имя в падеже; не склоняемое — как есть.</summary>
    public static string Inflect(string name, GrammaticalCase grammaticalCase)
    {
        ArgumentNullException.ThrowIfNull(name);

        return Forms(name.Trim()) is { } forms ? forms[(int)grammaticalCase] : name.Trim();
    }

    /// <summary>Упоминания имени в репликах: какие формы встречаются и сколько раз.</summary>
    public static IReadOnlyList<NameMention> Find(IEnumerable<CallTranscript> calls, string name)
    {
        ArgumentNullException.ThrowIfNull(calls);
        ArgumentNullException.ThrowIfNull(name);

        Dictionary<string, GrammaticalCase> forms = FormsOf(name);
        Regex pattern = Words.AnyOf(forms.Keys);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (CallLine line in calls.SelectMany(c => c.Lines))
        {
            foreach (Match match in pattern.Matches(line.Text))
            {
                string form = Words.Fold(match.Value);
                counts[form] = counts.GetValueOrDefault(form) + 1;
            }
        }

        return [.. counts.OrderBy(c => forms[c.Key]).Select(c => new NameMention(c.Key, forms[c.Key], c.Value))];
    }

    /// <summary>Заменить упоминания новым именем — в том же падеже, одним проходом.</summary>
    /// <returns>Новый транскрипт и сколько мест заменено.</returns>
    public static (CallTranscript Transcript, int Replaced) Replace(
        CallTranscript transcript,
        IEnumerable<NameMention> mentions,
        string newName)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(mentions);

        Dictionary<string, string> map = mentions.ToDictionary(m => Words.Fold(m.Form), m => Inflect(newName, m.Case), StringComparer.Ordinal);
        return map.Count == 0
            ? (transcript, 0)
            : TranscriptEdit.ReplaceAll(transcript, Words.AnyOf(map.Keys), found => map[Words.Fold(found)]);
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
    public static CallSession Rename(CallSession session, string from, string to) =>
        RenameAll(session, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [from] = to });

    /// <summary>
    /// Звонок, в котором люди подписаны псевдонимами, — для копии, которую отдают.
    /// </summary>
    /// <param name="session">Мета звонка.</param>
    /// <param name="transcript">Реплики.</param>
    /// <param name="aliases">Настоящее имя → псевдоним. Кого нет — остаётся как есть.</param>
    /// <param name="mentions">Заменить и упоминания в тексте реплик и в названии.</param>
    /// <remarks>
    /// <para>
    /// Файлы звонка не трогаются: меняется только то, что уходит наружу.
    /// Своё имя — такой же ключ: его подменяет тот, кто собирает транскрипт,
    /// передав псевдоним вместо имени владельца.
    /// </para>
    /// <para>
    /// Все псевдонимы — разом: иначе обмен «Кирилл ↔ Павел» превращал обоих
    /// в одного Кирилла.
    /// </para>
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

        var swap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string alias) in aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias) && !string.Equals(name, alias, StringComparison.Ordinal))
            {
                swap.TryAdd(name, alias.Trim());
            }
        }

        if (swap.Count == 0)
        {
            return (session, transcript);
        }

        session = RenameAll(session, swap);
        if (!mentions)
        {
            return (session, transcript);
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string name, string alias) in swap)
        {
            foreach ((string form, GrammaticalCase grammaticalCase) in FormsOf(name))
            {
                map.TryAdd(form, Inflect(alias, grammaticalCase));
            }
        }

        Regex pattern = Words.AnyOf(map.Keys);
        string Swap(Match found) => map[Words.Fold(found.Value)];
        return (
            session with { Title = session.Title is { } title ? pattern.Replace(title, Swap) : null },
            transcript with { Lines = [.. transcript.Lines.Select(l => l with { Text = pattern.Replace(l.Text, Swap) })] });
    }

    /// <summary>Переименовать сразу нескольких — по таблице «кто → кем».</summary>
    private static CallSession RenameAll(CallSession session, Dictionary<string, string> swap)
    {
        ArgumentNullException.ThrowIfNull(session);

        string Swap(string name) => swap.TryGetValue(name, out string? to) ? to : name;

        return session with
        {
            Participants = [.. session.Participants.Select(Swap).Distinct(StringComparer.OrdinalIgnoreCase)],
            VoiceNames = session.VoiceNames.ToDictionary(p => p.Key, p => Swap(p.Value)),
        };
    }

    /// <summary>Формы имени в сложенном виде → падеж; у одинаковых форм — первый падеж.</summary>
    private static Dictionary<string, GrammaticalCase> FormsOf(string name)
    {
        var forms = new Dictionary<string, GrammaticalCase>(StringComparer.Ordinal);
        string[] all = Forms(name.Trim()) ?? [name.Trim()];
        for (int i = 0; i < all.Length; i++)
        {
            forms.TryAdd(Words.Fold(all[i]), (GrammaticalCase)i);
        }

        return forms;
    }

    /// <summary>Все шесть падежных форм имени, или <c>null</c>, если имя не склоняется.</summary>
    private static string[]? Forms(string name)
    {
        if (name.Length < 2 || !CyrillicWord().IsMatch(name))
        {
            return null;
        }

        char last = char.ToLower(name[^1], CultureInfo.InvariantCulture);
        char beforeLast = char.ToLower(name[^2], CultureInfo.InvariantCulture);
        string stem = name[..^1];
        string[]? endings = last switch
        {
            'я' => beforeLast == 'и' ? Iya : Ya,
            'а' => A,
            'й' => beforeLast == 'и' ? Iiy : Iy,
            'ь' => Soft,
            _ => null,
        };

        if (endings is not null)
        {
            return [.. endings.Select((e, i) => stem + (last == 'а' ? AfterStem(stem, (GrammaticalCase)i, e) : e))];
        }

        return Vowels.Contains(last)
            ? null
            : [.. Consonant.Select((e, i) => (i == 0 ? name : Oblique(name)) + e)];
    }

    /// <summary>
    /// Окончание имени на «-а» с поправкой на правописание: «Саши», не
    /// «Сашы»; «Машей», не «Машой».
    /// </summary>
    private static string AfterStem(string stem, GrammaticalCase grammaticalCase, string ending)
    {
        char last = char.ToLower(stem[^1], CultureInfo.InvariantCulture);
        return grammaticalCase switch
        {
            GrammaticalCase.Genitive when "гкхжшщч".Contains(last) => "и",
            GrammaticalCase.Instrumental when "жшщчц".Contains(last) => "ей",
            _ => ending,
        };
    }

    /// <summary>
    /// Основа косвенных падежей: у «Павел» — «Павл», у «Лев» — «Льв», у «Кирилл» — та же.
    /// </summary>
    /// <remarks>
    /// Беглая гласная в русских именах — это «-ел» («Павел») и единственный
    /// частый случай с мягким знаком, «Лев»; остальные имена на согласный её
    /// не теряют.
    /// </remarks>
    private static string Oblique(string name)
    {
        if (name.Equals("Лев", StringComparison.OrdinalIgnoreCase))
        {
            return name[..1] + "ьв";
        }

        return name.Length >= 4 && name.EndsWith("ел", StringComparison.OrdinalIgnoreCase)
            ? name[..^2] + name[^1]
            : name;
    }
}
