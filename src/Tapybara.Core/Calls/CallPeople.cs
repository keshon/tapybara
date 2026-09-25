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
        var named = new Dictionary<string, (int Color, List<string> Voices)>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var unnamed = new List<(int Color, string Voice)>();
        var mine = new List<string>();
        for (int i = 0; i < voices.Count; i++)
        {
            string voice = voices[i];
            switch (CallSpeakers.NameOf(session, voice))
            {
                case CallSession.Me:
                    mine.Add(voice);
                    break;
                case null:
                    unnamed.Add((i, voice));
                    break;
                case string name when named.TryGetValue(name, out var person):
                    person.Voices.Add(voice);
                    break;
                case string name:
                    named[name] = (i, [voice]);
                    order.Add(name);
                    break;
            }
        }

        var people = new List<CallPerson>
        {
            Person(null, isMe: true, mine, -1, transcript, total, l => l.Channel == CallChannel.Mine || (l.Voice is { } v && mine.Contains(v))),
        };

        if (voices.Count == 0)
        {
            // Голоса не разделялись: вся чужая дорожка — один человек.
            if (transcript.Lines.Any(l => l.Channel == CallChannel.Theirs))
            {
                string? name = session.Participants.Count == 1 ? session.Participants[0] : null;
                people.Add(Person(name, isMe: false, [CallVoices.WholeOtherSide], 0, transcript, total, l => l.Channel == CallChannel.Theirs));
            }

            return people;
        }

        foreach (string name in order)
        {
            (int color, List<string> own) = named[name];
            people.Add(Person(name, isMe: false, own, color, transcript, total, l => l.Voice is { } v && own.Contains(v)));
        }

        foreach ((int color, string voice) in unnamed)
        {
            people.Add(Person(null, isMe: false, [voice], color, transcript, total, l => l.Voice == voice));
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
}
