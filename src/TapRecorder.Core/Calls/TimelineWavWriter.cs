using NAudio.Wave;
using TapRecorder.Core.Audio;

namespace TapRecorder.Core.Calls;

/// <summary>
/// Пишет канал звонка в WAV, удерживая его на общей временной шкале.
/// </summary>
/// <remarks>
/// <para>
/// Ключевая деталь всей записи звонков. WASAPI loopback НЕ отдаёт данные, пока
/// на устройстве вывода тишина: собеседник молчит — поток просто молчит тоже,
/// и в файл ничего не попадает. Микрофон при этом пишется непрерывно.
/// </para>
/// <para>
/// Если складывать оба канала «как пришло», они разъедутся: минута молчания
/// собеседника превратится в ноль секунд его дорожки, и вся дальнейшая
/// хронология поедет. Реплики в транскрипте перемешались бы.
/// </para>
/// <para>
/// Поэтому пропуски добиваются тишиной по часам: перед записью порции
/// сравниваем, сколько сэмплов уже лежит в файле, с тем, сколько их должно
/// было бы быть к этому моменту.
/// </para>
/// </remarks>
internal sealed class TimelineWavWriter : IDisposable
{
    /// <summary>
    /// Допуск, ниже которого пропуск не добивается.
    /// </summary>
    /// <remarks>
    /// Звук приходит пачками, и мгновенное отставание в десятки миллисекунд —
    /// норма, а не пропуск. Без допуска мы вставляли бы тишину на каждой пачке
    /// и растягивали запись.
    /// </remarks>
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(150);

    private readonly Lock _gate = new();
    private readonly WaveFileWriter _writer;
    private readonly float[] _silence = new float[AudioCapture.TargetSampleRate / 10];

    private long _written;
    private bool _disposed;

    public TimelineWavWriter(string path)
    {
        Path = path;
        _writer = new WaveFileWriter(path, new WaveFormat(AudioCapture.TargetSampleRate, 16, 1));
    }

    public string Path { get; }

    /// <summary>Сколько звука уже в файле.</summary>
    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
            {
                return TimeSpan.FromSeconds(_written / (double)AudioCapture.TargetSampleRate);
            }
        }
    }

    /// <summary>Дописать порцию, выровняв её по общим часам записи.</summary>
    /// <param name="samples">Сэмплы 16 кГц моно.</param>
    /// <param name="elapsed">Сколько прошло с начала записи по общим часам.</param>
    public void Write(ReadOnlySpan<float> samples, TimeSpan elapsed)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            long expected = (long)(elapsed.TotalSeconds * AudioCapture.TargetSampleRate);
            long missing = expected - samples.Length - _written;
            long toleranceSamples = (long)(Tolerance.TotalSeconds * AudioCapture.TargetSampleRate);

            if (missing > toleranceSamples)
            {
                PadLocked(missing);
            }

            foreach (float sample in samples)
            {
                // Клиппинг обязателен: float за пределами [-1, 1] при приведении
                // к short переполняется и превращается в громкий щелчок.
                _writer.WriteSample(Math.Clamp(sample, -1f, 1f));
            }

            _written += samples.Length;
        }
    }

    /// <summary>Довести дорожку до общей длительности записи.</summary>
    /// <remarks>
    /// Вызывается на остановке: если собеседник молчал последние полминуты,
    /// его дорожка иначе оказалась бы короче микрофонной, и длительности
    /// двух каналов в meta.json разошлись бы.
    /// </remarks>
    public void PadTo(TimeSpan total)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            long expected = (long)(total.TotalSeconds * AudioCapture.TargetSampleRate);
            PadLocked(expected - _written);
        }
    }

    private void PadLocked(long samples)
    {
        while (samples > 0)
        {
            int chunk = (int)Math.Min(samples, _silence.Length);
            for (int i = 0; i < chunk; i++)
            {
                _writer.WriteSample(0f);
            }

            _written += chunk;
            samples -= chunk;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }
}
