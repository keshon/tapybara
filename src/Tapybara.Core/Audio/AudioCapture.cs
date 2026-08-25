using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Audio;

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

    /// <summary>
    /// Сколько накапливать без перевыделения массива.
    /// </summary>
    /// <remarks>
    /// Тридцать секунд, а не шестнадцать минут. Резерв «на всякий случай» под
    /// самую длинную возможную диктовку стоил 61 МБ в куче больших объектов
    /// НА КАЖДОЕ нажатие хоткея — включая двухсекундную диктовку, которых
    /// подавляющее большинство. Рост списка вдвое случается редко и стоит
    /// микросекунды.
    /// </remarks>
    private const int InitialCapacitySamples = TargetSampleRate * 30;

    // System.Threading.Lock (.NET 9+) вместо lock(object): тот же синтаксис
    // lock(...), но типобезопасно — по объекту-замку нельзя случайно вызвать
    // Monitor.Enter из чужого кода.
    private readonly Lock _gate = new();
    private readonly Lock _drainGate = new();
    private readonly List<float> _samples = [];

    private WasapiCapture? _capture;
    private MMDevice? _device;
    private BufferedWaveProvider? _deviceBuffer;
    private WdlResamplingSampleProvider? _pipeline;
    private float[] _scratch = [];
    private TaskCompletionSource? _stopped;
    private volatile bool _draining;

    /// <summary>Сколько сэмплов прошло через захват, даже если они не копятся в памяти.</summary>
    private long _written;

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

    /// <summary>
    /// Захват прекратился сам: устройство отключили, драйвер перезапустился.
    /// </summary>
    /// <remarks>
    /// Без этого события выдернутая посреди записи гарнитура выглядит как
    /// «человек молчал»: поток данных прекращается, ошибки нет, запись
    /// заканчивается пустотой, и узнаётся это уже по результату.
    /// </remarks>
    public event Action<Exception>? Failed;

    /// <summary>Идентификатор устройства WASAPI. <c>null</c> — устройство по умолчанию.</summary>
    public string? DeviceId { get; init; }

    /// <summary>Накапливать сэмплы в памяти. Для длинных записей выключается.</summary>
    public bool KeepInMemory { get; init; } = true;

    /// <summary>Сколько уже записано. Читается из любого потока.</summary>
    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
            {
                return TimeSpan.FromSeconds((double)_written / TargetSampleRate);
            }
        }
    }

    /// <summary>Открыть устройство WASAPI. Единственное отличие наследников.</summary>
    protected abstract WasapiCapture CreateDevice(MMDevice? device);

    /// <summary>С какой стороны искать устройство по идентификатору.</summary>
    protected abstract DataFlow Flow { get; }

    /// <summary>Начать захват.</summary>
    public void Start()
    {
        if (_capture is not null)
        {
            throw new InvalidOperationException("Захват уже идёт — сначала StopAsync().");
        }

        // Устройство держим в поле, а не в using: WasapiCapture обращается к
        // нему и после конструктора, при каждом старте записи. Освобождённый
        // COM-объект под ним превращается в отказ открыть устройство.
        _device = AudioDevices.Resolve(DeviceId, Flow);
        WasapiCapture capture = CreateDevice(_device);
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
            _written = 0;
            if (KeepInMemory)
            {
                _samples.Capacity = InitialCapacitySamples;
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
            _samples.Capacity = 0;
            return result;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            _deviceBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
            DrainPipeline();
        }
        catch (Exception ex)
        {
            // Исключение, выпущенное в поток WASAPI, убивает его молча:
            // запись просто перестаёт расти, и понять это можно только
            // по результату.
            AppLog.Error("Сбой в обработке порции звука.", ex);
            Failed?.Invoke(ex);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _stopped?.TrySetResult();

        if (e.Exception is { } error)
        {
            AppLog.Error("Устройство записи остановилось с ошибкой.", error);
            Failed?.Invoke(error);
        }
    }

    /// <summary>
    /// Вычитать из цепочки всё, что она готова отдать.
    /// </summary>
    /// <remarks>
    /// Вызывается и из аудио-потока, и из <see cref="StopAsync"/> — последний
    /// добирает хвост ресемплера. Ресемплер и рабочий массив не потокобезопасны,
    /// поэтому вход сюда сериализован; момент, когда таймаут ожидания
    /// остановки истёк, а устройство ещё шлёт данные, реален.
    /// </remarks>
    private void DrainPipeline()
    {
        lock (_drainGate)
        {
            if (_pipeline is null || _draining)
            {
                return;
            }

            _draining = true;
            try
            {
                int read;
                while ((read = _pipeline.Read(_scratch, 0, _scratch.Length)) > 0)
                {
                    var chunk = _scratch.AsSpan(0, read);

                    lock (_gate)
                    {
                        _written += read;
                        if (KeepInMemory)
                        {
                            _samples.AddRange(chunk);
                        }
                    }

                    Publish(chunk);
                }
            }
            finally
            {
                _draining = false;
            }
        }
    }

    /// <summary>
    /// Отдать порцию подписчикам, не дав им уронить поток захвата.
    /// </summary>
    /// <remarks>
    /// Обработчики пишут на диск и трогают интерфейс. Любое исключение оттуда
    /// прилетело бы в поток WASAPI и завершило запись без единого признака.
    /// </remarks>
    private void Publish(ReadOnlySpan<float> chunk)
    {
        if (SamplesAvailable is { } sink)
        {
            float[] copy = chunk.ToArray();
            try
            {
                sink(copy);
            }
            catch (Exception ex)
            {
                AppLog.Error("Обработчик SamplesAvailable бросил исключение.", ex);
                Failed?.Invoke(ex);
            }
        }

        if (LevelChanged is { } handler)
        {
            float level = Rms(chunk);
            try
            {
                handler(level);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Обработчик LevelChanged бросил исключение.", ex);
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

        _device?.Dispose();
        _device = null;

        lock (_drainGate)
        {
            _pipeline = null;
            _deviceBuffer = null;
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>Микрофон: устройство из настроек или, если его нет, по умолчанию.</summary>
public sealed class MicrophoneCapture : AudioCapture
{
    protected override DataFlow Flow => DataFlow.Capture;

    protected override WasapiCapture CreateDevice(MMDevice? device) =>
        device is null ? new WasapiCapture() : new WasapiCapture(device);
}

/// <summary>
/// Системный звук — то, что слышно из колонок: голоса собеседников в звонке.
/// </summary>
/// <remarks>
/// WASAPI loopback: захват того, что уходит на устройство вывода. На Windows
/// это штатная возможность в одну строку — в macOS-оригинале ради того же
/// понадобился process tap Core Audio и приватное агрегатное устройство.
/// <para>
/// Пишется ВЕСЬ звук выбранного устройства, включая уведомления и музыку.
/// Сузить до одного приложения можно через process loopback (Windows 10 2004+)
/// — это отдельная задача.
/// </para>
/// </remarks>
public sealed class SystemAudioCapture : AudioCapture
{
    protected override DataFlow Flow => DataFlow.Render;

    protected override WasapiCapture CreateDevice(MMDevice? device) =>
        device is null ? new WasapiLoopbackCapture() : new WasapiLoopbackCapture(device);
}
