using NAudio.Wave;
using Tapybara.Core.Audio;
using Tapybara.Core.Calls;
using Xunit;

namespace Tapybara.Core.Tests;

public sealed class CallPlayerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tapybara-tests", Guid.NewGuid().ToString("N"));
    private readonly List<Output> _outputs = [];
    public CallPlayerTests() => Directory.CreateDirectory(_directory);
    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
    private CallPlayer Player() => new(() => { var output = new Output(); _outputs.Add(output); return output; });
    private void Wave(string name, int seconds, short sample = 1000)
    {
        using var writer = new WaveFileWriter(Path.Combine(_directory, name), new WaveFormat(16000, 16, 1));
        byte[] data = new byte[seconds * 16000 * 2];
        for (int i = 0; i < data.Length; i += 2) { data[i] = (byte)(sample & 255); data[i + 1] = (byte)(sample >> 8); }
        writer.Write(data, 0, data.Length);
    }

    private sealed class Output : IWavePlayer, IWavePosition
    {
        public IWaveProvider Source { get; private set; } = null!;
        public PlaybackState PlaybackState { get; private set; }
        public WaveFormat OutputWaveFormat => Source.WaveFormat;
        public float Volume { get; set; } = 1;
        public long BytesPlayed { get; set; }
        public bool Disposed { get; private set; }
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public void Init(IWaveProvider waveProvider) => Source = waveProvider;
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() => PlaybackState = PlaybackState.Stopped;
        public long GetPosition() => BytesPlayed;
        public void Advance(double seconds) => BytesPlayed = (long)(seconds * OutputWaveFormat.AverageBytesPerSecond);
        public EventHandler<StoppedEventArgs>? CaptureStopped() => PlaybackStopped;
        public void Complete()
        {
            PlaybackState = PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs());
        }
        public void Dispose() => Disposed = true;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RangeLockPreservesPlaybackStateAndUnlockRestoresWholeCall(bool playing)
    {
        Wave(CallSession.SystemFileName, 10);
        using CallPlayer player = Player();
        Assert.True(player.Load(_directory, TimeSpan.FromSeconds(3)));
        if (playing)
        {
            Assert.True(player.Resume());
        }

        Assert.True(player.SetRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), CallChannel.Theirs));
        Assert.Equal(playing, player.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(3), player.Position);
        Assert.Equal(TimeSpan.FromSeconds(2), player.RangeStart);
        Assert.Equal(TimeSpan.FromSeconds(5), player.RangeEnd);
        Assert.True(player.HasRange);
        Assert.True(player.Seek(player.Position, wholeCall: true));
        Assert.False(player.HasRange);
        Assert.Equal(playing, player.IsPlaying);
        Assert.Null(player.Channel);
    }

    [Fact]
    public void NaturalRangeCompletionCanRestartButExplicitStopIsDistinct()
    {
        Wave(CallSession.MicFileName, 10);
        using CallPlayer player = Player();
        Assert.True(player.Play(_directory, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        _outputs.Last().Complete();
        Assert.True(player.CompletedNaturally);
        Assert.True(player.Resume());
        Assert.False(player.CompletedNaturally);
        Assert.Equal(TimeSpan.FromSeconds(2), player.Position);
        Assert.Equal(TimeSpan.FromSeconds(4), player.RangeEnd);
        player.Stop();
        Assert.False(player.CompletedNaturally);
    }

    [Fact]
    public void PositionUsesDeviceClockInsteadOfDecoderReadAhead()
    {
        Wave(CallSession.MicFileName, 10);
        using CallPlayer player = Player();
        Assert.True(player.Play(_directory, TimeSpan.FromSeconds(2)));
        Output output = Assert.Single(_outputs);
        byte[] buffer = new byte[output.OutputWaveFormat.AverageBytesPerSecond * 3];
        Assert.Equal(buffer.Length, output.Source.Read(buffer, 0, buffer.Length));
        Assert.Equal(TimeSpan.FromSeconds(2), player.Position);
        output.Advance(0.5);
        Assert.Equal(TimeSpan.FromSeconds(2.5), player.Position);
    }

    [Fact]
    public void PauseAndResumePreserveOutputQueueAndExactPosition()
    {
        Wave(CallSession.MicFileName, 10);
        using CallPlayer player = Player();
        Assert.True(player.Play(_directory, TimeSpan.FromSeconds(1)));
        Output output = Assert.Single(_outputs);
        output.Advance(2.25);
        player.Pause();
        Assert.True(player.IsPaused);
        Assert.Equal(TimeSpan.FromSeconds(3.25), player.Position);
        output.Advance(7);
        Assert.Equal(TimeSpan.FromSeconds(3.25), player.Position);
        output.Advance(2.25);
        Assert.True(player.Resume());
        Assert.Same(output, Assert.Single(_outputs));
        Assert.Equal(TimeSpan.FromSeconds(3.25), player.Position);
    }

    [Fact]
    public void SeekReplacesBufferedAudioAndRejectsOldStoppedCallback()
    {
        Wave(CallSession.MicFileName, 10);
        using CallPlayer player = Player();
        Assert.True(player.Play(_directory, TimeSpan.Zero));
        Output previous = _outputs[0];
        EventHandler<StoppedEventArgs>? late = previous.CaptureStopped();
        Assert.True(player.Seek(TimeSpan.FromSeconds(6)));
        Assert.True(previous.Disposed);
        Assert.Equal(TimeSpan.FromSeconds(6), player.Position);
        late?.Invoke(previous, new StoppedEventArgs());
        Assert.True(player.IsPlaying);
        Assert.False(_outputs[1].Disposed);
    }

    [Fact]
    public void SeekingPastShorterSourceDoesNotReplayItsBeginning()
    {
        Wave(CallSession.MicFileName, 1, 16000);
        Wave(CallSession.SystemFileName, 5, 0);
        using CallPlayer player = Player();
        Assert.True(player.Play(_directory, TimeSpan.FromSeconds(3)));
        Output output = Assert.Single(_outputs);
        byte[] buffer = new byte[640];
        Assert.Equal(buffer.Length, output.Source.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, b => Assert.Equal(0, b));
        Assert.Equal(TimeSpan.FromSeconds(5), player.Duration);
    }

    [Fact]
    public void BoundedPlaybackEndsAtRangeAndWholeCallSeekClearsIt()
    {
        Wave(CallSession.SystemFileName, 10);
        using CallPlayer player = Player();
        Assert.True(player.Play(_directory, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), CallChannel.Theirs));
        Output output = Assert.Single(_outputs);
        byte[] buffer = new byte[output.OutputWaveFormat.AverageBytesPerSecond * 2];
        Assert.Equal(output.OutputWaveFormat.AverageBytesPerSecond, output.Source.Read(buffer, 0, buffer.Length));
        Assert.Equal(0, output.Source.Read(buffer, 0, buffer.Length));
        output.Complete();
        Assert.Equal(TimeSpan.FromSeconds(4), player.Position);
        Assert.True(player.HasRange);
        Assert.True(player.Seek(TimeSpan.FromSeconds(7), wholeCall: true));
        Assert.False(player.HasRange);
        Assert.Null(player.Channel);
        Assert.False(player.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(7), player.Position);
    }

    [Fact]
    public void PausedSeekAndEndpointDoNotStartAudio()
    {
        Wave(CallSession.MicFileName, 3);
        using CallPlayer player = Player();
        Assert.True(player.Load(_directory));
        Assert.Empty(_outputs);
        Assert.True(player.Seek(TimeSpan.FromHours(1)));
        Assert.Equal(TimeSpan.FromSeconds(3), player.Position);
        Assert.Empty(_outputs);
        Assert.True(player.Resume());
        Assert.Equal(TimeSpan.Zero, player.Position);
        Assert.Single(_outputs);
    }

    [Fact]
    public void InvalidRangeAndMissingChannelReleaseReaders()
    {
        Wave(CallSession.MicFileName, 3);
        using CallPlayer player = Player();
        Assert.False(player.Play(_directory, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
        Assert.False(player.IsLoaded);
        Assert.False(player.Play(_directory, TimeSpan.Zero, channel: CallChannel.Theirs));
        using FileStream exclusive = new(Path.Combine(_directory, CallSession.MicFileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Empty(_outputs);
    }

    [Fact]
    public void ActivityHasNoStaleSpeakerInSilenceAndIncludesOverlap()
    {
        CallLine a = new(CallChannel.Mine, TimeSpan.Zero, TimeSpan.FromSeconds(2), "a");
        CallLine b = new(CallChannel.Theirs, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "b");
        Assert.True(CallPlaybackActivity.IsActive(a, TimeSpan.FromSeconds(1.5)));
        Assert.True(CallPlaybackActivity.IsActive(b, TimeSpan.FromSeconds(1.5)));
        Assert.False(CallPlaybackActivity.IsActive(a, TimeSpan.FromSeconds(2)));
        Assert.False(CallPlaybackActivity.IsActive(b, TimeSpan.FromSeconds(4)));
        Assert.False(CallPlaybackActivity.IsActive(a, TimeSpan.FromSeconds(1.5), CallChannel.Theirs));
        Assert.False(CallPlaybackActivity.IsActive(a with { End = TimeSpan.Zero }, TimeSpan.Zero));
    }

    [Fact]
    public void SeekingToBoundedEndPreservesRangeForReplay()
    {
        Wave(CallSession.SystemFileName, 10);
        using CallPlayer player = Player();
        Assert.True(player.Play(_directory, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), CallChannel.Theirs));
        player.Pause();
        Assert.True(player.Seek(TimeSpan.FromSeconds(9)));
        Assert.Equal(TimeSpan.FromSeconds(4), player.Position);
        Assert.Equal(TimeSpan.FromSeconds(2), player.RangeStart);
        Assert.True(player.Resume());
        Assert.Equal(TimeSpan.FromSeconds(2), player.Position);
    }

    [Fact]
    public void PreparedPlaybackDoesNotBlockStaleHeaderRecovery()
    {
        Wave(CallSession.MicFileName, 4);
        string path = Path.Combine(_directory, CallSession.MicFileName);
        byte[] wav = File.ReadAllBytes(path);
        int data = -1;
        for (int i = 12; i < 100; i++)
        {
            if (wav.AsSpan(i, 4).SequenceEqual("data"u8)) { data = i; break; }
        }
        Assert.True(data >= 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(data + 4, 4), 16000 * 2);
        File.WriteAllBytes(path, wav);
        using CallPlayer player = Player();
        Assert.True(player.Load(_directory));
        Assert.Equal(TimeSpan.FromSeconds(1), player.Duration);
        Assert.True(CallRepair.RepairWav(path));
        Assert.True(player.Resume());
        Assert.Equal(TimeSpan.FromSeconds(4), player.Duration);
        player.Pause();
        Assert.True(player.Seek(TimeSpan.FromSeconds(2)));
        using FileStream exclusive = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(TimeSpan.FromSeconds(2), player.Position);
    }

    [Fact]
    public void ValidExcerptSourceDoesNotDependOnBrokenOtherChannel()
    {
        Wave(CallSession.SystemFileName, 3);
        File.WriteAllText(Path.Combine(_directory, CallSession.MicFileName), "invalid wav");
        using CallPlayer player = Player();
        Assert.True(player.Play(_directory, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), CallChannel.Theirs));
        Assert.True(player.IsPlaying);
        Assert.Equal(CallChannel.Theirs, player.Channel);
        Assert.Equal(TimeSpan.FromSeconds(1), player.Position);
    }
}
