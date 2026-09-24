using System.Text.Json;
using System.Text.Json.Serialization;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Speech;

namespace Tapybara.Core.Calls;

/// <summary>Чья дорожка: своего микрофона или системного звука.</summary>
public enum CallChannel
{
    /// <summary>Микрофон владельца. Кто говорит, известно по построению.</summary>
    Mine,

    /// <summary>Системный звук: собеседники, один или несколько.</summary>
    Theirs,
}

/// <summary>Одна распознанная реплика звонка.</summary>
/// <param name="Channel">С какой дорожки.</param>
/// <param name="Start">Начало от начала записи.</param>
/// <param name="End">Конец.</param>
/// <param name="Text">Текст после фильтров и замен.</param>
/// <param name="Voice">
/// Голос собеседника — буква в порядке первого появления («A», «B»…), или
/// <c>null</c>: реплика своя, голоса не разделялись или реплика не попала ни
/// в один найденный участок.
/// </param>
public sealed record CallLine(CallChannel Channel, TimeSpan Start, TimeSpan End, string Text, string? Voice = null);

/// <summary>
/// Всё, из чего собирается транскрипт звонка, — в том виде, в каком это
/// выдала машина.
/// </summary>
/// <remarks>
/// <para>
/// Лежит в папке звонка как <c>transcript.json</c>. Раньше распознавание
/// сразу сворачивалось в markdown, и из него выбрасывалось всё, из чего он
/// собран: какой голос звучал в реплике, границы, каналы. Поправить имя в
/// таком транскрипте можно было только одним способом — распознать звонок
/// заново, минутами.
/// </para>
/// <para>
/// Разделение ответственности: этот файл — то, что услышала машина, а
/// <c>meta.json</c> — то, что знает человек (кто был, как зовут какой голос).
/// <c>transcript.md</c> — отрисовка обоих и пересобирается за миллисекунды,
/// см. <see cref="CallTranscriptRenderer"/>.
/// </para>
/// </remarks>
public sealed record CallTranscript
{
    /// <summary>Версия формата. Растёт, когда старое чтение перестаёт быть верным.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Реплики обеих дорожек в хронологическом порядке.</summary>
    public IReadOnlyList<CallLine> Lines { get; init; } = [];

    /// <summary>Разделялись ли голоса собеседников.</summary>
    public bool VoicesSplit { get; init; }

    /// <summary>
    /// Сколько голосов было велено искать. Ноль — искали без подсказки.
    /// </summary>
    /// <remarks>
    /// Нужно, чтобы понять, устарело ли разделение: если человек потом
    /// отметил троих, а искали без подсказки или двоих, разделение стоит
    /// перезапустить — это минута процессора, а не повторное распознавание.
    /// </remarks>
    public int ExpectedVoices { get; init; }

    /// <summary>Сколько реплик своей дорожки отсеяно как чужая речь — по тексту.</summary>
    public int RemovedByText { get; init; }

    /// <summary>То же — по громкости.</summary>
    public int RemovedByEnergy { get; init; }

    /// <summary>
    /// Слепки голосов собеседников: буква голоса → нормированный вектор.
    /// </summary>
    /// <remarks>
    /// Когда голоса не разделялись, единственный слепок лежит под ключом
    /// <see cref="CallVoices.WholeOtherSide"/> — весь чужой канал. Слепки
    /// нужны книге голосов (<see cref="VoiceBook"/>): по ним на следующем
    /// звонке подсказывается имя. Сотни чисел на голос — копейки рядом с
    /// репликами.
    /// </remarks>
    public IReadOnlyDictionary<string, float[]> VoicePrints { get; init; } = new Dictionary<string, float[]>();

    /// <summary>Голоса собеседников в порядке первого появления.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Voices =>
    [
        .. Lines
            .Where(l => l.Voice is not null)
            .OrderBy(l => l.Start)
            .Select(l => l.Voice!)
            .Distinct(StringComparer.Ordinal),
    ];
}

