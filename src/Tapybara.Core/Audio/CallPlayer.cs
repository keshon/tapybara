using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Tapybara.Core.Calls;
using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Audio;

/// <summary>Плеер источников звонка; позиция по часам вывода, пауза сохраняет очередь устройства.</summary>
/// <remarks>Методы и события принадлежат одному потоку; WPF создаёт вывод в своём контексте синхронизации.</remarks>
public sealed class CallPlayer : IDisposable
{
    private readonly Func<IWavePlayer> _outputFactory;
    private IWavePlayer? _output;
    private ISampleProvider? _source;
    private readonly List<AudioFileReader> _readers = [];
    private TimeSpan _from;
    private TimeSpan _position;
    private TimeSpan _end;
    private bool _disposed;

    public CallPlayer() : this(() => new WaveOutEvent { DesiredLatency = 150 }) { }
    internal CallPlayer(Func<IWavePlayer> outputFactory) => _outputFactory = outputFactory;

    public event Action? Stopped;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public bool IsPaused => _output?.PlaybackState == PlaybackState.Paused;
    public bool IsLoaded => CallDirectory is not null && Duration > TimeSpan.Zero;
    public string? CallDirectory { get; private set; }
    public CallChannel? Channel { get; private set; }
    public TimeSpan StartedFrom => _from;
    public TimeSpan Duration { get; private set; }
    public TimeSpan RangeStart { get; private set; }
    public TimeSpan RangeEnd => _end;
    public bool HasRange { get; private set; }
    public bool LastPlaybackFailed { get; private set; }
    public bool CompletedNaturally { get; private set; }

    /// <summary>Не позиция декодера: учитываем байты, которые устройство уже воспроизвело.</summary>
    public TimeSpan Position
    {
        get
        {
            if (IsPlaying && _output is IWavePosition clock)
            {
                try
                {
                    _position = TimeSpan.FromSeconds(Math.Clamp(_from.TotalSeconds
                        + clock.GetPosition() / (double)clock.OutputWaveFormat.AverageBytesPerSecond,
                        _from.TotalSeconds, _end.TotalSeconds));
                }
                catch (NAudio.MmException ex) { AppLog.Warn("Не удалось прочитать часы воспроизведения.", ex); }
            }
            return _position;
        }
    }

    public bool Load(string directory, TimeSpan from = default) => Open(directory, from, null, null, play: false);
    public bool Play(string callDirectory, TimeSpan from, TimeSpan? to = null, CallChannel? channel = null) =>
        Open(callDirectory, from, to, channel, play: true);

    /// <summary>Limit playback without changing whether it is paused or playing.</summary>
    public bool SetRange(TimeSpan from, TimeSpan to, CallChannel? channel = null)
    {
        if (CallDirectory is null || from < TimeSpan.Zero || to <= from)
        {
            return false;
        }

        TimeSpan position = Position;
        bool playing = IsPlaying;
        string directory = CallDirectory;
        var begin = position >= from && position < to ? position : from;
        bool result = Open(directory, begin, to, channel, play: playing);
        if (result)
        {
            RangeStart = from;
        }

        return result;
    }

