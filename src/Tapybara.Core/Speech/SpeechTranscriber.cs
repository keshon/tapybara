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

    /// <summary>Слишком короткие участки не распознаём: там нечего услышать.</summary>
    private static readonly TimeSpan MinRegion = TimeSpan.FromMilliseconds(300);

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

        var result = new List<TranscriptSegment>();
        for (int i = 0; i < regions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SpeechRegion region = regions[i];
            (int from, int count) = Slice(region, audio.Length);
            if (count <= 0)
            {
                continue;
            }

            IReadOnlyList<TranscriptSegment> segments = await engine
                .TranscribeAsync(audio.AsMemory(from, count), progress: null, language, cancellationToken)
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

            progress?.Report((i + 1) * 100 / regions.Count);
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
    private static IReadOnlyList<SpeechRegion> Merge(IReadOnlyList<SpeechRegion> regions)
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
