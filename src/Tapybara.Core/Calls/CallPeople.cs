namespace Tapybara.Core.Calls;

/// <summary>Участник звонка — человек, а не кусок записи.</summary>
/// <param name="Name">Имя; <c>null</c> — голос ещё не назван.</param>
/// <param name="IsMe">Владелец микрофона.</param>
/// <param name="Voices">
/// Голоса, из которых человек собран. У владельца — голоса чужой дорожки,
/// названные «это я»; у собеседника, чьи голоса не разделялись, —
/// <see cref="CallVoices.WholeOtherSide"/>.
/// </param>
/// <param name="Color">Индекс цвета — по первому голосу; у владельца −1.</param>
/// <param name="Speech">Сколько говорил.</param>
/// <param name="Share">Доля во всей речи звонка, вместе с владельцем, 0–1.</param>
/// <param name="Quotes">Реплики, по которым его проще всего узнать.</param>
public sealed record CallPerson(
    string? Name,
    bool IsMe,
    IReadOnlyList<string> Voices,
    int Color,
    TimeSpan Speech,
    double Share,
    IReadOnlyList<CallLine> Quotes);

/// <summary>
/// Кто был на звонке: голоса, собранные в людей.
/// </summary>
/// <remarks>
/// <para>
/// Машина находит голоса, человек говорит, кто это. Имя — это человек:
/// два голоса с одним именем — не ошибка, а один человек, которого
/// разделитель развалил надвое (отошёл от микрофона, заговорил громче).
/// Раньше имя принадлежало голосу, и второй «Павел» отнимал имя у первого;
/// склеить их можно было только отдельным действием, безвозвратно.
/// Теперь присоединение — это просто то же имя, а отделить голос обратно —
/// снять с него имя.
/// </para>
/// <para>
/// Владелец микрофона — такой же участник: в счёте и долях он стоит
/// наравне со всеми. «48% речи Павла» без него значило бы «48% того, что
/// сказала другая сторона», а читается это как доля звонка.
/// </para>
/// </remarks>
public static class CallPeople
{
    /// <summary>Разложить звонок на людей: владелец, названные, неназванные.</summary>
    public static IReadOnlyList<CallPerson> Of(CallSession session, CallTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(transcript);

        double total = transcript.Lines.Sum(Seconds);
        IReadOnlyList<string> voices = transcript.Voices;

        // Голоса по людям, в порядке первого голоса каждого: цвет человека —
        // цвет его первого голоса, и он не прыгает, когда к нему присоединяют.
        var named = new List<(string Name, List<string> Voices)>();
        var unnamed = new List<string>();
        var mine = new List<string>();
        foreach (string voice in voices)
        {
            string? name = CallSpeakers.NameOf(session, voice);
            if (name == CallSpeakers.Me)
            {
                mine.Add(voice);
            }
            else if (name is null)
            {
                unnamed.Add(voice);
            }
            else if (named.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) is { Name: not null } person)
            {
                person.Voices.Add(voice);
            }
            else
            {
                named.Add((name, [voice]));
            }
        }

        var people = new List<CallPerson>
        {
            Person(null, isMe: true, mine, -1, transcript, total, l => l.Channel == CallChannel.Mine || (l.Voice is { } v && mine.Contains(v))),
        };

        if (voices.Count == 0)
        {
            // Голоса не разделялись: вся чужая дорожка — один человек.
            List<CallLine> theirs = [.. transcript.Lines.Where(l => l.Channel == CallChannel.Theirs)];
            if (theirs.Count > 0)
            {
                string? name = session.Participants.Count == 1 ? session.Participants[0] : null;
                people.Add(Person(name, isMe: false, [CallVoices.WholeOtherSide], 0, transcript, total, l => l.Channel == CallChannel.Theirs));
            }

            return people;
        }

        foreach ((string name, List<string> own) in named)
        {
            people.Add(Person(name, isMe: false, own, IndexOf(voices, own[0]), transcript, total, l => l.Voice is { } v && own.Contains(v)));
        }

        foreach (string voice in unnamed)
        {
            people.Add(Person(null, isMe: false, [voice], IndexOf(voices, voice), transcript, total, l => l.Voice == voice));
        }

        return people;
    }

    /// <summary>
    /// Цвет каждого голоса — цвет его человека.
    /// </summary>
    /// <returns>Голос → индекс цвета; −1 — цвет владельца.</returns>
    public static IReadOnlyDictionary<string, int> Colors(IReadOnlyList<CallPerson> people)
    {
        ArgumentNullException.ThrowIfNull(people);

        var colors = new Dictionary<string, int>();
        foreach (CallPerson person in people)
        {
            foreach (string voice in person.Voices)
            {
                colors[voice] = person.Color;
            }
        }

        return colors;
    }

    private static CallPerson Person(
        string? name,
        bool isMe,
        IReadOnlyList<string> voices,
        int color,
        CallTranscript transcript,
        double total,
        Func<CallLine, bool> belongs)
    {
        List<CallLine> own = [.. transcript.Lines.Where(belongs)];
        double seconds = own.Sum(Seconds);
        return new CallPerson(
            name,
            isMe,
            voices,
            color,
            TimeSpan.FromSeconds(seconds),
            total > 0 ? seconds / total : 0,
            isMe ? [] : CallVoices.QuotesOf(own));
    }

    private static double Seconds(CallLine line) => Math.Max(0, (line.End - line.Start).TotalSeconds);

    private static int IndexOf(IReadOnlyList<string> voices, string voice)
    {
        for (int i = 0; i < voices.Count; i++)
        {
            if (voices[i] == voice)
            {
                return i;
            }
        }

        return -1;
    }
}
