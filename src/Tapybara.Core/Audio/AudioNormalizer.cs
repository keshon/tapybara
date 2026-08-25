namespace Tapybara.Core.Audio;

/// <summary>
/// Приведение громкости записи к рабочему уровню перед распознаванием.
/// </summary>
/// <remarks>
/// <para>
/// Микрофон на гибкой стойке, отодвинутый от лица, даёт запись в разы тише
/// нормальной. Детектор речи от этого страдает напрямую: он решает по энергии,
/// и тихая речь просто не проходит порог — фраза теряется целиком. Whisper
/// устойчивее, но и он на тихом входе распознаёт заметно хуже.
/// </para>
/// <para>
/// Нормализация безопасна для энергетического анти-bleed фильтра: тот
/// сравнивает уровни относительно перцентиля СВОЕГО канала, а постоянное
/// усиление в такой разности сокращается.
/// </para>
/// </remarks>
public static class AudioNormalizer
{
    /// <summary>Целевой уровень речи в дБFS.</summary>
    /// <remarks>
    /// −20 дБFS — обычный уровень речи в звукозаписи: громко, но с запасом
    /// до перегрузки на всплесках.
    /// </remarks>
    private const double TargetDb = -20.0;

    /// <summary>
    /// Предел усиления.
    /// </summary>
    /// <remarks>
    /// Без него запись, где речи нет вовсе, была бы «нормализована» по шуму:
    /// тихое шипение выкрутилось бы до уровня речи, и модель принялась бы
    /// сочинять слова из ничего.
    /// </remarks>
    private const double MaxGainDb = 25.0;

    /// <summary>Окно для оценки уровня.</summary>
    private const int FrameSamples = AudioCapture.TargetSampleRate / 33; // ~30 мс

    /// <summary>
    /// Есть ли в записи что-то громче фонового шума.
    /// </summary>
    /// <remarks>
    /// Нужно, чтобы отличить «детектор не услышал речи» от «в файле пусто».
    /// В первом случае запись всё равно стоит распознать, во втором — нет.
    /// </remarks>
    public static bool HasAudibleContent(float[] samples) =>
        EstimateSpeechLevelDb(samples) is > -45;

    /// <summary>Вернуть копию записи, приведённую к рабочей громкости.</summary>
    public static float[] Normalize(float[] samples)
    {
        if (Gain(samples) is not { } gain)
        {
            return samples;
        }

        float[] result = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            // Жёсткое ограничение вместо переполнения: одиночный щелчок
            // дешевле, чем испорченная перегрузкой фраза.
            result[i] = (float)Math.Clamp(samples[i] * gain, -1.0, 1.0);
        }

        return result;
    }

    /// <summary>
    /// Привести громкость прямо в переданном массиве.
    /// </summary>
    /// <remarks>
    /// Для звонков это принципиально: час записи — это 230 МБ на канал, и
    /// копия ради усиления удваивала расход впустую. Диктовке хватает и
    /// обычной <see cref="Normalize"/>, но раз уж массив всё равно наш —
    /// незачем плодить второй.
    /// </remarks>
    public static void NormalizeInPlace(float[] samples)
    {
        if (Gain(samples) is not { } gain)
        {
            return;
        }

        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Math.Clamp(samples[i] * gain, -1.0, 1.0);
        }
    }

    /// <summary>Во сколько раз усилить, или <c>null</c>, если усиливать не надо.</summary>
    private static double? Gain(float[] samples)
    {
        double? speechLevel = EstimateSpeechLevelDb(samples);
        if (speechLevel is null)
        {
            return null;
        }

        double gainDb = Math.Min(TargetDb - speechLevel.Value, MaxGainDb);

        // Тише делать не будем: перегруженную запись это не чинит, а тихую
        // портит. Ослабление оставлено ограничителю ниже.
        return gainDb <= 0 ? null : Math.Pow(10, gainDb / 20);
    }

    /// <summary>
    /// Оценить уровень именно речи, а не записи целиком.
    /// </summary>
    /// <remarks>
    /// Берётся 90-й перцентиль громкости окон. Среднее по всей записи не
    /// годится: в звонке большая часть дорожки — тишина, и она утянула бы
    /// оценку вниз, а нормализация выкрутила бы шум.
    /// </remarks>
    private static double? EstimateSpeechLevelDb(float[] samples)
    {
        if (samples.Length < FrameSamples)
        {
            return null;
        }

        var levels = new List<double>(samples.Length / FrameSamples);
        for (int start = 0; start + FrameSamples <= samples.Length; start += FrameSamples)
        {
            double sum = 0;
            for (int i = start; i < start + FrameSamples; i++)
            {
                sum += (double)samples[i] * samples[i];
            }

            double rms = Math.Sqrt(sum / FrameSamples);
            if (rms > 1e-5)
            {
                levels.Add(20 * Math.Log10(rms));
            }
        }

        if (levels.Count == 0)
        {
            return null; // тишина от края до края — усиливать нечего
        }

        levels.Sort();
        int index = Math.Clamp((int)(levels.Count * 0.9), 0, levels.Count - 1);
        return levels[index];
    }
}
