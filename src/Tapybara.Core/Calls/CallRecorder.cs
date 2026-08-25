using System.Diagnostics;
using Tapybara.Core.Audio;
using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Calls;

/// <summary>Настройки записи звонка.</summary>
/// <param name="CallsRoot">Корневая папка со звонками.</param>
/// <param name="MicrophoneDeviceId">Устройство записи. <c>null</c> — по умолчанию.</param>
/// <param name="SystemAudioDeviceId">Устройство вывода, с которого снимается чужой канал.</param>
/// <param name="MaxDuration">Предохранитель от забытой записи.</param>
/// <param name="Trigger">Приложение-инициатор, если запись автоматическая.</param>
public sealed record CallRecordingOptions(
    string CallsRoot,
    string? MicrophoneDeviceId = null,
    string? SystemAudioDeviceId = null,
    TimeSpan? MaxDuration = null,
    string? Trigger = null);

/// <summary>Почему запись прекратилась сама.</summary>
public enum CallStopReason
{
    /// <summary>Отвалилось устройство.</summary>
    DeviceLost,

    /// <summary>Сработал предохранитель по длительности.</summary>
    LengthLimitReached,

    /// <summary>Кончилось место на диске.</summary>
    DiskFull,
}

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
    /// <summary>Сколько места требуем свободным, чтобы вообще начать.</summary>
    private const long RequiredFreeBytes = 500L * 1024 * 1024;

    /// <summary>Ниже этого запись останавливается сама.</summary>
    private const long CriticalFreeBytes = 100L * 1024 * 1024;

    private readonly Lock _gate = new();

    private MicrophoneCapture? _mic;
    private SystemAudioCapture? _system;
    private TimelineWavWriter? _micWriter;
    private TimelineWavWriter? _systemWriter;
    private Stopwatch? _clock;
    private CallSession? _session;
    private Timer? _watchdog;
    private bool _disposed;

    /// <summary>Идёт ли запись прямо сейчас.</summary>
    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _clock is not null;
            }
        }
    }

    /// <summary>Сколько идёт текущая запись.</summary>
    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
            {
                return _clock?.Elapsed ?? TimeSpan.Zero;
            }
        }
    }

    /// <summary>Уровень микрофона 0..~1 — для индикатора.</summary>
    public event Action<float>? MicLevel;

    /// <summary>
    /// Запись остановилась сама.
    /// </summary>
    /// <remarks>
    /// Раньше это событие поднималось только при неудачном старте, а
    /// выдернутая посреди разговора гарнитура не поднимала ничего: запись
    /// продолжала писать тишину и рапортовала об успехе. Теперь сюда приходят
    /// все три реальные причины — устройство, время, диск.
    /// </remarks>
    public event Action<CallStopReason, string?>? Stopped;

    /// <summary>Начать запись в новую папку.</summary>
    public CallSession Start(CallRecordingOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsRecording)
        {
            throw new InvalidOperationException("Запись звонка уже идёт.");
        }

        Directory.CreateDirectory(options.CallsRoot);
        EnsureDiskSpace(options.CallsRoot);

        DateTimeOffset startedAt = DateTimeOffset.Now;
        string directory = Path.Combine(options.CallsRoot, CallSession.BuildFolderName(startedAt, options.Trigger));
        Directory.CreateDirectory(directory);

        var session = new CallSession
        {
            Directory = directory,
            StartedAt = startedAt,
            Trigger = options.Trigger,
        };

        CallMeta.Save(session);

        TimelineWavWriter? micWriter = null;
        TimelineWavWriter? systemWriter = null;
        MicrophoneCapture? mic = null;
        SystemAudioCapture? system = null;

        try
        {
            micWriter = new TimelineWavWriter(session.MicPath);
            systemWriter = new TimelineWavWriter(session.SystemPath);

            var clock = Stopwatch.StartNew();

            mic = new MicrophoneCapture
            {
                KeepInMemory = false,
                DeviceId = options.MicrophoneDeviceId,
            };

            system = new SystemAudioCapture
            {
                KeepInMemory = false,
                DeviceId = options.SystemAudioDeviceId,
            };

            // Опорные часы для микрофона — секундомер: канал непрерывный, и
            // сверять его больше не с чем.
            TimelineWavWriter capturedMicWriter = micWriter;
            TimelineWavWriter capturedSystemWriter = systemWriter;

            mic.SamplesAvailable += chunk => capturedMicWriter.Write(chunk, clock.Elapsed);
            mic.LevelChanged += level => MicLevel?.Invoke(level);
            mic.Failed += error => OnDeviceFailed(error);

            // А для системного канала опорные часы — уже записанный микрофон,
            // а НЕ секундомер. Часы звуковой карты и системные часы идут с
            // разной скоростью, и сверка каждого канала со своим источником
            // разводила дорожки: одна оказывалась длиннее другой ровно на
            // накопленное расхождение. Сверка с микрофоном делает расхождение
            // общим для обоих каналов, а значит невидимым.
            system.SamplesAvailable += chunk => capturedSystemWriter.Write(chunk, capturedMicWriter.Duration);
            system.Failed += error => OnDeviceFailed(error);

            lock (_gate)
            {
                _session = session;
                _micWriter = micWriter;
                _systemWriter = systemWriter;
                _clock = clock;
                _mic = mic;
                _system = system;
            }

            mic.Start();
            system.Start();

            StartWatchdog(options);
            AppLog.Info($"Начата запись звонка: {Path.GetFileName(directory)}");
            return session;
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось начать запись звонка.", ex);

            // Прибираем ЗДЕСЬ и синхронно. Прежний вариант запускал уборку
            // фоновой задачей и тут же бросал исключение — вызывающий код
            // получал и событие, и исключение, а недописанные файлы в это
            // время ещё держались открытыми.
            lock (_gate)
            {
                _session = null;
                _micWriter = null;
                _systemWriter = null;
                _clock = null;
                _mic = null;
                _system = null;
            }

            mic?.Dispose();
            system?.Dispose();
            micWriter?.Dispose();
            systemWriter?.Dispose();
            TryDeleteEmpty(directory);
            throw;
        }
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
        Timer? watchdog;

        lock (_gate)
        {
            (mic, system, micWriter, systemWriter, clock, session, watchdog) =
                (_mic, _system, _micWriter, _systemWriter, _clock, _session, _watchdog);

            _mic = null;
            _system = null;
            _micWriter = null;
            _systemWriter = null;
            _clock = null;
            _session = null;
            _watchdog = null;
        }

        watchdog?.Dispose();

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

        // Микрофон доводим до секундомера, а системный канал — до микрофона.
        // Так обе дорожки заведомо одной длины, что бы ни делали часы карты.
        micWriter?.PadTo(clock.Elapsed);

        TimeSpan total = micWriter?.Duration ?? clock.Elapsed;
        systemWriter?.PadTo(total);

        micWriter?.Dispose();
        systemWriter?.Dispose();

        CallSession finished = session with { Duration = total };
        CallMeta.Save(finished);
        AppLog.Info($"Запись звонка закончена: {total.TotalMinutes:F1} мин.");
        return finished;
    }

    /// <summary>
    /// Сторож: длительность и место на диске.
    /// </summary>
    /// <remarks>
    /// Два канала стоят около 230 МБ в час. Забытая на ночь запись забивала
    /// диск, а узнавал об этом пользователь по отказу записать следующий
    /// разговор.
    /// </remarks>
    private void StartWatchdog(CallRecordingOptions options)
    {
        var period = TimeSpan.FromSeconds(30);
        _watchdog = new Timer(
            _ =>
            {
                try
                {
                    if (!IsRecording)
                    {
                        return;
                    }

                    if (options.MaxDuration is { } limit && Elapsed >= limit)
                    {
                        StopBecause(CallStopReason.LengthLimitReached, null);
                        return;
                    }

                    if (FreeBytes(options.CallsRoot) is { } free && free < CriticalFreeBytes)
                    {
                        StopBecause(CallStopReason.DiskFull, null);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Сбой сторожа записи звонка.", ex);
                }
            },
            state: null,
            period,
            period);
    }

    private void OnDeviceFailed(Exception error) => StopBecause(CallStopReason.DeviceLost, error.Message);

    private void StopBecause(CallStopReason reason, string? detail)
    {
        if (!IsRecording)
        {
            return;
        }

        AppLog.Warn($"Запись звонка остановлена сама: {reason}. {detail}");

        _ = Task.Run(async () =>
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Error("Сбой при аварийной остановке записи.", ex);
            }
            finally
            {
                Stopped?.Invoke(reason, detail);
            }
        });
    }

    private static void EnsureDiskSpace(string path)
    {
        if (FreeBytes(path) is { } free && free < RequiredFreeBytes)
        {
            throw new IOException(
                $"На диске свободно {free / 1024 / 1024} МБ — этого мало для записи разговора.");
        }
    }

    private static long? FreeBytes(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            return root is null ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDeleteEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).All(IsDisposable))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Не удалось убрать папку несостоявшегося звонка.", ex);
        }

        // Пустые заголовки WAV и мета — единственное, что успело появиться.
        static bool IsDisposable(string entry) =>
            new FileInfo(entry).Length <= 64 || Path.GetFileName(entry) == CallSession.MetaFileName;
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
