using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TapRecorder.Core.Audio;

/// <summary>
/// Захват звука в память в формате whisper: 16 кГц, моно, float32.
/// </summary>
/// <remarks>
/// Базовый класс для микрофона и системного звука: различаются они ровно одним
/// — каким объектом WASAPI открывается устройство. Всё остальное (сведение в
/// моно, ресемплинг, накопление, уровень) общее.
/// </remarks>
public abstract class AudioCapture : IDisposable
{
    /// <summary>Частота дискретизации, на которой работает whisper. Не настраивается.</summary>
    public const int TargetSampleRate = 16_000;

    // System.Threading.Lock (.NET 9+) вместо lock(object): тот же синтаксис
    // lock(...), но типобезопасно — по объекту-замку нельзя случайно вызвать
    // Monitor.Enter из чужого кода.
    private readonly Lock _gate = new();
    private readonly List<float> _samples = [];

    private WasapiCapture? _capture;
    private BufferedWaveProvider? _deviceBuffer;
    private WdlResamplingSampleProvider? _pipeline;
    private float[] _scratch = [];
    private TaskCompletionSource? _stopped;

    /// <summary>Уровень входа (RMS, 0..~1) на каждую порцию звука — для индикатора.</summary>
    public event Action<float>? LevelChanged;

    /// <summary>
    /// Свежая порция сэмплов в целевом формате.
    /// </summary>
    /// <remarks>
    /// Для записи звонков: позволяет писать на диск по ходу, а не копить
    /// часовой разговор в памяти. Вызывается из аудио-потока.
    /// </remarks>
    public event Action<float[]>? SamplesAvailable;

    /// <summary>Накапливать сэмплы в памяти. Для длинных записей выключается.</summary>
    public bool KeepInMemory { get; init; } = true;

    /// <summary>Сколько уже записано. Читается из любого потока.</summary>
    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
            {
                return TimeSpan.FromSeconds((double)_samples.Count / TargetSampleRate);
            }
        }
    }

    /// <summary>Открыть устройство WASAPI. Единственное отличие наследников.</summary>
    protected abstract WasapiCapture CreateDevice();

    /// <summary>Начать захват.</summary>
    public void Start()
    {
        if (_capture is not null)
        {
            throw new InvalidOperationException("Захват уже идёт — сначала StopAsync().");
        }

        WasapiCapture capture = CreateDevice();
        WaveFormat deviceFormat = capture.WaveFormat;

        // ReadFully = false — важно. По умолчанию BufferedWaveProvider добивает
        // недостающие байты тишиной, чтобы Read() всегда возвращал запрошенное.
        // Нам это сломало бы запись: в паузах между порциями от устройства в
        // массив лился бы поток нулей и запись «росла» бы быстрее реального времени.
        _deviceBuffer = new BufferedWaveProvider(deviceFormat)
        {
            ReadFully = false,
            BufferDuration = TimeSpan.FromSeconds(10),
            DiscardOnBufferOverflow = true,
        };

        _pipeline = new WdlResamplingSampleProvider(ToMono(_deviceBuffer.ToSampleProvider()), TargetSampleRate);
        _scratch = new float[TargetSampleRate]; // секунда запаса — Read вернёт сколько есть

        lock (_gate)
        {
            _samples.Clear();
            if (KeepInMemory)
            {
                // Резерв под ~16 минут: аудио-поток не должен упираться
                // в перевыделение массива посреди записи.
                _samples.Capacity = TargetSampleRate * 60 * 16;
            }
        }

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;
        _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _capture = capture;
        capture.StartRecording();
    }

    /// <summary>
    /// Остановить захват и отдать всё записанное.
    /// </summary>
    /// <remarks>
    /// Асинхронный намеренно: NAudio поднимает <c>RecordingStopped</c> в том
    /// контексте синхронизации, в котором создан объект. Если создать захват на
    /// UI-потоке и там же заблокироваться в ожидании этого события — получим
    /// вечный дедлок. <c>await</c> поток не держит, поэтому событие доезжает.
    /// </remarks>
    public async Task<float[]> StopAsync()
    {
        if (_capture is null)
        {
            return [];
        }

        _capture.StopRecording();
        if (_stopped is not null)
        {
            // Устройство отвалилось до события — не висим здесь навсегда.
            await Task.WhenAny(_stopped.Task, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        }

        DrainPipeline(); // хвост, который ресемплер держал у себя

        lock (_gate)
        {
            float[] result = [.. _samples];
            _samples.Clear();
            return result;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _deviceBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
        DrainPipeline();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) => _stopped?.TrySetResult();

    /// <summary>Вычитать из цепочки всё, что она готова отдать.</summary>
    private void DrainPipeline()
    {
        if (_pipeline is null)
        {
            return;
        }

        int read;
        while ((read = _pipeline.Read(_scratch, 0, _scratch.Length)) > 0)
        {
            var chunk = _scratch.AsSpan(0, read);

            if (KeepInMemory)
            {
                lock (_gate)
                {
                    _samples.AddRange(chunk);
                }
            }

            if (SamplesAvailable is { } sink)
            {
                sink(chunk.ToArray());
            }

            if (LevelChanged is { } handler)
            {
                handler(Rms(chunk));
            }
        }
    }

    /// <summary>Среднеквадратичный уровень порции — дешёвая мера «насколько громко».</summary>
    private static float Rms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0f;
        }

        double sum = 0;
        foreach (float s in samples)
        {
            sum += (double)s * s;
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }

    /// <summary>Свести любое число каналов устройства в моно.</summary>
    private static ISampleProvider ToMono(ISampleProvider source) => source.WaveFormat.Channels switch
    {
        1 => source,
        2 => new StereoToMonoSampleProvider(source) { LeftVolume = 0.5f, RightVolume = 0.5f },
        // Экзотика (интерфейсы, гарнитуры с матрицей каналов): берём первый канал.
        // Смешивать неизвестную раскладку рискованно — можно сложить голос с тишиной.
        _ => new MultiplexingSampleProvider([source], 1),
    };

    public void Dispose()
    {
        if (_capture is { } capture)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            capture.Dispose();
            _capture = null;
        }

        _pipeline = null;
        _deviceBuffer = null;
        GC.SuppressFinalize(this);
    }
}

/// <summary>Микрофон — устройство ввода по умолчанию.</summary>
public sealed class MicrophoneCapture : AudioCapture
{
    protected override WasapiCapture CreateDevice() => new WasapiCapture();
}

/// <summary>
/// Системный звук — то, что слышно из колонок: голоса собеседников в звонке.
/// </summary>
/// <remarks>
/// WASAPI loopback: захват того, что уходит на устройство вывода. На Windows
/// это штатная возможность в одну строку — в macOS-оригинале ради того же
/// понадобился process tap Core Audio и приватное агрегатное устройство.
/// <para>
/// Пишется ВЕСЬ звук системы, включая уведомления и музыку. Сузить до одного
/// приложения можно через process loopback (Windows 10 2004+) — это отдельная
/// задача.
/// </para>
/// </remarks>
public sealed class SystemAudioCapture : AudioCapture
{
    protected override WasapiCapture CreateDevice() => new WasapiLoopbackCapture();
}
