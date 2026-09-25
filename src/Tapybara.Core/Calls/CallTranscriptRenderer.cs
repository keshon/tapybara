using System.Globalization;
using System.Text;

namespace Tapybara.Core.Calls;

/// <summary>
/// Кто сказал реплику — по тому, что знает человек и что нашла машина.
/// </summary>
/// <remarks>
/// <para>
/// Имя голосу даёт человек, а не порядок появления. Раньше первое имя из
/// списка участников доставалось тому, кто заговорил первым, — и при двух
/// собеседниках это было угадывание пятьдесят на пятьдесят, которое в
/// транскрипте выглядело как факт. Машина умеет разделить голоса, но не
/// знает, как их зовут; сопоставить может только человек, глядя на цитаты.
/// </para>
/// <para>
/// Единственное исключение — один собеседник. Там сопоставлять нечего:
/// всё, что пришло с системной дорожки, сказал он, и это верно по
/// построению, а не по догадке.
/// </para>
/// </remarks>
public static class CallSpeakers
{
    /// <summary>
    /// Имя голоса, если оно известно, иначе <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Названный голос — по имени. Единственный голос при единственном
    /// отмеченном участнике — этот участник: человек сказал, кто был на той
    /// стороне.
    /// </para>
    /// <para>
    /// Но только пока голос один. При одном отмеченном голоса сами не
    /// разделяются, и несколько их бывает, лишь когда человек сам попросил
    /// разделить — то есть сказал «их было больше». Раньше тогда каждый
    /// найденный голос подписывался этим одним именем: на звонке, где
    /// отметили Витю, а говорили трое, все трое становились Витей, и
    /// назвать их никто не просил.
    /// </para>
    /// </remarks>
    public static string? NameOf(CallSession session, string voice)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.VoiceNames.TryGetValue(voice, out string? name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return session.Participants.Count == 1 && session.Voices.Count <= 1 ? session.Participants[0] : null;
    }

    /// <summary>
    /// Нужно ли человеку назвать голоса.
    /// </summary>
    /// <remarks>
    /// Только когда голосов больше одного и хотя бы у одного нет имени.
    /// Единственный неназванный голос честно подписан «Собеседник», и
    /// требовать от человека действия ради него незачем.
    /// </remarks>
    public static bool NeedsNames(CallSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.Voices.Count > 1 && session.Voices.Any(v => NameOf(session, v) is null);
    }

    /// <summary>Подпись реплики в транскрипте.</summary>
    /// <param name="line">Реплика.</param>
    /// <param name="session">Мета звонка: участники и имена голосов.</param>
    /// <param name="voiceCount">Сколько голосов нашлось у собеседников.</param>
    /// <param name="myName">Имя владельца микрофона.</param>
    /// <param name="fallback">Общее слово для собеседника без имени.</param>
    /// <param name="voiceWord">Слово «Голос» на языке интерфейса.</param>
    public static string Label(
        CallLine line,
        CallSession session,
        int voiceCount,
        string myName,
        string fallback,
        string voiceWord)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(session);

        if (line.Channel == CallChannel.Mine)
        {
            return myName;
        }

        if (line.Voice is { } voice)
        {
            // Неназванный голос среди нескольких — «Голос B», а не общее
            // слово: «Собеседник» на всех стёр бы то единственное, что машина
            // действительно знает, — что это разные люди.
            return NameOf(session, voice)
                   ?? (voiceCount > 1 ? $"{voiceWord} {voice}" : fallback);
        }

        return session.Participants.Count == 1 ? session.Participants[0] : fallback;
    }
}

/// <summary>
/// Сборка <c>transcript.md</c> из <c>transcript.json</c> и <c>meta.json</c>.
/// </summary>
/// <remarks>
/// Чистая функция без распознавания. Её зовут после каждой правки имени,
/// и она обязана быть мгновенной: человек назвал голос — транскрипт уже
/// другой, без минут ожидания.
/// </remarks>
public static class CallTranscriptRenderer
{
    /// <summary>Как часто повторять подпись внутри длинной реплики одного человека.</summary>
    private static readonly TimeSpan HeaderInterval = TimeSpan.FromMinutes(2);

    /// <summary>Собрать markdown.</summary>
    /// <param name="session">Мета звонка.</param>
    /// <param name="transcript">Что распознано.</param>
    /// <param name="myName">Имя владельца микрофона.</param>
    /// <param name="fallback">Общее слово для собеседника без имени.</param>
    /// <param name="labels">Подписи на языке интерфейса.</param>
    public static string Render(
        CallSession session,
        CallTranscript transcript,
        string myName,
        string fallback,
        CallTranscriptLabels labels)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(labels);

        int voiceCount = transcript.Voices.Count;

        var markdown = new StringBuilder();
        markdown.Append("# ")
            .AppendLine(string.IsNullOrWhiteSpace(session.Title) ? Path.GetFileName(session.Directory) : session.Title)
            .AppendLine();

