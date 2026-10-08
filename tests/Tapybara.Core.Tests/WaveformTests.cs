using NAudio.Wave;
using Tapybara.Core.Audio;
using Tapybara.Core.Calls;
using Xunit;

namespace Tapybara.Core.Tests;

public sealed class WaveformTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tapybara-waveform-" + Guid.NewGuid());
    public WaveformTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);
    private string Write(string name, float[] samples, int channels = 1, int rate = 1000)
    {
        string path = Path.Combine(_directory, name);
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(rate, channels));
        writer.WriteSamples(samples, 0, samples.Length);
        return path;
    }

    [Fact]
    public void PeaksRetainSilenceAndPartialFinalBinOnRealTimeScale()
    {
        string path = Write("mono.wav", [0, 0, .5f, -.8f, 0, 0, .25f]);
        WaveformEnvelope wave = WaveformEnvelope.Read(path, maximumBins: 3);
        Assert.Equal(new float[] { .5f, .8f, .25f }, wave.Peaks);
        Assert.Equal(TimeSpan.FromMilliseconds(7), wave.Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(3), wave.BinDuration);
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void OppositePolarityStereoDoesNotDisappearAndNonFiniteSamplesAreIgnored()
    {
        string path = Write("stereo.wav", [.7f, -.7f, float.NaN, float.PositiveInfinity, -2, 0], channels: 2);
        WaveformEnvelope wave = WaveformEnvelope.Read(path, 3);
        Assert.Equal(new float[] { .7f, 0, 1 }, wave.Peaks);
        Assert.Equal(TimeSpan.FromMilliseconds(3), wave.Duration);
    }

    [Fact]
    public void LongSourceHasBoundedPeaksAndRetainsLastFrame()
    {
        var samples = new float[100003];
        samples[^1] = .9f;
        WaveformEnvelope wave = WaveformEnvelope.Read(Write("long.wav", samples), 64);
        Assert.InRange(wave.Peaks.Count, 1, 64);
        Assert.Equal(.9f, wave.Peaks[^1]);
        Assert.Equal(TimeSpan.FromSeconds(100.003), wave.Duration);
    }

    [Fact]
    public void InvalidLimitAndCancellationDoNotOpenSource()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WaveformEnvelope.Read("missing.wav", 0));
        Assert.Throws<OperationCanceledException>(() => WaveformEnvelope.Read("missing.wav", cancellationToken: new CancellationToken(true)));
        Assert.Same(WaveformEnvelope.Empty, WaveformEnvelope.Read(Write("empty.wav", [])));
    }

    [Fact]
    public async Task CacheReusesUnchangedSourceAndInvalidatesChangedOrDeletedFiles()
    {
        string path = Write("cached.wav", [.2f]);
        var cache = new WaveformCache();
        WaveformEnvelope first = await cache.ReadAsync(path);
        Assert.Same(first, await cache.ReadAsync(path));
        Write("cached.wav", [.3f, .6f]);
        WaveformEnvelope next = await cache.ReadAsync(path);
        Assert.NotSame(first, next);
        Assert.Equal(.6f, next.Peaks[^1]);
        File.Delete(path);
        Assert.Same(WaveformEnvelope.Empty, await cache.ReadAsync(path));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.ReadAsync(path, new CancellationToken(true)));
    }

    [Fact]
    public async Task CacheEvictsOldSourcesAtCapacity()
    {
        var cache = new WaveformCache(1);
        string a = Write("a.wav", [.1f]), b = Write("b.wav", [.2f]);
        WaveformEnvelope first = await cache.ReadAsync(a);
        await cache.ReadAsync(b);
        Assert.NotSame(first, await cache.ReadAsync(a));
    }

    [Fact]
    public async Task RepairWaitsForWaveformAccessAndRestoresUndeclaredTail()
    {
        string path = Write("repair.wav", [.1f, .2f]);
        using (var append = new FileStream(path, FileMode.Append)) { append.Write(new byte[400]); }
        using var started = new ManualResetEventSlim();
        Task<bool> repair;
        using (AudioSourceAccess.Acquire(path))
        {
            repair = Task.Run(() => { started.Set(); return CallRepair.RepairWav(path); });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotSame(repair, await Task.WhenAny(repair, Task.Delay(100)));
        }
        Assert.True(await repair.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(102, WaveformEnvelope.Read(path).Duration.TotalMilliseconds, precision: 3);
    }

    [Fact]
    public async Task CanceledWaitDoesNotLeakSourceGateOrBlockFutureRead()
    {
        string path = Write("cancel.wav", [.4f]);
        using var cancellation = new CancellationTokenSource();
        using var started = new ManualResetEventSlim();
        using (AudioSourceAccess.Acquire(path))
        {
            Task<WaveformEnvelope> read = Task.Run(() =>
            {
                started.Set();
                return WaveformEnvelope.Read(path, cancellationToken: cancellation.Token);
            });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotSame(read, await Task.WhenAny(read, Task.Delay(100)));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        }
        Assert.Equal(.4f, WaveformEnvelope.Read(path).Peaks[0]);
    }
}