    private bool Open(string directory, TimeSpan from, TimeSpan? to, CallChannel? channel, bool play)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CompletedNaturally = false;
        ReleaseResources();
        ClearContext();
        try
        {
            var selected = new List<AudioFileReader>();
            foreach ((string name, CallChannel kind) in new[] { (CallSession.MicFileName, CallChannel.Mine), (CallSession.SystemFileName, CallChannel.Theirs) })
            {
                string path = Path.Combine(directory, name);
                if (!File.Exists(path)) { continue; }
                if (channel is not null && channel != kind)
                {
                    // Длительность общей шкалы полезна, но поломка чужого канала не блокирует выбранный источник.
                    try
                    {
                        using var other = new WaveFileReader(path);
                        if (other.TotalTime > Duration) { Duration = other.TotalTime; }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or FormatException)
                    {
                        AppLog.Warn($"Не удалось прочитать длительность {path}.", ex);
                    }
                    continue;
                }
                var reader = new AudioFileReader(path);
                _readers.Add(reader);
                if (reader.TotalTime > Duration) { Duration = reader.TotalTime; }
                if (channel is null || channel == kind) { selected.Add(reader); }
            }
            if (selected.Count == 0 || selected.All(r => r.TotalTime <= TimeSpan.Zero)) { ReleaseResources(); ClearContext(); return false; }
            TimeSpan sourceEnd = selected.Max(r => r.TotalTime);
            TimeSpan begin = from < TimeSpan.Zero ? TimeSpan.Zero : from > sourceEnd ? sourceEnd : from;
            TimeSpan end = to is { } requested && requested < sourceEnd ? requested : sourceEnd;
            if (end < begin || (play && end == begin)) { ReleaseResources(); ClearContext(); return false; }
            foreach (AudioFileReader reader in selected)
            {
                // Короткая дорожка закончилась: оставляем её на EOF, а не запускаем с нуля.
                reader.CurrentTime = begin < reader.TotalTime ? begin : reader.TotalTime;
            }
            _source = selected.Count == 1 ? selected[0] : new MixingSampleProvider(selected);
            if (to is not null) { _source = new OffsetSampleProvider(_source) { Take = end - begin }; }
            CallDirectory = directory;
            Channel = channel;
            _from = _position = begin;
            _end = end;
            HasRange = to is not null;
            RangeStart = HasRange ? begin : TimeSpan.Zero;
            if (!play)
            {
                // Подготовленная позиция не удерживает файлы: починка заголовков должна оставаться возможной.
                ReleaseResources();
                return true;
            }
            return Resume();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or FormatException or NAudio.MmException)
        {
            AppLog.Warn($"Не удалось открыть звук {directory}.", ex);
            ReleaseResources();
            ClearContext();
            return false;
        }
    }

    public void Pause()
    {
        if (!IsPlaying) { return; }
        _position = Position;
        try
        {
            _output!.Pause();
            // Часы читаем после паузы: граница соответствует остановленному устройству.
            if (_output is IWavePosition clock)
            {
                _position = TimeSpan.FromSeconds(Math.Clamp(_from.TotalSeconds
                    + clock.GetPosition() / (double)clock.OutputWaveFormat.AverageBytesPerSecond, _from.TotalSeconds, _end.TotalSeconds));
            }
        }
        catch (NAudio.MmException ex)
        {
            LastPlaybackFailed = true;
            AppLog.Warn("Не удалось приостановить вывод звука.", ex);
            ReleaseResources();
            Stopped?.Invoke();
        }
    }

    public bool Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CompletedNaturally = false;
        if (!IsLoaded) { return false; }
        if (_position >= _end)
        {
            return Open(CallDirectory!, RangeStart, HasRange ? _end : null, Channel, play: true);
        }
        try
        {
            if (_output is null)
            {
                if (_source is null)
                {
                    TimeSpan originalStart = RangeStart;
                    bool resumed = Open(CallDirectory!, _position, HasRange ? _end : null, Channel, play: true);
                    if (resumed) { RangeStart = originalStart; }
                    return resumed;
                }
                _output = _outputFactory();
                if (_output is not IWavePosition) { throw new InvalidOperationException("Playback output requires a device clock."); }
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Init(_source);
            }
            _output.Play();
            CompletedNaturally = false;
            LastPlaybackFailed = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or NAudio.MmException)
        {
            LastPlaybackFailed = true;
            AppLog.Warn("Не удалось запустить вывод звука.", ex);
            ReleaseResources();
            return false;
        }
    }

    /// <summary>Пересоздание очереди исключает старые буферизованные сэмплы после перемещения.</summary>
    public bool Seek(TimeSpan position, bool wholeCall = false)
    {
        if (!IsLoaded) { return false; }
        bool playing = IsPlaying;
        string directory = CallDirectory!;
        TimeSpan start = wholeCall ? TimeSpan.Zero : RangeStart;
        TimeSpan end = wholeCall ? Duration : _end;
        TimeSpan target = position < start ? start : position > end ? end : position;
        bool result = Open(directory, target, wholeCall || !HasRange ? null : end,
            wholeCall ? null : Channel, play: playing && target < end);
        if (result && !wholeCall) { RangeStart = start; }
        return result;
    }

    public void Stop()
    {
        CompletedNaturally = false;
        LastPlaybackFailed = false;
        _position = RangeStart;
        ReleaseResources();
        Stopped?.Invoke();
    }

    public void Clear()
    {
        ReleaseResources();
        ClearContext();
        Stopped?.Invoke();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, _output)) { return; }
        CompletedNaturally = e.Exception is null;
        LastPlaybackFailed = e.Exception is not null;
        if (e.Exception is null) { _position = _end; }
        else { _position = Position; AppLog.Warn("Проигрывание звонка оборвалось.", e.Exception); }
        ReleaseResources();
        Stopped?.Invoke();
    }

    private void ReleaseResources()
    {
        if (_output is { } output)
        {
            _output = null;
            output.PlaybackStopped -= OnPlaybackStopped;
            try { output.Stop(); }
            catch (NAudio.MmException ex) { AppLog.Warn("Не удалось остановить устройство вывода.", ex); }
            finally { output.Dispose(); }
        }
        foreach (AudioFileReader reader in _readers) { reader.Dispose(); }
        _readers.Clear();
        _source = null;
    }

    private void ClearContext()
    {
        CallDirectory = null;
        Channel = null;
        Duration = _from = _position = _end = RangeStart = TimeSpan.Zero;
        HasRange = false;
        CompletedNaturally = false;
        LastPlaybackFailed = false;
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        ReleaseResources();
        ClearContext();
    }
}
