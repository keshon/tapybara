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

/// <summary>Где сейчас распознавание звонка.</summary>
/// <param name="Stage">Этап.</param>
/// <param name="Percent">Доля всей работы, 0–100, а не только текущего этапа.</param>
/// <remarks>
/// Процент общий. Прежде интерфейс знал только этап, и «Распознаю
/// собеседников…» висело без движения по нескольку минут — неотличимо от
/// зависания.
/// </remarks>
public readonly record struct CallTranscriptionProgress(CallTranscriptionStage Stage, int Percent);

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
    string Voice,
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
        "with the participant count as a hint",
        "without a hint",
        "Someone",
        "Voice",
        "Other-side speech removed from your channel",
        "by text",
        "by loudness",
        "_No speech recognised._");
}

/// <summary>
/// Сборка транскрипта звонка из двух каналов.
/// </summary>
/// <remarks>
/// <para>
/// Каналы распознаются раздельно, поэтому «кто говорит» известно заранее:
/// микрофон — владелец, системный звук — собеседники. Разделение по голосу
/// нужно только чтобы разделить нескольких собеседников между собой, и для
/// разговора один на один не требуется вовсе.
/// </para>
/// <para>
/// Три операции разной цены. <see cref="TranscribeAsync"/> — распознавание,
/// минуты на час записи. <see cref="ResplitAsync"/> — только разделение
/// голосов по уже распознанному, минута процессора. <see cref="Render"/> —
/// отрисовка, миллисекунды. Раньше существовала только первая, и любая
/// правка имени стоила полного распознавания.
/// </para>
/// </remarks>
public sealed class CallTranscriber(
    SpeechTranscriber transcriber,
    Func<AppSettings> settings,
    Func<CallTranscriptLabels>? labels = null,
    Func<SpeakerDiarizer?>? diarizer = null)
{
    /// <summary>
    /// Где кончается каждый этап — в процентах всей работы.
    /// </summary>
    /// <remarks>
    /// Оценка, а не замер: дорожки одной длины, и распознавание каждой
    /// стоит примерно одинаково; разделение голосов на процессоре — примерно
    /// четверть распознавания на видеокарте. Точность здесь не нужна, нужно,
    /// чтобы полоса двигалась и не откатывалась.
    /// </remarks>
    private const int MicrophoneEnds = 40;
    private const int OtherSideEnds = 82;
    private const int FilteringEnds = 85;
    private const int SplittingEnds = 99;

    /// <summary>Распознать звонок и записать транскрипт в его папку.</summary>
    /// <returns>Путь к <c>transcript.md</c>.</returns>
    public async Task<string> TranscribeAsync(
        CallSession session,
        IProgress<CallTranscriptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        AppSettings current = settings();

        if (!File.Exists(session.MicPath) || !File.Exists(session.SystemPath))
        {
            throw new FileNotFoundException(
                "В папке звонка нет дорожек mic.wav и system.wav.", session.MicPath);
        }

        // Оборванная запись — обычное дело: приложение могли закрыть жёстко.
        // Чиним заголовки, прежде чем пытаться читать.
        CallRepair.RepairCall(session);

        progress?.Report(new(CallTranscriptionStage.ReadingTracks, 0));

        // Каналы обрабатываем ПО ОЧЕРЕДИ и массив отпускаем сразу. Держать оба
        // часовых канала в памяти — это около гигабайта в куче больших
        // объектов ради нескольких десятков средних значений, которые
        // прекрасно считаются по огибающей.
        (IReadOnlyList<TranscriptSegment> micSegments, EnergyEnvelope micEnergy) = await ProcessChannelAsync(
            session.MicPath,
            // Свой канал распознаём заданным языком: что говорит владелец
            // микрофона, известно заранее.
            current.Language,
            current,
            Stage(progress, CallTranscriptionStage.TranscribingMicrophone, 0, MicrophoneEnds),
            cancellationToken).ConfigureAwait(false);

        (IReadOnlyList<TranscriptSegment> systemSegments, EnergyEnvelope systemEnergy) = await ProcessChannelAsync(
            session.SystemPath,
            // Чужой канал — определением языка. Навязанный не тому каналу язык
            // не «слегка ухудшает» распознавание, а превращает речь в бессмыслицу.
            current.OtherSideLanguage,
            current,
            Stage(progress, CallTranscriptionStage.TranscribingOtherSide, MicrophoneEnds, OtherSideEnds),
            cancellationToken).ConfigureAwait(false);

        progress?.Report(new(CallTranscriptionStage.FilteringBleed, OtherSideEnds));
        BleedFilter.Result filtered = BleedFilter.Apply(micSegments, systemSegments, micEnergy, systemEnergy);

        List<CallLine> lines =
        [
            .. filtered.Kept.Select(s => new CallLine(CallChannel.Mine, s.Start, s.End, s.Text)),
            .. systemSegments.Select(s => new CallLine(CallChannel.Theirs, s.Start, s.End, s.Text)),
        ];

        lines.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Участников перечитываем с диска именно здесь, а не берём из
        // аргумента. Распознавание начинается сразу после остановки записи,
        // и человек отмечает, кто был на звонке, пока оно идёт. Сколько
        // голосов искать, нужно знать только к этому месту — и к этому
        // месту ответ обычно уже есть.
        CallSession fresh = CallMeta.Load(session.Directory) ?? session;
        int expected = ExpectedVoices(fresh, current);

        var transcript = new CallTranscript
        {
            Lines = lines,
            RemovedByText = filtered.RemovedByText,
            RemovedByEnergy = filtered.RemovedByEnergy,
        };

        if (expected != 0)
        {
            transcript = await SplitAsync(session, transcript, expected, current, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        string path = Store(session.Directory, transcript, current);

        AppLog.Info($"Транскрипт готов: {path}, отсеяно {filtered.RemovedTotal} реплик, голосов {transcript.Voices.Count}.");
        progress?.Report(new(CallTranscriptionStage.Done, 100));
        return path;
    }

    /// <summary>
    /// Разделить голоса заново — по уже распознанному, без Whisper.
    /// </summary>
    /// <param name="callDirectory">Папка звонка.</param>
    /// <param name="expectedVoices">Сколько голосов искать; ноль или меньше — без подсказки.</param>
    /// <param name="progress">Ход работы.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Путь к транскрипту, или <c>null</c>, если разделять нечем или не по чему.</returns>
    /// <remarks>
    /// Нужна, когда человек отметил участников уже после разделения —
    /// «их было трое», а искали без подсказки или двоих. Подсказка о числе
    /// голосов — самый сильный рычаг точности, и применить её стоит минуту
    /// процессора, а не повторное распознавание часа записи.
    /// </remarks>
    public async Task<string?> ResplitAsync(
        string callDirectory,
        int expectedVoices,
        IProgress<CallTranscriptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (CallMeta.Load(callDirectory) is not { } session
            || CallTranscriptStore.Load(callDirectory) is not { } transcript
            || diarizer?.Invoke() is null)
        {
            return null;
        }

        AppSettings current = settings();
        CallTranscript split = await SplitAsync(
            session,
            transcript,
            expectedVoices > 0 ? expectedVoices : -1,
            current,
            progress,
            cancellationToken).ConfigureAwait(false);

        string path = Store(callDirectory, split, current);
        progress?.Report(new(CallTranscriptionStage.Done, 100));
        return path;
    }

    /// <summary>
    /// Привести транскрипт в соответствие с тем, что человек сказал об участниках.
    /// </summary>
    /// <returns>Путь к транскрипту, или <c>null</c>, если распознанных данных нет.</returns>
    /// <remarks>
    /// Зовётся после правки участников. Если отмечено несколько человек, а
    /// голоса искали под другое число, — разделяем заново. Иначе хватает
    /// перерисовки: один отмеченный участник и так подписывает весь чужой
    /// канал, а имена голосов берутся из меты при отрисовке.
    /// </remarks>
    public async Task<string?> ReconcileAsync(
        string callDirectory,
        IProgress<CallTranscriptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (CallMeta.Load(callDirectory) is not { } session
            || CallTranscriptStore.Load(callDirectory) is not { } transcript)
        {
            return null;
        }

        AppSettings current = settings();
        int expected = ExpectedVoices(session, current);

        if (expected > 0 && transcript.ExpectedVoices != expected && diarizer?.Invoke() is not null)
        {
            AppLog.Info($"Участников стало {expected}, голоса искали под {transcript.ExpectedVoices} — разделяю заново.");
            return await ResplitAsync(callDirectory, expected, progress, cancellationToken).ConfigureAwait(false);
        }

        return Render(callDirectory);
    }

    /// <summary>Перерисовать <c>transcript.md</c> по сохранённым данным. Мгновенно.</summary>
    /// <returns>Путь к транскрипту, или <c>null</c>, если распознанных данных нет.</returns>
    public string? Render(string callDirectory)
    {
        AppSettings current = settings();
        CallTranscriptLabels text = Labels();

        return CallTranscriptRenderer.Write(callDirectory, current.EffectiveMyName, Fallback(current, text), text);
    }

    /// <summary>
    /// Сколько голосов искать в чужом канале.
    /// </summary>
    /// <returns>Ноль — не разделять; меньше нуля — без подсказки; больше — столько.</returns>
    /// <remarks>
    /// Один отмеченный собеседник — разделять нечего: его именем подписывается
    /// весь канал, и это бесплатно и точно. Ни одного — ищем без подсказки:
    /// людей там может быть и трое, просто их не отметили, а «Собеседник» на
    /// всех хуже, чем «Голос A» и «Голос B».
    /// </remarks>
    internal static int ExpectedVoices(CallSession session, AppSettings current)
    {
        if (!current.SplitVoices || session.Participants.Count == 1)
        {
            return 0;
        }

        return session.Participants.Count > 1 ? session.Participants.Count : -1;
    }

    /// <summary>Разделить голоса на чужой дорожке и разметить реплики.</summary>
    /// <remarks>
    /// Дорожка читается заново, а не берётся из распознавания: к этому
    /// моменту её массив уже отпущен, и держать его ради разделения значило бы
    /// держать в памяти лишние сотни мегабайт на всё время распознавания.
    /// Нормализованная копия — потому что сегментация решает по энергии, как
    /// и детектор речи.
    /// </remarks>
    private async Task<CallTranscript> SplitAsync(
        CallSession session,
        CallTranscript transcript,
        int expected,
        AppSettings current,
        IProgress<CallTranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (diarizer?.Invoke() is not { } splitter)
        {
            return transcript;
        }

        progress?.Report(new(CallTranscriptionStage.SplittingVoices, FilteringEnds));

        float[] samples = AudioFile.ReadMono16k(session.SystemPath);
        if (samples.Length == 0)
        {
            return transcript;
        }

        float[] audio = current.NormalizeAudio ? AudioNormalizer.Normalize(samples) : samples;
        IProgress<int>? percent = Stage(progress, CallTranscriptionStage.SplittingVoices, FilteringEnds, SplittingEnds);

        IReadOnlyList<SpeakerSpan> spans = await Task.Run(
            () => splitter.Split(audio, expected > 0 ? expected : 0, percent, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        List<CallLine> unassigned = [.. transcript.Lines.Select(l => l.Voice is null ? l : l with { Voice = null })];

        return transcript with
        {
            Lines = CallVoices.Assign(unassigned, spans),
            VoicesSplit = true,
            ExpectedVoices = Math.Max(expected, 0),
        };
    }

    /// <summary>
    /// Записать данные, найденные голоса в мету — и отрисовать.
    /// </summary>
    /// <remarks>
    /// Имена голосов при этом сбрасываются. Буквы раздаются заново по
    /// порядку появления, и «B» нового разделения — не обязательно «B»
    /// прежнего. Оставить старые имена значило бы молча подписать реплики
    /// чужим именем, а именно с этим переделка и боролась.
    /// </remarks>
    private string Store(string callDirectory, CallTranscript transcript, AppSettings current)
    {
        CallTranscriptStore.Save(callDirectory, transcript);
        CallMeta.Update(callDirectory, s => s with
        {
            Voices = transcript.Voices,
            VoiceNames = new Dictionary<string, string>(),
        });

        CallTranscriptLabels text = Labels();
        return CallTranscriptRenderer.Write(callDirectory, current.EffectiveMyName, Fallback(current, text), text)
               ?? Path.Combine(callDirectory, CallSession.TranscriptFileName);
    }

    /// <summary>
    /// Прочитать канал, распознать его и снять огибающую.
    /// </summary>
    /// <remarks>
    /// Массив сэмплов живёт только внутри этого метода: наружу уходят сегменты
    /// и огибающая, вместе занимающие меньше мегабайта на час записи.
    /// </remarks>
    private async Task<(IReadOnlyList<TranscriptSegment> Segments, EnergyEnvelope Energy)> ProcessChannelAsync(
        string path,
        string language,
        AppSettings current,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        float[] samples = AudioFile.ReadMono16k(path);
        if (samples.Length == 0)
        {
            return ([], EnergyEnvelope.Empty);
        }

        // Огибающую снимаем ДО нормализации: анти-bleed сравнивает каналы
        // между собой, а нормализация усиливает каждый по-своему.
        EnergyEnvelope energy = EnergyEnvelope.Build(samples);

        IReadOnlyList<TranscriptSegment> segments = await transcriber
            .TranscribeAsync(samples, progress, language: language, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return (Clean(segments, current), energy);
    }

    /// <summary>Пересчитать проценты этапа в проценты всей работы.</summary>
    private static Progress<int>? Stage(
        IProgress<CallTranscriptionProgress>? progress,
        CallTranscriptionStage stage,
        int from,
        int to)
    {
        if (progress is null)
        {
            return null;
        }

        progress.Report(new(stage, from));
        return new Progress<int>(percent =>
            progress.Report(new(stage, from + ((to - from) * Math.Clamp(percent, 0, 100) / 100))));
    }

    private CallTranscriptLabels Labels() => labels?.Invoke() ?? CallTranscriptLabels.Default;

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
}
