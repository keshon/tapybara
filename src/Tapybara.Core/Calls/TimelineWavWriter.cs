using System.Diagnostics;
using NAudio.Wave;
using Tapybara.Core.Audio;
using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Calls;

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
/// Поэтому пропуски добиваются тишиной по опорным часам: перед записью порции
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

    /// <summary>
    /// Как часто переписывать заголовок файла.
    /// </summary>
    /// <remarks>
    /// Длины областей RIFF записываются при закрытии файла. Любой конец, кроме
    /// штатного — падение, выход из системы, диспетчер задач, <c>taskkill /f</c>
    /// из собственного скрипта сборки, — оставлял часовую запись с нулём в
    /// заголовке: проигрыватели её не открывают, и приложение тоже. Регулярное
    /// обновление заголовка означает, что в худшем случае теряются последние
    /// несколько секунд, а не весь разговор.
    /// </remarks>
    private static readonly TimeSpan HeaderRefresh = TimeSpan.FromSeconds(5);

    /// <summary>Предел разовой добивки — защита от испорченных опорных часов.</summary>
    private static readonly TimeSpan MaxSinglePad = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private readonly WaveFileWriter _writer;
    private readonly float[] _silence = new float[AudioCapture.TargetSampleRate / 10];
    private readonly Stopwatch _sinceHeaderRefresh = Stopwatch.StartNew();

    private float[] _scratch = [];
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

    /// <summary>Дописать порцию, выровняв её по опорным часам записи.</summary>
    /// <param name="samples">Сэмплы 16 кГц моно.</param>
    /// <param name="reference">Сколько должно было пройти по опорным часам.</param>
    public void Write(ReadOnlySpan<float> samples, TimeSpan reference)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            long expected = (long)(reference.TotalSeconds * AudioCapture.TargetSampleRate);
            long missing = expected - samples.Length - _written;
            long toleranceSamples = (long)(Tolerance.TotalSeconds * AudioCapture.TargetSampleRate);

            if (missing > toleranceSamples)
            {
                PadLocked(missing);
            }

            WriteClampedLocked(samples);
            _written += samples.Length;

            if (_sinceHeaderRefresh.Elapsed >= HeaderRefresh)
            {
                RefreshHeaderLocked();
            }
        }
    }

    /// <summary>Довести дорожку до заданной длительности.</summary>
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

    /// <summary>Переписать длины в заголовке, не закрывая файл.</summary>
    public void RefreshHeader()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                RefreshHeaderLocked();
            }
        }
    }

    private void RefreshHeaderLocked()
    {
        try
        {
            // Flush у WaveFileWriter не просто сбрасывает буфер: он переписывает
            // размеры областей RIFF и возвращает позицию обратно.
            _writer.Flush();
            _sinceHeaderRefresh.Restart();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            AppLog.Warn($"Не удалось обновить заголовок {System.IO.Path.GetFileName(Path)}.", ex);
        }
    }

    /// <summary>
    /// Записать порцию с ограничением амплитуды.
    /// </summary>
    /// <remarks>
    /// Клиппинг обязателен: float за пределами [-1, 1] при приведении к short
    /// переполняется и превращается в громкий щелчок. Пишем пачкой, а не по
    /// сэмплу: вызов на каждый из шестнадцати тысяч сэмплов в секунду делается
    /// в потоке WASAPI, где лишняя работа оборачивается пропусками в записи.
    /// </remarks>
    private void WriteClampedLocked(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        if (_scratch.Length < samples.Length)
        {
            _scratch = new float[samples.Length];
        }

        for (int i = 0; i < samples.Length; i++)
        {
            _scratch[i] = Math.Clamp(samples[i], -1f, 1f);
        }

        _writer.WriteSamples(_scratch, 0, samples.Length);
    }

    private void PadLocked(long samples)
    {
        long limit = (long)(MaxSinglePad.TotalSeconds * AudioCapture.TargetSampleRate);
        if (samples > limit)
        {
            AppLog.Warn($"Добивка {samples} сэмплов ограничена пределом — опорные часы разошлись.");
            samples = limit;
        }

        while (samples > 0)
        {
            int chunk = (int)Math.Min(samples, _silence.Length);
            _writer.WriteSamples(_silence, 0, chunk);
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
