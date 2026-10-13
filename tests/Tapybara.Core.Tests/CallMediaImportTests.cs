using NAudio.Wave;
using Tapybara.Core.Audio;
using Tapybara.Core.Calls;
using Tapybara.Core.Settings;
using Tapybara.Core.Speech;
using Xunit;

namespace Tapybara.Core.Tests;

public sealed class CallMediaImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tapybara-import-" + Guid.NewGuid().ToString("N"));
    public CallMediaImportTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ImportedConfirmedOwnVoiceHasPlayableSamplesAndSpeechShare()
    {
        var session = new CallSession { Directory = _root, StartedAt = DateTimeOffset.UtcNow, ImportedMedia = true,
            VoiceNames = new Dictionary<string, string> { ["A"] = CallSession.Me } };
        var own = new CallLine(CallChannel.Theirs, TimeSpan.Zero, TimeSpan.FromSeconds(6), "my speech", "A")
            { HumanEdits = CallLineEdits.Speaker };
        var doc = new CallTranscript { Lines = [own,
            new(CallChannel.Theirs, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(16), "other", "B")] };
        var me = Assert.Single(CallPeople.Of(session, doc), p => p.IsMe);
        Assert.Contains(own, me.Quotes);
        Assert.Equal(.5, me.Share);
    }

    [Fact]
    public async Task ImportOwnsAudioAndPersistsSingleTrackIdentityWithoutChangingOriginal()
    {
        string source = Path.Combine(_root, "original.wav");
        AudioFile.WriteWav16k(source, new float[32000]);
        byte[] before = File.ReadAllBytes(source);
        string calls = Path.Combine(_root, "calls");
        var session = await CallMediaImport.ImportAsync(source, calls, language: "ru", speakerCount: 5);
        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.True(CallMeta.Load(session.Directory)!.ImportedMedia);
        Assert.Equal("ru", CallMeta.Load(session.Directory)!.ImportLanguage);
        Assert.Equal(5, CallMeta.Load(session.Directory)!.SpeakerCountHint);
        Assert.Equal("original", session.Title);
        Assert.Equal(TimeSpan.FromSeconds(2), session.Duration);
        Assert.False(File.Exists(session.MicPath));
        File.Delete(source);
        Assert.Equal(CallState.NotTranscribed, Assert.Single(CallLibrary.Scan(calls)).State);
        using var audio = new WaveFileReader(session.SystemPath);
        Assert.Equal(16000, audio.WaveFormat.SampleRate);
        Assert.Equal(1, audio.WaveFormat.Channels);
        Assert.DoesNotContain(Directory.GetDirectories(calls), d => Path.GetFileName(d).StartsWith(".import-", StringComparison.Ordinal));
        var doc = new CallTranscript { Lines = [new(CallChannel.Theirs, TimeSpan.Zero, TimeSpan.FromSeconds(1), "speech", "A")] };
        Assert.DoesNotContain(CallPeople.Of(session, doc), p => p.IsMe);
    }

    [Fact]
    public async Task CancelledImportDoesNotLeaveSessionOrTouchSource()
    {
        string source = Path.Combine(_root, "original.wav");
        AudioFile.WriteWav16k(source, new float[16000]);
        byte[] before = File.ReadAllBytes(source);
        string calls = Path.Combine(_root, "calls");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CallMediaImport.ImportAsync(source, calls, cancellationToken: cancellation.Token));
        Assert.Empty(Directory.GetDirectories(calls));
        Assert.Equal(before, File.ReadAllBytes(source));
    }

    [Fact]
    public async Task EmptyAudioIsRejectedWithoutPublishingBrokenSession()
    {
        string source = Path.Combine(_root, "empty.wav");
        AudioFile.WriteWav16k(source, []);
        string calls = Path.Combine(_root, "calls");
        await Assert.ThrowsAsync<IOException>(() => CallMediaImport.ImportAsync(source, calls));
        Assert.Empty(Directory.GetDirectories(calls));
    }
    [Fact]
    public async Task MultichannelMediaRetainsSpeechFromCenterChannel()
    {
        string source = Path.Combine(_root, "surround.wav");
        using (var writer = new WaveFileWriter(source, new WaveFormat(16000, 16, 6)))
        {
            var samples = new float[16000 * 6];
            for (int frame = 0; frame < 16000; frame++) samples[frame * 6 + 2] = .6f;
            writer.WriteSamples(samples, 0, samples.Length);
        }
        var session = await CallMediaImport.ImportAsync(source, Path.Combine(_root, "calls"));
        float[] mono = AudioFile.ReadMono16k(session.SystemPath);
        Assert.Equal(16000, mono.Length);
        Assert.InRange(mono[100], .09f, .11f);
    }

    [Fact]
    public async Task SingleTrackPipelineSkipsMicrophoneAndBleedStages()
    {
        var session = new CallSession { Directory = _root, StartedAt = DateTimeOffset.Now, ImportedMedia = true };
        AudioFile.WriteWav16k(session.SystemPath, []);
        CallMeta.Save(session);
        await using var engine = new WhisperEngine(new WhisperEngineOptions { ModelPath = "unused.bin" });
        var transcriber = new CallTranscriber(new SpeechTranscriber(engine, null), () => new AppSettings());
        var progress = new Stages();
        await transcriber.TranscribeAsync(session, progress);
        Assert.DoesNotContain(CallTranscriptionStage.TranscribingMicrophone, progress.Values);
        Assert.DoesNotContain(CallTranscriptionStage.FilteringBleed, progress.Values);
        Assert.Contains(CallTranscriptionStage.Done, progress.Values);
        Assert.True(CallMeta.Load(_root)!.ImportedMedia);
        Assert.False(File.Exists(session.MicPath));
    }

    private sealed class Stages : IProgress<CallTranscriptionProgress>
    {
        public List<CallTranscriptionStage> Values { get; } = [];
        public void Report(CallTranscriptionProgress value) => Values.Add(value.Stage);
    }

}
