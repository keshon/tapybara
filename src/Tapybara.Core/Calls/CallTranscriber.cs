using System.Globalization;
using System.Text;
using Tapybara.Core.Audio;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Settings;
using Tapybara.Core.Speech;

namespace Tapybara.Core.Calls;

/// <summary>Этап сборки транскрипта — чтобы интерфейс мог сказать это своими словами.</summary>
public enum CallTranscriptionStage
{
    ReadingTracks,
    TranscribingMicrophone,
    TranscribingOtherSide,
    SplittingVoices,
    FilteringBleed,
    Done,
}

/// <summary>
/// Подписи в готовом транскрипте.
/// </summary>
/// <remarks>
/// Транскрипт читает человек, и заголовки в нём должны быть на его языке.
/// Раньше они были зашиты по-русски прямо здесь: английский пользователь
/// получал английскую речь под русскими заголовками. Словаря у <c>Core</c>
/// нет и быть не должно, поэтому подписи приходят снаружи.
/// </remarks>
public sealed record CallTranscriptLabels(
    string StartedAt,
    string Duration,
    string Trigger,
    string Participants,
    string VoicesSplit,
    string VoicesSplitHinted,
    string VoicesSplitGuessed,
    string UnknownSpeaker,
    string BleedRemoved,
    string BleedByText,
    string BleedByEnergy,
    string NothingRecognized)
{
    /// <summary>Английские подписи — запасной вариант для консольных сценариев.</summary>
    public static CallTranscriptLabels Default { get; } = new(
        "Started",
        "Duration",
        "Trigger",
        "Participants",
        "Voices told apart",
        "with the participant list as a hint",
        "without a hint",
        "Someone",
        "Other-side speech removed from your channel",
        "by text",
        "by loudness",
        "_No speech recognised._");
}

