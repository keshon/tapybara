using Tapybara.Core.Audio;

namespace Tapybara.Core.Speech;

/// <summary>Что детектор нашёл при одном значении порога.</summary>
/// <param name="Threshold">Проверенный порог.</param>
/// <param name="Regions">Сколько участков речи найдено.</param>
/// <param name="SpeechDuration">Суммарная длительность найденной речи.</param>
public sealed record VadProbe(float Threshold, int Regions, TimeSpan SpeechDuration);

/// <summary>
/// Подбор порога детектора речи по образцу голоса.
/// </summary>
/// <remarks>
/// <para>
/// Правильный порог зависит от микрофона, комнаты и голоса — угадать его
/// нельзя, а измерить можно. Пользователь произносит несколько фраз, мы
/// прогоняем детектор на разных порогах и смотрим, сколько речи он находит.
/// </para>
/// <para>
/// Отправная точка — самый строгий порог из тех, что ещё не теряют речь:
/// строже означает меньше ложных срабатываний на шуме и щелчках. Но
/// рекомендуется на шаг мягче найденного, потому что образец снимается в
/// тепличных условиях, а живая речь тише и небрежнее. Потерянное слово не
/// восстановить, поэтому запас берётся в сторону чувствительности.
/// </para>
/// </remarks>
public static class VadCalibrator
{
    /// <summary>Проверяемые пороги — от чувствительного к строгому.</summary>
    private static readonly float[] Candidates = [0.15f, 0.25f, 0.35f, 0.45f, 0.55f, 0.65f];

    /// <summary>
    /// Доля речи, которую строгий порог обязан сохранить.
    /// </summary>
    /// <remarks>
    /// Не 100%: у самого чувствительного порога в «речь» попадает и часть
    /// шума, поэтому требовать полного совпадения означало бы всегда выбирать
    /// минимум. Пятнадцать процентов — допуск на этот шум.
    /// </remarks>
    private const double KeepShare = 0.85;

    /// <summary>Прогнать образец через детектор на всех проверяемых порогах.</summary>
    public static async Task<IReadOnlyList<VadProbe>> ProbeAsync(
        float[] samples,
        string vadModelPath,
        bool normalize = true,
        CancellationToken cancellationToken = default)
    {
        // Нормализуем один раз и тем же способом, что и рабочий путь: иначе
        // подобранный порог не будет соответствовать реальной работе.
        float[] audio = normalize ? AudioNormalizer.Normalize(samples) : samples;

        var probes = new List<VadProbe>(Candidates.Length);
        foreach (float threshold in Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var detector = new SpeechDetector(new SpeechDetectorOptions
            {
                ModelPath = vadModelPath,
                Threshold = threshold,
            });

            IReadOnlyList<SpeechRegion> regions =
                await detector.DetectAsync(audio, cancellationToken).ConfigureAwait(false);

            TimeSpan total = regions.Aggregate(TimeSpan.Zero, (sum, r) => sum + r.Duration);
            probes.Add(new VadProbe(threshold, regions.Count, total));
        }

        return probes;
    }

    /// <summary>Самый строгий порог, который ещё не теряет речь.</summary>
    /// <returns>Рекомендуемое значение, или <c>null</c>, если речи не нашлось вовсе.</returns>
    public static float? Recommend(IReadOnlyList<VadProbe> probes)
    {
        TimeSpan best = probes.Count == 0
            ? TimeSpan.Zero
            : probes.Max(p => p.SpeechDuration);

        if (best <= TimeSpan.FromMilliseconds(500))
        {
            return null; // речи по сути нет — рекомендовать нечего
        }

        VadProbe[] acceptable = [.. probes.Where(p => p.SpeechDuration.TotalSeconds >= best.TotalSeconds * KeepShare)];
        if (acceptable.Length == 0)
        {
            return null;
        }

        float strictest = acceptable.Max(p => p.Threshold);

        // Шаг назад от самого строгого прошедшего порога. Образец снимается в
        // тепличных условиях: человек сознательно считает вслух, чётко и ровно.
        // Живая речь тише, небрежнее и с проглоченными окончаниями, поэтому
        // порог, впритык прошедший тест, на реальном разговоре начнёт терять
        // слова. Запас в один шаг — плата за эту разницу.
        int index = Array.IndexOf(Candidates, strictest);
        return index > 0 ? Candidates[index - 1] : strictest;
    }
}
