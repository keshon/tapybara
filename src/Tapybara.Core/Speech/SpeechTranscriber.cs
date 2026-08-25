using Tapybara.Core.Audio;

namespace Tapybara.Core.Speech;

/// <summary>
/// Распознавание с предварительным поиском речи.
/// </summary>
/// <remarks>
/// <para>
/// Композиция детектора и движка: сначала VAD находит участки речи с
/// измеренными границами, затем каждый участок распознаётся отдельно, и его
/// сегменты сдвигаются на фактическое смещение участка.
/// </para>
/// <para>
/// Именно сдвиг решает главную задачу. Внутри участка whisper по-прежнему
/// предсказывает тайминги, но участок короткий и целиком состоит из речи —
/// ошибаться там почти негде. А положение участка на записи мы знаем точно.
/// </para>
/// <para>
/// Без детектора класс просто прогоняет запись через движок целиком, поэтому
/// вызывающему коду не нужно ветвиться.
/// </para>
/// </remarks>
public sealed class SpeechTranscriber(WhisperEngine engine, SpeechDetector? detector, bool normalize = true)
{
    /// <summary>
    /// Участки ближе этого сливаются в один.
    /// </summary>
    /// <remarks>
    /// Каждый участок — отдельный запуск модели со своей платой за старт.
    /// Дробить фразу на «слово — пауза — слово» невыгодно и вредно: модель
    /// теряет контекст внутри предложения и хуже ставит знаки препинания.
    /// </remarks>
    private static readonly TimeSpan MergeGap = TimeSpan.FromMilliseconds(700);

    /// <summary>
    /// Слишком короткие участки не распознаём.
    /// </summary>
    /// <remarks>
    /// Порог низкий сознательно. Детектор отдаёт участки уже с запасом тишины
    /// по краям, поэтому здесь мы отсекаем не короткую речь, а огрызки в
    /// единицы миллисекунд. Прежние 300 мс рисковали съесть односложный
    /// ответ — «да», «нет», «угу», — а в транскрипте звонка пропавшее «нет»
    /// меняет смысл разговора.
    /// </remarks>
    private static readonly TimeSpan MinRegion = TimeSpan.FromMilliseconds(120);

    /// <summary>Движок, на котором работает этот распознаватель.</summary>
    public WhisperEngine Engine => engine;

    /// <summary>Прогреть движок заранее — пока пользователь ещё говорит.</summary>
    public Task PrepareAsync(CancellationToken cancellationToken = default) =>
        engine.LoadAsync(cancellationToken: cancellationToken);

    /// <summary>Распознать запись. Сэмплы — 16 кГц моно float32.</summary>
    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        float[] samples,
        IProgress<int>? progress = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        // Нормализация ДО детектора, а не только перед распознаванием:
        // детектор решает по энергии, и тихая запись не проходит его порог —
        // фраза теряется целиком, ещё не дойдя до модели.
        float[] audio = normalize ? AudioNormalizer.Normalize(samples) : samples;

        if (detector is null)
        {
            return await engine.TranscribeAsync(audio, progress, language, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<SpeechRegion> regions = Merge(
            await detector.DetectAsync(audio, cancellationToken).ConfigureAwait(false));

        if (regions.Count == 0)
        {
            // Детектор не нашёл речи. Это не повод молча вернуть пустоту: он
            // обучен на человеческом голосе и, например, синтезированную или
            // сильно зашумлённую речь может не признать. Если в записи есть
            // звук — распознаём целиком. Потерять канал хуже, чем получить
            // неточные тайминги: детектор здесь уточняет результат, а не
            // решает, быть ему или нет.
            return AudioNormalizer.HasAudibleContent(audio)
                ? await engine.TranscribeAsync(audio, progress, language, cancellationToken).ConfigureAwait(false)
                : [];
        }

        // Прогресс считаем по доле звука, а не по числу участков: участки
        // разной длины, и «один из трёх готов» после двадцатисекундной реплики
        // и после полусекундной означает совершенно разное. Ровно движущийся
        // индикатор — единственное, что отличает работу от зависания.
        double totalSeconds = regions.Sum(r => r.Duration.TotalSeconds);
        double doneSeconds = 0;

        var result = new List<TranscriptSegment>();
        foreach (SpeechRegion region in regions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (int from, int count) = Slice(region, audio.Length);
            if (count <= 0)
            {
                continue;
            }

            double regionShare = totalSeconds > 0 ? region.Duration.TotalSeconds / totalSeconds : 0;
            double regionStart = doneSeconds;

            IProgress<int>? inner = progress is null
                ? null
                : new Progress<int>(percent => progress.Report((int)Math.Clamp(
                    ((regionStart / Math.Max(totalSeconds, 0.001)) + (regionShare * percent / 100.0)) * 100,
                    0,
                    100)));

            IReadOnlyList<TranscriptSegment> segments = await engine
                .TranscribeAsync(audio.AsMemory(from, count), inner, language, cancellationToken)
                .ConfigureAwait(false);

            // Сдвиг на измеренное начало участка — то, ради чего всё затевалось.
            foreach (TranscriptSegment segment in segments)
            {
                result.Add(segment with
                {
                    Start = segment.Start + region.Start,
                    End = segment.End + region.Start,
                });
            }

            doneSeconds += region.Duration.TotalSeconds;
            progress?.Report((int)Math.Clamp(doneSeconds / Math.Max(totalSeconds, 0.001) * 100, 0, 100));
        }

        return result;
    }

    /// <summary>Границы участка в сэмплах, с защитой от выхода за массив.</summary>
    private static (int From, int Count) Slice(SpeechRegion region, int totalSamples)
    {
        int from = Math.Clamp((int)(region.Start.TotalSeconds * AudioCapture.TargetSampleRate), 0, totalSamples);
        int to = Math.Clamp((int)(region.End.TotalSeconds * AudioCapture.TargetSampleRate), from, totalSamples);
        return (from, to - from);
    }

    /// <summary>Слить близкие участки и выбросить слишком короткие.</summary>
    internal static IReadOnlyList<SpeechRegion> Merge(IReadOnlyList<SpeechRegion> regions)
    {
        if (regions.Count == 0)
        {
            return [];
        }

        var merged = new List<SpeechRegion>();
        SpeechRegion current = regions[0];

        foreach (SpeechRegion next in regions.Skip(1))
        {
            if (next.Start - current.End <= MergeGap)
            {
                current = current with { End = next.End };
                continue;
            }

            merged.Add(current);
            current = next;
        }

        merged.Add(current);
        return [.. merged.Where(r => r.Duration >= MinRegion)];
    }
}
