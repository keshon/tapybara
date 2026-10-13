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
    Func<SpeakerDiarizer?>? diarizer = null,
    Func<VoiceprintExtractor?>? voiceprints = null,
    Action<Action>? commitBoundary = null)
{
    /// <summary>Задание получает свою точку фиксации, не меняя общий распознаватель.</summary>
    public CallTranscriber WithCommitBoundary(Action<Action> commit) =>
        new(transcriber, settings, labels, diarizer, voiceprints, commit);

    private T Commit<T>(Func<T> write, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (commitBoundary is null)
        {
            return write();
        }

        T result = default!;
        commitBoundary(() => result = write());
        return result;
    }
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
        long? startingRevision = CallTranscriptStore.Load(session.Directory)?.Revision;
        AppSettings current = settings();

        if ((!session.ImportedMedia && !File.Exists(session.MicPath)) || !File.Exists(session.SystemPath))
        {
            throw new FileNotFoundException(
                "В папке звонка нет дорожек mic.wav и system.wav.", session.MicPath);
        }

        // Оборванная запись — обычное дело: приложение могли закрыть жёстко.
        // Чиним заголовки, прежде чем пытаться читать.
        CallRepair.RepairCall(session, cancellationToken);

        progress?.Report(new(CallTranscriptionStage.ReadingTracks, 0));

        // Каналы обрабатываем ПО ОЧЕРЕДИ и массив отпускаем сразу. Держать оба
        // часовых канала в памяти — это около гигабайта в куче больших
        // объектов ради нескольких десятков средних значений, которые
        // прекрасно считаются по огибающей.
        (IReadOnlyList<TranscriptSegment> micSegments, EnergyEnvelope micEnergy) = session.ImportedMedia ? (Array.Empty<TranscriptSegment>(), EnergyEnvelope.Empty) : await ProcessChannelAsync(
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
            session.ImportedMedia ? session.ImportLanguage ?? current.Language : current.OtherSideLanguage,
            current,
            Stage(progress, CallTranscriptionStage.TranscribingOtherSide, session.ImportedMedia ? 0 : MicrophoneEnds, OtherSideEnds),
            cancellationToken).ConfigureAwait(false);

        if (!session.ImportedMedia)
        {
            progress?.Report(new(CallTranscriptionStage.FilteringBleed, OtherSideEnds));
        }

        BleedFilter.Result filtered = session.ImportedMedia ? new([], 0, 0)
            : BleedFilter.Apply(micSegments, systemSegments, micEnergy, systemEnergy);

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

        transcript = await CheckVoicesAsync(session, transcript, current, cancellationToken).ConfigureAwait(false);
        transcript = await WithPrintsAsync(session, transcript, current, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        string path = Commit(() => Store(session.Directory, transcript, current, startingRevision)
            ?? throw new InvalidOperationException("Транскрипт изменился во время распознавания; результат не сохранён."), cancellationToken);

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
            SpeakerSeparation.AnchorNames(transcript, session.VoiceNames),
            expectedVoices > 0 ? expectedVoices : -1,
            current,
            progress,
            cancellationToken).ConfigureAwait(false);

        split = await CheckVoicesAsync(session, split, current, cancellationToken).ConfigureAwait(false);
        split = await WithPrintsAsync(session, split, current, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        string? path = Commit(() => Keep(callDirectory, split), cancellationToken);
        if (path is null)
        {
            return null;
        }
        progress?.Report(new(CallTranscriptionStage.Done, 100));
        return path;
    }

    /// <summary>
    /// Проверить реплики по слепкам голоса у звонка, которого проверка ещё не касалась.
    /// </summary>
    /// <returns>Путь к транскрипту, или <c>null</c>, если проверять нечего или нечем.</returns>
    /// <remarks>
    /// Для звонков, распознанных до появления проверки: они открываются, и
    /// проверка проходит один раз, в фоне, — полминуты процессора на час
    /// записи. Имена голосов сохраняются: реплики переезжают только между
    /// уже найденными голосами, и «B» остаётся «B».
    /// </remarks>
    public Task<string?> VerifyAsync(
        string callDirectory,
        IProgress<CallTranscriptionProgress>? progress = null,
        CancellationToken cancellationToken = default) => VerifyCoreAsync(callDirectory, false, cancellationToken);

    public Task<string?> RecheckAsync(string callDirectory, CancellationToken cancellationToken = default) =>
        VerifyCoreAsync(callDirectory, true, cancellationToken);

    private async Task<string?> VerifyCoreAsync(string callDirectory, bool force, CancellationToken cancellationToken)
    {
        if (CallMeta.Load(callDirectory) is not { } session
            || CallTranscriptStore.Load(callDirectory) is not { } transcript || SpeakerSeparation.NeedsCount(transcript) || (!force && transcript.VoicesChecked)
            || voiceprints?.Invoke() is null)
        {
            return null;
        }

        AppSettings current = settings();
        CallTranscript checkedOne = await CheckVoicesAsync(session, transcript, current, cancellationToken).ConfigureAwait(false);
        if (!checkedOne.VoicesChecked)
        {
            return null;
        }

        checkedOne = await WithPrintsAsync(session, checkedOne, current, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Commit(() => Keep(callDirectory, checkedOne), cancellationToken);
    }

    /// <summary>
    /// Снять слепки голосов заново — после того как человек переселил реплики.
    /// </summary>
    /// <returns>Путь к транскрипту, или <c>null</c>, если нечего или нечем.</returns>
    /// <remarks>
    /// Слепок голоса снят с его реплик. Человек убрал из голоса чужую — и
    /// слепок, снятый до этого, всё ещё помнит чужой голос: по нему пошла бы
    /// подсказка на следующем звонке, а при назывании он лёг бы в книгу
    /// голосов. Проверка реплик здесь не повторяется: она могла бы вернуть
    /// реплику туда, откуда человек её только что убрал.
    /// </remarks>
    public async Task<string?> RefreshPrintsAsync(
        string callDirectory,
        IProgress<CallTranscriptionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (CallMeta.Load(callDirectory) is not { } session
            || CallTranscriptStore.Load(callDirectory) is not { } transcript)
        {
            return null;
        }

        CallTranscript refreshed = await WithPrintsAsync(session, transcript, settings(), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Commit(() => CallTranscriptStore.TryUpdate(callDirectory, transcript.Revision, t => t with { VoicePrints = refreshed.VoicePrints }) is null
            ? null
            : Render(callDirectory), cancellationToken);
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

        return Commit(() => Render(callDirectory), cancellationToken);
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
        if (session.SpeakerCountHint is > 0)
        {
            return session.SpeakerCountHint.Value;
        }

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

        float[] samples = AudioFile.ReadMono16k(session.SystemPath, cancellationToken);
        if (samples.Length == 0)
        {
            return transcript;
        }

        float[] audio = current.NormalizeAudio ? AudioNormalizer.Normalize(samples) : samples;
        IProgress<int>? percent = Stage(progress, CallTranscriptionStage.SplittingVoices, FilteringEnds, SplittingEnds);

        IReadOnlyList<SpeakerSpan> spans = await Task.Run(
            () => splitter.Split(audio, expected > 0 ? expected : 0, percent, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return SpeakerSeparation.Apply(transcript, spans, expected);
    }

    /// <summary>
    /// Проверить реплики собеседников по слепкам голоса (<see cref="VoiceCheck"/>).
    /// </summary>
    /// <remarks>
    /// Не зависит от «Запоминать голоса»: слепки здесь не сохраняются, а
    /// служат только сверке реплик между собой. Сбой проверки не стоит
    /// транскрипта — он уже собран, просто останется без неё.
    /// </remarks>
    private async Task<CallTranscript> CheckVoicesAsync(
        CallSession session,
        CallTranscript transcript,
        AppSettings current,
        CancellationToken cancellationToken)
    {
        if (SpeakerSeparation.NeedsCount(transcript) || voiceprints?.Invoke() is not { } extractor
            || !transcript.Lines.Any(l => l.Channel == CallChannel.Theirs))
        {
            return transcript;
        }

        try
        {
            return await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    float[] audio = ReadOtherSide(session, current, cancellationToken);
                    if (audio.Length == 0)
                    {
                        return transcript;
                    }

                    // Один экстрактор на всё приложение: проверка старого звонка
                    // может совпасть со слепками нового.
                    CallTranscript checkedOne = VoiceCheck.Check(transcript, line =>
                    {
                        lock (extractor)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return extractor.Embed(VoiceCheck.Slice(audio, line));
                        }
                    }, cancellationToken);

                    int moved = checkedOne.Lines.Zip(transcript.Lines).Count(p => p.First.Voice != p.Second.Voice);
                    int doubtful = checkedOne.Lines.Count(l => l.IsDoubtful);
                    AppLog.Info($"Реплики сверены с голосами: переселено {moved}, под сомнением {doubtful}.");
                    return checkedOne;
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            AppLog.Warn("Не удалось сверить реплики с голосами — транскрипт останется без проверки.", ex);
            return transcript;
        }
    }

    /// <summary>Чужой канал целиком — нормализованный, если так настроено.</summary>
    private static float[] ReadOtherSide(CallSession session, AppSettings current, CancellationToken cancellationToken)
    {
        float[] samples = AudioFile.ReadMono16k(session.SystemPath, cancellationToken);
        return current.NormalizeAudio && samples.Length > 0 ? AudioNormalizer.Normalize(samples) : samples;
    }

    /// <summary>
    /// Записать проверенный транскрипт, НЕ сбрасывая имена голосов.
    /// </summary>
    /// <remarks>
    /// В отличие от <see cref="Store"/>: там голоса найдены заново и буквы
    /// значат другое, здесь — те же голоса, только реплики между ними
    /// уточнены. Голос, у которого не осталось реплик, уходит из списка.
    /// </remarks>
    private string? Keep(string callDirectory, CallTranscript transcript)
    {
        CallTranscript? saved = CallTranscriptStore.TrySave(callDirectory, transcript.Revision, transcript,
            saved => CallMeta.Update(callDirectory, s => s with { Voices = saved.Voices, SpeakerAnalysisNeedsCount = SpeakerSeparation.NeedsCount(saved),
                SpeakerCountHint = saved.ExpectedVoices > 0 ? saved.ExpectedVoices : s.SpeakerCountHint }));
        if (saved is null)
        {
            return null;
        }
        return Render(callDirectory);
    }

    /// <summary>
    /// Снять слепки голосов, если есть чем.
    /// </summary>
    /// <remarks>
    /// Слепки — для книги голосов: по ним на следующем звонке подсказывается
    /// имя. Нет модели слепков — нет и слепков, транскрипт от этого не хуже.
    /// Сбой здесь тоже не должен стоить транскрипта: он уже собран.
    /// </remarks>
    private async Task<CallTranscript> WithPrintsAsync(
        CallSession session,
        CallTranscript transcript,
        AppSettings current,
        CancellationToken cancellationToken)
    {
        if (SpeakerSeparation.NeedsCount(transcript) || !current.RememberVoices || voiceprints?.Invoke() is not { } extractor)
        {
            return transcript;
        }

        try
        {
            IReadOnlyDictionary<string, float[]> prints = await Task.Run(
                () =>
                {
                    float[] samples = AudioFile.ReadMono16k(session.SystemPath, cancellationToken);
                    float[] audio = current.NormalizeAudio ? AudioNormalizer.Normalize(samples) : samples;
                    return CallVoices.Prints(audio, transcript, speech =>
                    {
                        lock (extractor)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return extractor.Embed(speech);
                        }
                    }, cancellationToken: cancellationToken);
                },
                cancellationToken).ConfigureAwait(false);

            return transcript with { VoicePrints = prints };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            AppLog.Warn("Не удалось снять слепки голосов — звонок останется без подсказок имён.", ex);
            return transcript;
        }
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
    private string? Store(string callDirectory, CallTranscript transcript, AppSettings current, long? expectedRevision)
    {
        if (CallTranscriptStore.TrySave(callDirectory, expectedRevision, transcript, saved =>
            CallMeta.Update(callDirectory, s => s with
            {
                Voices = saved.Voices,
                SpeakerAnalysisNeedsCount = SpeakerSeparation.NeedsCount(saved),
                VoiceNames = s.VoiceNames.Where(p => saved.Lines.Any(l => l.Voice == p.Key
                    && l.HumanEdits.HasFlag(CallLineEdits.Speaker))).ToDictionary(),
            })) is null)
        {
            return null;
        }

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
        // Чтение и огибающая до первого await иначе занимали UI-поток.
        (float[] samples, EnergyEnvelope energy) = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            float[] audio = AudioFile.ReadMono16k(path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            EnergyEnvelope envelope = audio.Length == 0 ? EnergyEnvelope.Empty : EnergyEnvelope.Build(audio);
            cancellationToken.ThrowIfCancellationRequested();
            return (audio, envelope);
        }, cancellationToken).ConfigureAwait(false);
        if (samples.Length == 0)
        {
            return ([], EnergyEnvelope.Empty);
        }

        // Огибающую снимаем ДО нормализации: анти-bleed сравнивает каналы
        // между собой, а нормализация усиливает каждый по-своему.
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