/// <summary>
/// Сборка транскрипта звонка из двух каналов.
/// </summary>
/// <remarks>
/// Каналы распознаются раздельно, поэтому «кто говорит» известно заранее:
/// микрофон — владелец, системный звук — собеседники. Диаризация по голосу
/// нужна только чтобы разделить нескольких собеседников между собой, и для
/// разговора один на один не требуется вовсе.
/// </remarks>
public sealed class CallTranscriber(
    SpeechTranscriber transcriber,
    Func<AppSettings> settings,
    Func<CallTranscriptLabels>? labels = null,
    Func<SpeakerDiarizer?>? diarizer = null)
{
    /// <summary>Одна реплика в общей хронологии.</summary>
    private sealed record Utterance(TimeSpan Start, string Speaker, string Text);

    /// <summary>Распознать звонок и записать <c>transcript.md</c> в его папку.</summary>
    /// <returns>Путь к готовому транскрипту.</returns>
    public async Task<string> TranscribeAsync(
        CallSession session,
        IProgress<CallTranscriptionStage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        AppSettings current = settings();

        if (!File.Exists(session.MicPath) || !File.Exists(session.SystemPath))
        {
            throw new FileNotFoundException(
                "В папке звонка нет дорожек mic.wav и system.wav.", session.MicPath);
        }

        // Оборванная запись — обычное дело: приложение могли закрыть жёстко.
        // Чиним заголовки, прежде чем пытаться читать.
        CallRepair.RepairCall(session);

        progress?.Report(CallTranscriptionStage.ReadingTracks);

        // Каналы обрабатываем ПО ОЧЕРЕДИ и массив отпускаем сразу. Держать оба
        // часовых канала в памяти — это около гигабайта в куче больших
        // объектов ради нескольких десятков средних значений, которые
        // прекрасно считаются по огибающей.
        progress?.Report(CallTranscriptionStage.TranscribingMicrophone);
        (IReadOnlyList<TranscriptSegment> micSegments, EnergyEnvelope micEnergy, _) = await ProcessChannelAsync(
            session.MicPath,
            // Свой канал распознаём заданным языком: что говорит владелец
            // микрофона, известно заранее.
            current.Language,
            current,
            // Свой канал не разделяем никогда: он и есть один человек.
            speakersToLookFor: 0,
            progress: null,
            cancellationToken).ConfigureAwait(false);

        progress?.Report(CallTranscriptionStage.TranscribingOtherSide);
        (IReadOnlyList<TranscriptSegment> systemSegments, EnergyEnvelope systemEnergy, IReadOnlyList<SpeakerSpan> voices) =
            await ProcessChannelAsync(
                session.SystemPath,
                // Чужой канал — определением языка. Навязанный не тому каналу язык
                // не «слегка ухудшает» распознавание, а превращает речь в бессмыслицу.
                current.OtherSideLanguage,
                current,
                // Разделять голоса имеет смысл только на чужом канале: свой
                // принадлежит владельцу микрофона по построению.
                SpeakersToLookFor(session, current),
                progress,
                cancellationToken).ConfigureAwait(false);

        progress?.Report(CallTranscriptionStage.FilteringBleed);
        BleedFilter.Result filtered = BleedFilter.Apply(micSegments, systemSegments, micEnergy, systemEnergy);

        CallTranscriptLabels text = labels?.Invoke() ?? CallTranscriptLabels.Default;
        Func<TranscriptSegment, string> nameOther = OtherSideNamer(session, current, voices, text);

        List<Utterance> timeline =
        [
            .. filtered.Kept.Select(s => new Utterance(s.Start, current.EffectiveMyName, s.Text)),
            .. systemSegments.Select(s => new Utterance(s.Start, nameOther(s), s.Text)),
        ];

        timeline.Sort((a, b) => a.Start.CompareTo(b.Start));

        string markdown = Render(session, current, timeline, filtered, voices, text);
        await File.WriteAllTextAsync(session.TranscriptPath, markdown, cancellationToken).ConfigureAwait(false);

        AppLog.Info($"Транскрипт готов: {session.TranscriptPath}, отсеяно {filtered.RemovedTotal} реплик.");
        progress?.Report(CallTranscriptionStage.Done);
        return session.TranscriptPath;
    }

    /// <summary>
    /// Прочитать канал, распознать его, снять огибающую и, если нужно, разделить голоса.
    /// </summary>
    /// <remarks>
    /// Массив сэмплов живёт только внутри этого метода: наружу уходят сегменты,
    /// огибающая и границы голосов, вместе занимающие меньше мегабайта на час
    /// записи.
    /// </remarks>
    private async Task<(IReadOnlyList<TranscriptSegment> Segments, EnergyEnvelope Energy, IReadOnlyList<SpeakerSpan> Voices)>
        ProcessChannelAsync(
            string path,
            string language,
            AppSettings current,
            int speakersToLookFor,
            IProgress<CallTranscriptionStage>? progress,
            CancellationToken cancellationToken)
    {
        float[] samples = AudioFile.ReadMono16k(path);
        if (samples.Length == 0)
        {
            return ([], EnergyEnvelope.Empty, []);
        }

        // Огибающую снимаем ДО нормализации: анти-bleed сравнивает каналы
        // между собой, а нормализация усиливает каждый по-своему.
        EnergyEnvelope energy = EnergyEnvelope.Build(samples);

        IReadOnlyList<TranscriptSegment> segments = await transcriber
            .TranscribeAsync(samples, progress: null, language: language, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<SpeakerSpan> voices = [];
        if (speakersToLookFor != 0 && diarizer?.Invoke() is { } splitter)
        {
            progress?.Report(CallTranscriptionStage.SplittingVoices);

            // Разделяем ПОСЛЕ распознавания и по нормализованной копии.
            // После — потому что своя копия распознавания к этому моменту уже
            // освобождена, и в памяти снова лежит один лишний массив, а не два.
            float[] audio = current.NormalizeAudio ? AudioNormalizer.Normalize(samples) : samples;

            voices = await Task.Run(
                () => splitter.Split(audio, speakersToLookFor, progress: null, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        return (Clean(segments, current), energy, voices);
    }

    /// <summary>
    /// Сколько голосов искать в чужом канале. Ноль — не искать вовсе.
    /// </summary>
    /// <remarks>
    /// Один названный собеседник — разделять нечего: его именем подписывается
    /// весь канал, и это бесплатно и точно. Ни одного названного — ищем без
    /// подсказки: людей там может быть и трое, просто мы не знаем их имён, а
    /// «Them» на всех хуже, чем «Собеседник 1» и «Собеседник 2».
    /// </remarks>
    private static int SpeakersToLookFor(CallSession session, AppSettings current)
    {
        if (!current.SplitVoices || session.Participants.Count == 1)
        {
            return 0;
        }

        return session.Participants.Count > 1 ? session.Participants.Count : -1;
    }

    /// <summary>
    /// Чем подписывать реплики чужого канала.
    /// </summary>
    /// <remarks>
    /// Порядок предпочтений: имя разделённого голоса, затем единственный
    /// названный собеседник, затем общее слово из настроек. Каждый следующий
    /// шаг — признание, что мы знаем меньше, и подпись обязана это показывать,
    /// а не делать вид, что знает.
    /// </remarks>
    private static Func<TranscriptSegment, string> OtherSideNamer(
        CallSession session,
        AppSettings current,
        IReadOnlyList<SpeakerSpan> voices,
        CallTranscriptLabels text)
    {
        if (session.Participants.Count == 1)
        {
            string only = session.Participants[0];
            return _ => only;
        }

        if (voices.Count == 0)
        {
            string fallback = Fallback(current, text);
            return _ => fallback;
        }

        IReadOnlyDictionary<int, string> names =
            SpeakerAssignment.Label(voices, session.Participants, text.UnknownSpeaker);

        return segment =>
        {
            int? speaker = SpeakerAssignment.For(segment.Start, segment.End, voices);

            // Реплика, не попавшая ни в один найденный участок, — обычное
            // дело: распознавание слышит речь там, где сегментация её не
            // разметила. Подписываем общим словом, а не ближайшим по времени
            // именем: приписать фразу конкретному человеку наугад хуже, чем
            // честно сказать «собеседник».
            return speaker is not null && names.TryGetValue(speaker.Value, out string? name)
                ? name
                : Fallback(current, text);
        };
    }

    /// <summary>Общее слово для собеседника, когда имени нет.</summary>
    private static string Fallback(AppSettings current, CallTranscriptLabels text) =>
        string.IsNullOrWhiteSpace(current.OtherSideName) ? text.UnknownSpeaker : current.OtherSideName;

    /// <summary>Убрать галлюцинации и применить пользовательский словарь.</summary>
    private static IReadOnlyList<TranscriptSegment> Clean(
        IReadOnlyList<TranscriptSegment> segments,
        AppSettings appSettings) =>
        [
            .. TextPostProcessor.RemoveHallucinations(segments)
                .Select(s => s with { Text = TextPostProcessor.ApplyReplacements(s.Text, appSettings.Replacements) })
                .Where(s => s.Text.Length > 0),
        ];

    private static string Render(
        CallSession session,
        AppSettings appSettings,
        IReadOnlyList<Utterance> timeline,
        BleedFilter.Result filtered,
        IReadOnlyList<SpeakerSpan> voices,
        CallTranscriptLabels text)
    {
        var markdown = new StringBuilder();
        markdown.Append("# ").AppendLine(Path.GetFileName(session.Directory)).AppendLine();

        markdown.Append("- ").Append(text.StartedAt).Append(": ")
            .AppendLine(session.StartedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture));
        markdown.Append("- ").Append(text.Duration).Append(": ")
            .AppendLine(Stamp(session.Duration));

        if (!string.IsNullOrWhiteSpace(session.Trigger))
        {
            markdown.Append("- ").Append(text.Trigger).Append(": ").AppendLine(session.Trigger);
        }

        // Как именно разделены голоса — в шапку. Читающий транскрипт должен
        // видеть, откуда взялись имена: по названным участникам или машина
        // угадала сама. Это разные степени доверия к подписям.
        if (voices.Count > 0)
        {
            int found = voices.Select(v => v.Speaker).Distinct().Count();
            markdown.Append("- ").Append(text.VoicesSplit).Append(": ")
                .Append(found.ToString(CultureInfo.CurrentCulture))
                .Append(" (")
                .Append(session.Participants.Count > 1 ? text.VoicesSplitHinted : text.VoicesSplitGuessed)
                .AppendLine(")");
        }

        if (session.Participants.Count > 0)
        {
            markdown.Append("- ").Append(text.Participants).Append(": ")
                .AppendLine(string.Join(", ", new[] { appSettings.EffectiveMyName }.Concat(session.Participants)));
        }

        // Честно пишем, сколько реплик отсеяно: если фильтр переусердствовал,
        // это единственный способ заметить пропажу, не переслушивая запись.
        if (filtered.RemovedTotal > 0)
        {
            markdown.Append("- ").Append(text.BleedRemoved).Append(": ")
                .Append(filtered.RemovedTotal.ToString(CultureInfo.CurrentCulture))
                .Append(" (").Append(text.BleedByText).Append(' ')
                .Append(filtered.RemovedByText.ToString(CultureInfo.CurrentCulture))
                .Append(", ").Append(text.BleedByEnergy).Append(' ')
                .Append(filtered.RemovedByEnergy.ToString(CultureInfo.CurrentCulture))
                .AppendLine(")");
        }

        markdown.AppendLine().AppendLine("---").AppendLine();

        if (timeline.Count == 0)
        {
            markdown.AppendLine(text.NothingRecognized);
            return markdown.ToString();
        }

        TimeSpan headerAt = TimeSpan.MinValue;
        string? headerSpeaker = null;

        foreach (Utterance utterance in timeline)
        {
            // Подпись ставится на СМЕНЕ говорящего, а не на каждом сегменте.
            // Whisper режет речь на куски по несколько секунд, и штамп на
            // каждом превращал двухминутный монолог в сорок одинаковых строк
            // «[2:14] Кирилл:», между которыми терялся сам текст.
            //
            // Внутри длинного монолога подпись всё-таки повторяется: без
            // отметок времени в получасовой реплике невозможно найти место
            // в записи, а ради этого транскрипт и держат рядом со звуком.
            bool speakerChanged = utterance.Speaker != headerSpeaker;
            bool longSinceHeader = utterance.Start - headerAt >= HeaderInterval;

            if (speakerChanged || longSinceHeader)
            {
                markdown.Append("**[").Append(Stamp(utterance.Start)).Append("] ")
                    .Append(utterance.Speaker).Append(":** ");

                headerSpeaker = utterance.Speaker;
                headerAt = utterance.Start;
            }

            markdown.AppendLine(utterance.Text).AppendLine();
        }

        return markdown.ToString();
    }

    /// <summary>Как часто повторять подпись внутри длинной реплики одного человека.</summary>
    private static readonly TimeSpan HeaderInterval = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Отметка времени.
    /// </summary>
    /// <remarks>
    /// Минуты считаем через <c>TotalMinutes</c>, а не форматом <c>mm</c>:
    /// тот обнуляется на шестидесятой минуте, а звонок бывает и длиннее.
    /// </remarks>
    private static string Stamp(TimeSpan time) =>
        $"{(int)time.TotalMinutes}:{time.Seconds:D2}";
}