        markdown.Append("- ").Append(labels.StartedAt).Append(": ")
            .AppendLine(session.StartedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture));
        markdown.Append("- ").Append(labels.Duration).Append(": ")
            .AppendLine(Stamp(session.Duration));

        if (!string.IsNullOrWhiteSpace(session.Trigger))
        {
            markdown.Append("- ").Append(labels.Trigger).Append(": ").AppendLine(session.Trigger);
        }

        // Как именно разделены голоса — в шапку. Читающий транскрипт должен
        // видеть, откуда взялись подписи: по подсказке о числе участников
        // или машина решала сама. Это разные степени доверия.
        if (transcript.VoicesSplit && voiceCount > 0)
        {
            markdown.Append("- ").Append(labels.VoicesSplit).Append(": ")
                .Append(voiceCount.ToString(CultureInfo.CurrentCulture))
                .Append(" (")
                .Append(transcript.ExpectedVoices > 0 ? labels.VoicesSplitHinted : labels.VoicesSplitGuessed)
                .AppendLine(")");
        }

        List<string> people = Participants(session);
        if (people.Count > 0)
        {
            markdown.Append("- ").Append(labels.Participants).Append(": ")
                .AppendLine(string.Join(", ", new[] { myName }.Concat(people)));
        }

        // Честно пишем, сколько реплик отсеяно: если фильтр переусердствовал,
        // это единственный способ заметить пропажу, не переслушивая запись.
        int removed = transcript.RemovedByText + transcript.RemovedByEnergy;
        if (removed > 0)
        {
            markdown.Append("- ").Append(labels.BleedRemoved).Append(": ")
                .Append(removed.ToString(CultureInfo.CurrentCulture))
                .Append(" (").Append(labels.BleedByText).Append(' ')
                .Append(transcript.RemovedByText.ToString(CultureInfo.CurrentCulture))
                .Append(", ").Append(labels.BleedByEnergy).Append(' ')
                .Append(transcript.RemovedByEnergy.ToString(CultureInfo.CurrentCulture))
                .AppendLine(")");
        }

        markdown.AppendLine().AppendLine("---").AppendLine();

        if (transcript.Lines.Count == 0)
        {
            markdown.AppendLine(labels.NothingRecognized);
            return markdown.ToString();
        }

        TimeSpan? headerAt = null;
        string? headerSpeaker = null;

        foreach (CallLine line in transcript.Lines.OrderBy(l => l.Start))
        {
            string speaker = CallSpeakers.Label(line, session, voiceCount, myName, fallback, labels.Voice);

            if (NeedsHeader(line.Start, speaker, headerAt, headerSpeaker))
            {
                markdown.Append("**[").Append(Stamp(line.Start)).Append("] ")
                    .Append(speaker).Append(":** ");

                headerSpeaker = speaker;
                headerAt = line.Start;
            }

            markdown.AppendLine(line.Text).AppendLine();
        }

        return markdown.ToString();
    }

    /// <summary>Отрисовать и записать <c>transcript.md</c> рядом с данными.</summary>
    /// <returns>Путь к транскрипту, или <c>null</c>, если данных распознавания нет.</returns>
    public static string? Write(string callDirectory, string myName, string fallback, CallTranscriptLabels labels)
    {
        if (CallMeta.Load(callDirectory) is not { } session
            || CallTranscriptStore.Load(callDirectory) is not { } transcript)
        {
            return null;
        }

        File.WriteAllText(session.TranscriptPath, Render(session, transcript, myName, fallback, labels));
        return session.TranscriptPath;
    }

    /// <summary>
    /// Участники для шапки: отмеченные и те, чьим именем назван голос.
    /// </summary>
    /// <remarks>
    /// Имя голосу могли дать прямо в окне звонка, не отмечая человека в
    /// списке участников. В шапке он всё равно должен быть.
    /// </remarks>
    private static List<string> Participants(CallSession session)
    {
        var people = new List<string>(session.Participants);
        foreach (string name in session.VoiceNames.Values)
        {
            if (!string.IsNullOrWhiteSpace(name) && !people.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                people.Add(name);
            }
        }

        return people;
    }

    /// <summary>
    /// Ставить ли перед этой репликой подпись «[время] Имя:».
    /// </summary>
    /// <param name="start">Начало реплики от начала записи.</param>
    /// <param name="speaker">Кто её произнёс.</param>
    /// <param name="headerAt">Время последней поставленной подписи, или <c>null</c>, если её ещё не было.</param>
    /// <param name="headerSpeaker">Кто стоял в последней подписи.</param>
    /// <remarks>
    /// Подпись ставится на СМЕНЕ говорящего, а не на каждом сегменте. Whisper
    /// режет речь на куски по несколько секунд, и штамп на каждом превращал
    /// двухминутный монолог в сорок одинаковых строк «[2:14] Кирилл:», между
    /// которыми терялся сам текст. Внутри длинного монолога подпись всё-таки
    /// повторяется: без отметок времени в получасовой реплике невозможно найти
    /// место в записи, а ради этого транскрипт и держат рядом со звуком.
    /// <para>
    /// Отсутствие предыдущей подписи — это <c>null</c>, а не «очень давно».
    /// Здесь стояло <see cref="TimeSpan.MinValue"/>, и вычитание его из времени
    /// реплики переполняло <see cref="TimeSpan"/> на ПЕРВОЙ же строке любого
    /// транскрипта — час записи разговора не собирался вовсе. Отдельная функция
    /// существует именно ради этого: на ней стоит тест, а внутри цикла
    /// сборки markdown такой случай было некому проверить.
    /// </para>
    /// </remarks>
    internal static bool NeedsHeader(TimeSpan start, string speaker, TimeSpan? headerAt, string? headerSpeaker) =>
        headerAt is null
        || speaker != headerSpeaker
        || start - headerAt.Value >= HeaderInterval;

    /// <summary>
    /// Отметка времени.
    /// </summary>
    /// <remarks>
    /// Минуты считаем через <c>TotalMinutes</c>, а не форматом <c>mm</c>:
    /// тот обнуляется на шестидесятой минуте, а звонок бывает и длиннее.
    /// </remarks>
    public static string Stamp(TimeSpan time) =>
        $"{(int)time.TotalMinutes}:{time.Seconds:D2}";
}
