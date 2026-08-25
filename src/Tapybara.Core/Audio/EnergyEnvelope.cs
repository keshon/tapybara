namespace Tapybara.Core.Audio;

/// <summary>
/// Огибающая громкости записи: по одному значению на короткое окно.
/// </summary>
/// <remarks>
/// <para>
/// Нужна, чтобы спрашивать «насколько громко было в такой-то момент», не держа
/// в памяти всю запись. Час разговора в двух каналах — это около 460 МБ
/// float-массивов, и держали мы их только ради того, чтобы посчитать
/// несколько десятков средних значений.
/// </para>
/// <para>
/// Огибающая того же часа занимает меньше мегабайта: одно число на двадцать
/// миллисекунд. Для сравнения уровней двух каналов этого достаточно с
/// избытком — речь не меняет громкость за миллисекунды.
/// </para>
/// </remarks>
public sealed class EnergyEnvelope
{
    /// <summary>Окно усреднения.</summary>
    private const int FrameSamples = AudioCapture.TargetSampleRate / 50; // 20 мс

    private readonly float[] _meanSquare;

    private EnergyEnvelope(float[] meanSquare, int sampleCount)
    {
        _meanSquare = meanSquare;
        SampleCount = sampleCount;
    }

    /// <summary>Сколько сэмплов было в исходной записи.</summary>
    public int SampleCount { get; }

    /// <summary>Длительность исходной записи.</summary>
    public TimeSpan Duration => TimeSpan.FromSeconds(SampleCount / (double)AudioCapture.TargetSampleRate);

    /// <summary>Пустая огибающая — для канала, которого не было.</summary>
    public static EnergyEnvelope Empty { get; } = new([], 0);

    /// <summary>Снять огибающую с записи.</summary>
    public static EnergyEnvelope Build(ReadOnlySpan<float> samples)
    {
        int frames = samples.Length / FrameSamples;
        float[] values = new float[frames];

        for (int frame = 0; frame < frames; frame++)
        {
            int start = frame * FrameSamples;
            double sum = 0;
            for (int i = start; i < start + FrameSamples; i++)
            {
                sum += (double)samples[i] * samples[i];
            }

            values[frame] = (float)(sum / FrameSamples);
        }

        return new EnergyEnvelope(values, samples.Length);
    }

    /// <summary>
    /// Средний уровень на отрезке, в децибелах.
    /// </summary>
    /// <returns>−100 дБ, если отрезок пуст или лежит за пределами записи.</returns>
    public double LevelDb(TimeSpan start, TimeSpan end)
    {
        if (_meanSquare.Length == 0)
        {
            return -100;
        }

        int from = Math.Clamp(FrameAt(start), 0, _meanSquare.Length);
        int to = Math.Clamp(FrameAt(end), from, _meanSquare.Length);

        if (to <= from)
        {
            // Отрезок короче окна усреднения — берём то окно, в которое он попал.
            if (from >= _meanSquare.Length)
            {
                return -100;
            }

            to = from + 1;
        }

        double sum = 0;
        for (int i = from; i < to; i++)
        {
            sum += _meanSquare[i];
        }

        return 20 * Math.Log10(Math.Sqrt(sum / (to - from)) + 1e-9);
    }

    private static int FrameAt(TimeSpan time) =>
        (int)(time.TotalSeconds * AudioCapture.TargetSampleRate / FrameSamples);
}
