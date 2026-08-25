using System.Diagnostics;
using TapRecorder.Core.Audio;

namespace TapRecorder.Core.Calls;

/// <summary>
/// Запись звонка в два раздельных канала: микрофон и системный звук.
/// </summary>
/// <remarks>
/// <para>
/// Разделение каналов — не удобство, а замена диаризации. «Я» и «собеседники»
/// оказываются физически в разных дорожках, и для разговора один на один
/// определять говорящего по голосу вообще не требуется. Диаризация нужна лишь
/// затем, чтобы разделить между собой нескольких собеседников.
/// </para>
/// <para>
/// Пишется сразу на диск, а не в память: часовой разговор в двух каналах —
/// это сотни мегабайт, и держать их в куче незачем.
/// </para>
/// </remarks>
public sealed class CallRecorder : IDisposable
{
    private readonly Lock _gate = new();

    private MicrophoneCapture? _mic;
    private SystemAudioCapture? _system;
    private TimelineWavWriter? _micWriter;
    private TimelineWavWriter? _systemWriter;
    private Stopwatch? _clock;
    private CallSession? _session;
    private bool _disposed;

    /// <summary>Идёт ли запись прямо сейчас.</summary>
    public bool IsRecording => _clock is not null;

    /// <summary>Сколько идёт текущая запись.</summary>
    public TimeSpan Elapsed => _clock?.Elapsed ?? TimeSpan.Zero;

    /// <summary>Уровень микрофона 0..~1 — для индикатора.</summary>
    public event Action<float>? MicLevel;

    /// <summary>Запись остановилась сама из-за ошибки устройства.</summary>
    public event Action<string>? Failed;

    /// <summary>Начать запись в новую папку внутри <paramref name="callsRoot"/>.</summary>
    /// <param name="callsRoot">Корневая папка со звонками.</param>
    /// <param name="trigger">Приложение-инициатор, если запись автоматическая.</param>
    public CallSession Start(string callsRoot, string? trigger = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRecording)
        {
            throw new InvalidOperationException("Запись звонка уже идёт.");
        }

        DateTimeOffset startedAt = DateTimeOffset.Now;
        string directory = Path.Combine(callsRoot, CallSession.BuildFolderName(startedAt, trigger));
        Directory.CreateDirectory(directory);

        var session = new CallSession
        {
            Directory = directory,
            StartedAt = startedAt,
            Trigger = trigger,
        };

        CallMeta.Save(session);

        var micWriter = new TimelineWavWriter(session.MicPath);
        var systemWriter = new TimelineWavWriter(session.SystemPath);

        // Часы одни на оба канала: именно по ним дорожки удерживаются
        // на общей временной шкале.
        var clock = Stopwatch.StartNew();

        var mic = new MicrophoneCapture { KeepInMemory = false };
        var system = new SystemAudioCapture { KeepInMemory = false };

        mic.SamplesAvailable += chunk => micWriter.Write(chunk, clock.Elapsed);
        mic.LevelChanged += level => MicLevel?.Invoke(level);
        system.SamplesAvailable += chunk => systemWriter.Write(chunk, clock.Elapsed);

        lock (_gate)
        {
            _session = session;
            _micWriter = micWriter;
            _systemWriter = systemWriter;
            _clock = clock;
            _mic = mic;
            _system = system;
        }

        try
        {
            mic.Start();
            system.Start();
        }
        catch (Exception ex)
        {
            // Устройство не открылось — не оставляем половину запущенной записи.
            _ = StopAsync();
            Failed?.Invoke(ex.Message);
            throw;
        }

        return session;
    }

    /// <summary>Остановить запись и вернуть завершённый сеанс.</summary>
    public async Task<CallSession?> StopAsync()
    {
        MicrophoneCapture? mic;
        SystemAudioCapture? system;
        TimelineWavWriter? micWriter;
        TimelineWavWriter? systemWriter;
        Stopwatch? clock;
        CallSession? session;

        lock (_gate)
        {
            (mic, system, micWriter, systemWriter, clock, session) =
                (_mic, _system, _micWriter, _systemWriter, _clock, _session);

            _mic = null;
            _system = null;
            _micWriter = null;
            _systemWriter = null;
            _clock = null;
            _session = null;
        }

        if (clock is null || session is null)
        {
            return null;
        }

        clock.Stop();

        if (mic is not null)
        {
            await mic.StopAsync().ConfigureAwait(false);
            mic.Dispose();
        }

        if (system is not null)
        {
            await system.StopAsync().ConfigureAwait(false);
            system.Dispose();
        }

        // Довести обе дорожки до общей длительности: собеседник мог молчать
        // в самом конце, и без этого его канал оказался бы короче.
        TimeSpan total = clock.Elapsed;
        micWriter?.PadTo(total);
        systemWriter?.PadTo(total);
        micWriter?.Dispose();
        systemWriter?.Dispose();

        CallSession finished = session with { Duration = total };
        CallMeta.Save(finished);
        return finished;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Выход посреди записи не должен терять записанное: дожидаемся
        // закрытия файлов, но не бесконечно.
        StopAsync().Wait(TimeSpan.FromSeconds(5));
    }
}