/// <summary>Чтение и запись <c>transcript.json</c>.</summary>
public static class CallTranscriptStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Одна запись за раз во всём приложении.
    /// </summary>
    /// <remarks>
    /// Правят файл двое: распознавание в фоне и окно звонка, где человек
    /// переназначает реплику. Без замка правка из окна, пришедшая между
    /// чтением и записью распознавания, пропала бы молча.
    /// </remarks>
    private static readonly Lock Gate = new();

    /// <summary>Прочитать, или <c>null</c>, если файла нет или он испорчен.</summary>
    public static CallTranscript? Load(string callDirectory)
    {
        string path = Path.Combine(callDirectory, CallSession.TranscriptDataFileName);
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<CallTranscript>(File.ReadAllText(path), Options)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Не удалось прочитать {path}.", ex);
            return null;
        }
    }

    /// <summary>Записать через временный файл: обрыв посередине не оставит битый JSON.</summary>
    public static void Save(string callDirectory, CallTranscript transcript)
    {
        lock (Gate)
        {
            Write(callDirectory, transcript);
        }
    }

    /// <summary>Прочитать, поправить и записать под одним замком.</summary>
    /// <returns>Записанное, или <c>null</c>, если файла не было.</returns>
    public static CallTranscript? Update(string callDirectory, Func<CallTranscript, CallTranscript> change)
    {
        lock (Gate)
        {
            if (Load(callDirectory) is not { } current)
            {
                return null;
            }

            CallTranscript updated = change(current);
            Write(callDirectory, updated);
            return updated;
        }
    }

    private static void Write(string callDirectory, CallTranscript transcript)
    {
        string path = Path.Combine(callDirectory, CallSession.TranscriptDataFileName);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(transcript, Options));
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Один голос собеседника: сколько говорил и чем его узнать.</summary>
/// <param name="Id">Буква голоса.</param>
/// <param name="Speech">Сколько он говорил в сумме.</param>
/// <param name="Share">Доля от всей речи собеседников, 0–1.</param>
/// <param name="Quotes">Реплики, по которым его проще всего узнать.</param>
public sealed record VoiceSummary(string Id, TimeSpan Speech, double Share, IReadOnlyList<CallLine> Quotes);

/// <summary>Голоса собеседников: разметка реплик и сводка для человека.</summary>
public static class CallVoices
{
    /// <summary>Ключ слепка всего чужого канала — когда голоса не разделялись.</summary>
    public const string WholeOtherSide = "*";

    /// <summary>
    /// Сколько речи голоса брать в слепок.
    /// </summary>
    /// <remarks>
    /// Модели слепков насыщаются на десятках секунд: дальше вектор почти не
    /// меняется, а время растёт линейно. Самые длинные реплики — потому что
    /// в коротких больше перебиваний и чужих голосов на стыках.
    /// </remarks>
    public static readonly TimeSpan PrintBudget = TimeSpan.FromSeconds(30);

    /// <summary>Меньше этого слепок ненадёжен — лучше никакого.</summary>
    public static readonly TimeSpan PrintMinimum = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Снять слепки голосов собеседников.
    /// </summary>
    /// <param name="audio">Чужой канал целиком: 16 кГц, моно.</param>
    /// <param name="transcript">Реплики с разметкой голосов.</param>
    /// <param name="embed">Слепок по отрезку речи.</param>
    /// <param name="sampleRate">Частота <paramref name="audio"/>.</param>
    /// <remarks>
    /// Берутся реплики, а не участки разделителя: после того как человек
    /// переназначил реплику или склеил два голоса, правда — в репликах.
    /// </remarks>
    public static IReadOnlyDictionary<string, float[]> Prints(
        float[] audio,
        CallTranscript transcript,
        Func<float[], float[]?> embed,
        int sampleRate = 16_000)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(embed);

        var prints = new Dictionary<string, float[]>();
        List<CallLine> theirs = [.. transcript.Lines.Where(l => l.Channel == CallChannel.Theirs)];

        IEnumerable<IGrouping<string, CallLine>> groups = transcript.Voices.Count == 0
            ? theirs.GroupBy(_ => WholeOtherSide)
            : theirs.Where(l => l.Voice is not null).GroupBy(l => l.Voice!);

        foreach (IGrouping<string, CallLine> group in groups)
        {
            float[] speech = Gather(audio, group, sampleRate);
            if (speech.Length < PrintMinimum.TotalSeconds * sampleRate)
            {
                continue;
            }

            if (embed(speech) is { } print)
            {
                prints[group.Key] = print;
            }
        }

