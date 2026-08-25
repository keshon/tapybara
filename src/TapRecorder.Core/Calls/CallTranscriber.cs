using System.Globalization;
using System.Text;
using TapRecorder.Core.Audio;
using TapRecorder.Core.Settings;
using TapRecorder.Core.Speech;

namespace TapRecorder.Core.Calls;

/// <summary>
/// Сборка транскрипта звонка из двух каналов.
/// </summary>
/// <remarks>
/// Каналы распознаются раздельно, поэтому «кто говорит» известно заранее:
/// микрофон — владелец, системный звук — собеседники. Диаризация по голосу
/// нужна только чтобы разделить нескольких собеседников между собой, и для
/// разговора один на один не требуется вовсе.
/// </remarks>
public sealed class CallTranscriber(SpeechTranscriber transcriber, Func<AppSettings> settings)
{
    /// <summary>Одна реплика в общей хронологии.</summary>
    private sealed record Utterance(TimeSpan Start, string Speaker, string Text);

    /// <summary>Распознать звонок и записать <c>transcript.md</c> в его папку.</summary>
    /// <returns>Путь к готовому транскрипту.</returns>
    public async Task<string> TranscribeAsync(
        CallSession session,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        AppSettings current = settings();

        progress?.Report("Читаю дорожки");
        float[] micSamples = AudioFile.ReadMono16k(session.MicPath);
        float[] systemSamples = AudioFile.ReadMono16k(session.SystemPath);

        progress?.Report("Распознаю микрофон");
        // Свой канал распознаём заданным языком: что говорит владелец
        // микрофона, известно заранее.
        IReadOnlyList<TranscriptSegment> micSegments = await transcriber
            .TranscribeAsync(micSamples, language: current.Language, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        progress?.Report("Распознаю собеседников");
        // Чужой канал — определением языка. Навязанный не тому каналу язык
        // не «слегка ухудшает» распознавание, а превращает речь в бессмыслицу.
        IReadOnlyList<TranscriptSegment> systemSegments = await transcriber
            .TranscribeAsync(systemSamples, language: current.OtherSideLanguage, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        progress?.Report("Отсеиваю чужую речь из своего канала");
        BleedFilter.Result filtered = BleedFilter.Apply(
            Clean(micSegments, current),
            Clean(systemSegments, current),
            micSamples,
            systemSamples);

        string otherSide = session.Participants.Count == 1
            ? session.Participants[0]
            : current.OtherSideName;

        List<Utterance> timeline =
        [
            .. filtered.Kept.Select(s => new Utterance(s.Start, current.MyName, s.Text)),
            .. Clean(systemSegments, current).Select(s => new Utterance(s.Start, otherSide, s.Text)),
        ];

        timeline.Sort((a, b) => a.Start.CompareTo(b.Start));

        string markdown = Render(session, current, timeline, filtered);
        await File.WriteAllTextAsync(session.TranscriptPath, markdown, cancellationToken).ConfigureAwait(false);

        progress?.Report($"Готово: убрано чужой речи {filtered.RemovedTotal}");
        return session.TranscriptPath;
    }

    /// <summary>Убрать галлюцинации и применить пользовательский словарь.</summary>
    private static IReadOnlyList<TranscriptSegment> Clean(
        IReadOnlyList<TranscriptSegment> segments,
        AppSettings appSettings) =>
        [
            .. segments
                .Where(s => !TextPostProcessor.IsHallucination(s.Text))
                .Select(s => s with { Text = TextPostProcessor.ApplyReplacements(s.Text, appSettings.Replacements) })
                .Where(s => s.Text.Length > 0),
        ];

    private static string Render(
        CallSession session,
        AppSettings appSettings,
        IReadOnlyList<Utterance> timeline,
        BleedFilter.Result filtered)
    {
        var text = new StringBuilder();
        text.Append("# ").AppendLine(Path.GetFileName(session.Directory)).AppendLine();

        text.Append("- Начало: ")
            .AppendLine(session.StartedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture));
        text.Append("- Длительность: ")
            .AppendLine(Stamp(session.Duration));

        if (!string.IsNullOrWhiteSpace(session.Trigger))
        {
            text.Append("- Триггер: ").AppendLine(session.Trigger);
        }

        if (session.Participants.Count > 0)
        {
            text.Append("- Участники: ")
                .AppendLine(string.Join(", ", new[] { appSettings.MyName }.Concat(session.Participants)));
        }

        // Честно пишем, сколько реплик отсеяно: если фильтр переусердствовал,
        // это единственный способ заметить пропажу, не переслушивая запись.
        if (filtered.RemovedTotal > 0)
        {
            text.Append("- Отсеяно чужой речи из своего канала: ")
                .Append(filtered.RemovedTotal.ToString(CultureInfo.CurrentCulture))
                .Append(" (по тексту ")
                .Append(filtered.RemovedByText.ToString(CultureInfo.CurrentCulture))
                .Append(", по громкости ")
                .Append(filtered.RemovedByEnergy.ToString(CultureInfo.CurrentCulture))
                .AppendLine(")");
        }

        text.AppendLine().AppendLine("---").AppendLine();

        if (timeline.Count == 0)
        {
            text.AppendLine("_Речь не распознана._");
            return text.ToString();
        }

        foreach (Utterance utterance in timeline)
        {
            text.Append("**[").Append(Stamp(utterance.Start)).Append("] ")
                .Append(utterance.Speaker).Append(":** ")
                .AppendLine(utterance.Text)
                .AppendLine();
        }

        return text.ToString();
    }

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