        return prints;
    }

    /// <summary>Склеить самые длинные реплики голоса в пределах бюджета.</summary>
    internal static float[] Gather(float[] audio, IEnumerable<CallLine> lines, int sampleRate)
    {
        var speech = new List<float>();
        int budget = (int)(PrintBudget.TotalSeconds * sampleRate);

        foreach (CallLine line in lines.OrderByDescending(l => l.End - l.Start))
        {
            int from = Math.Clamp((int)(line.Start.TotalSeconds * sampleRate), 0, audio.Length);
            int to = Math.Clamp((int)(line.End.TotalSeconds * sampleRate), from, audio.Length);
            int take = Math.Min(to - from, budget - speech.Count);
            if (take <= 0)
            {
                continue;
            }

            speech.AddRange(new ArraySegment<float>(audio, from, take));
            if (speech.Count >= budget)
            {
                break;
            }
        }

        return [.. speech];
    }

    /// <summary>Сколько цитат показывать на голос.</summary>
    /// <remarks>
    /// Три — примерно столько нужно, чтобы узнать человека по словам, а не
    /// по одному обрывку. Больше не помещается в панель рядом с транскриптом.
    /// </remarks>
    public const int QuotesPerVoice = 3;

    /// <summary>
    /// Короче этого реплика в цитаты не годится.
    /// </summary>
    /// <remarks>
    /// «Да», «угу», «ага» — самые частые реплики в любом разговоре и самые
    /// бесполезные для узнавания: их говорят все одинаково.
    /// </remarks>
    private const int MinQuoteLength = 24;

    /// <summary>
    /// Подписать реплики собеседников голосами из разделителя.
    /// </summary>
    /// <remarks>
    /// Номера кластеров заменяются буквами в порядке первого появления.
    /// Разделитель нумерует кластеры с дырами и в своём порядке: на записи
    /// с единственным голосом он вернул номер 2. Буква по порядку появления
    /// — то, что человек может сопоставить с тем, что слышал: «A» заговорил
    /// первым.
    /// <para>
    /// Свои реплики не трогаются: свой канал не разделяется никогда.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<CallLine> Assign(IReadOnlyList<CallLine> lines, IReadOnlyList<SpeakerSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(spans);

        Dictionary<int, string> letters = Letters(spans);

        return
        [
            .. lines.Select(line =>
            {
                if (line.Channel != CallChannel.Theirs)
                {
                    return line;
                }

                int? speaker = SpeakerAssignment.For(line.Start, line.End, spans);
                return line with
                {
                    Voice = speaker is { } s && letters.TryGetValue(s, out string? letter) ? letter : null,
                };
            }),
        ];
    }

    /// <summary>Буква для голоса с порядковым номером <paramref name="index"/>.</summary>
    /// <remarks>
    /// После «Z» — «AA», «AB»… Столько голосов на одном звонке разделитель
    /// не найдёт, но и упасть из-за двадцать седьмого он не должен.
    /// </remarks>
    public static string Letter(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        string result = string.Empty;
        int n = index;
        do
        {
            result = (char)('A' + (n % 26)) + result;
            n = (n / 26) - 1;
        }
        while (n >= 0);

        return result;
    }

    /// <summary>
    /// Сводка по голосам: доля речи и цитаты для узнавания.
    /// </summary>
    /// <remarks>
    /// Цитаты — самые длинные реплики голоса, но показываются в порядке
    /// времени: так их легче сопоставить с ходом разговора. Короткие
    /// реплики берутся, только если длинных нет вовсе.
    /// </remarks>
    public static IReadOnlyList<VoiceSummary> Summarize(CallTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        List<CallLine> theirs = [.. transcript.Lines.Where(l => l.Channel == CallChannel.Theirs && l.Voice is not null)];
        double total = theirs.Sum(l => Math.Max(0, (l.End - l.Start).TotalSeconds));

        return
        [
            .. transcript.Voices.Select(id =>
            {
                List<CallLine> own = [.. theirs.Where(l => l.Voice == id)];
                double seconds = own.Sum(l => Math.Max(0, (l.End - l.Start).TotalSeconds));

                IEnumerable<CallLine> pool = own.Any(l => l.Text.Length >= MinQuoteLength)
                    ? own.Where(l => l.Text.Length >= MinQuoteLength)
                    : own;

                return new VoiceSummary(
                    id,
                    TimeSpan.FromSeconds(seconds),
                    total > 0 ? seconds / total : 0,
                    [.. pool.OrderByDescending(l => l.Text.Length).Take(QuotesPerVoice).OrderBy(l => l.Start)]);
            }),
        ];
    }

    /// <summary>
    /// Слить голос <paramref name="from"/> в голос <paramref name="into"/>.
    /// </summary>
    /// <remarks>
    /// Разделитель без подсказки охотно разваливает один голос на два —
    /// простуженный, отошедший от микрофона, заговоривший громче. Исправить
    /// это человеку проще всего словами «это тот же человек».
    /// </remarks>
    public static CallTranscript Merge(CallTranscript transcript, string from, string into)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        return transcript with
        {
            Lines = [.. transcript.Lines.Select(l => l.Voice == from ? l with { Voice = into } : l)],
        };
    }

    /// <summary>Отдать одну реплику другому голосу.</summary>
    /// <param name="transcript">Транскрипт.</param>
    /// <param name="line">Реплика — как она лежит в транскрипте.</param>
    /// <param name="voice">Новый голос.</param>
    public static CallTranscript Reassign(CallTranscript transcript, CallLine line, string voice)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        return transcript with
        {
            Lines = [.. transcript.Lines.Select(l => l == line ? l with { Voice = voice } : l)],
        };
    }

    private static Dictionary<int, string> Letters(IReadOnlyList<SpeakerSpan> spans)
    {
        var letters = new Dictionary<int, string>();
        foreach (SpeakerSpan span in spans.OrderBy(s => s.Start))
        {
            if (!letters.ContainsKey(span.Speaker))
            {
                letters[span.Speaker] = Letter(letters.Count);
            }
        }

        return letters;
    }
}
