using NAudio.Wave;
using Tapybara.Core.Audio;
using Tapybara.Core.Calls;
using Tapybara.Core.Concurrency;
using Tapybara.Core.Settings;
using Tapybara.Core.Speech;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Регрессии поздней отмены, длинной тишины и страховочных копий.</summary>
public sealed class CallSafetyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tapybara-tests", Guid.NewGuid().ToString("N"));

    public CallSafetyTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static CallLine Line(string text, string voice = "A") =>
        new(CallChannel.Theirs, TimeSpan.Zero, TimeSpan.FromSeconds(5), text, voice);

    [Fact]
    public void CancellationBeforeCommitNeverRunsTheWrite()
    {
        using var operation = new CancelableOperation();
        Assert.True(operation.TryCancel());
        bool written = false;
        Assert.Throws<OperationCanceledException>(() => operation.Commit(() => written = true));
        Assert.False(written);
    }

    [Fact]
    public void CancellationDuringCommitDoesNotInterruptOrMisreportIt()
    {
        using var operation = new CancelableOperation();
        operation.Commit(() =>
        {
            Assert.False(operation.TryCancel());
            Assert.False(operation.Token.IsCancellationRequested);
            File.WriteAllText(Path.Combine(_directory, "first"), "saved");
            File.WriteAllText(Path.Combine(_directory, "second"), "saved");
        });
        Assert.True(File.Exists(Path.Combine(_directory, "second")));
        Assert.False(operation.TryCancel());
        Assert.Throws<InvalidOperationException>(() => operation.Commit(() => { }));
    }

    [Fact]
    public void ReturningSpeechAfterTwentyMinutesKeepsItsActualPosition()
    {
        string path = Path.Combine(_directory, "system.wav");
        float[] speech = new float[AudioCapture.TargetSampleRate];
        Array.Fill(speech, 0.5f);
        using (var writer = new TimelineWavWriter(path))
        {
            writer.Write(speech, TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(1));
            Assert.Equal(1201, writer.Duration.TotalSeconds);
        }

        using var reader = new WaveFileReader(path);
        reader.CurrentTime = TimeSpan.FromMinutes(20);
        Assert.True(reader.ReadNextSampleFrame()![0] > 0.4f);
    }

    [Fact]
    public void SilentTailLongerThanTenMinutesIsPreserved()
    {
        string path = Path.Combine(_directory, "tail.wav");
        using (var writer = new TimelineWavWriter(path))
        {
            writer.PadTo(TimeSpan.FromMinutes(20));
        }

        using var reader = new WaveFileReader(path);
        Assert.Equal(TimeSpan.FromMinutes(20), reader.TotalTime);
    }

    [Fact]
    public void VoiceCheckStopsAfterCancellationInsideTheLastEmbedding()
    {
        using var cancellation = new CancellationTokenSource();
        var transcript = new CallTranscript { Lines = [Line("Last phrase")] };
        Assert.Throws<OperationCanceledException>(() => VoiceCheck.Check(transcript, _ =>
        {
            cancellation.Cancel();
            return [1f, 0f];
        }, cancellation.Token));
    }

    [Fact]
    public void VoiceprintsDoNotEmbedTheNextSpeakerAfterCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var transcript = new CallTranscript { Lines = [Line("One", "A"), Line("Two", "B")] };
        float[] audio = new float[5 * AudioCapture.TargetSampleRate];
        int embedded = 0;
        Assert.Throws<OperationCanceledException>(() => CallVoices.Prints(audio, transcript, _ =>
        {
            embedded++;
            cancellation.Cancel();
            return [1f, 0f];
        }, cancellationToken: cancellation.Token));
        Assert.Equal(1, embedded);
    }

    [Fact]
    public void CancelledAudioReadDoesNotOpenAFile()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => AudioFile.ReadMono16k(
            Path.Combine(_directory, "not-created.wav"), cancellation.Token));
    }

    [Fact]
    public void TranscriptEditPreservesPreviousBytesAndNoOpDoesNotRotateThem()
    {
        CallTranscriptStore.Save(_directory, new CallTranscript { Lines = [Line("Original")] });
        string path = Path.Combine(_directory, CallSession.TranscriptDataFileName);
        string original = File.ReadAllText(path);
        CallTranscriptStore.Update(_directory, t => t with { Lines = [Line("Corrected")], EditedByHand = true });
        Assert.Equal(original, File.ReadAllText(path + ".previous"));
        CallTranscriptStore.Update(_directory, t => t);
        Assert.Equal(original, File.ReadAllText(path + ".previous"));
        Assert.Equal("Corrected", CallTranscriptStore.Load(_directory)!.Lines[0].Text);
    }

    [Fact]
    public void MetadataEditPreservesPreviousNames()
    {
        var session = new CallSession
        {
            Directory = _directory,
            StartedAt = DateTimeOffset.UtcNow,
            VoiceNames = new Dictionary<string, string> { ["A"] = "Anna" },
        };
        CallMeta.Save(session);
        string original = File.ReadAllText(session.MetaPath);
        CallMeta.Update(_directory, s => s with { VoiceNames = new Dictionary<string, string>() });
        Assert.Equal(original, File.ReadAllText(session.MetaPath + ".previous"));
    }

    [Fact]
    public async Task LateCancellationDoesNotCommitOrReportDone()
    {
        var session = new CallSession { Directory = _directory, StartedAt = DateTimeOffset.UtcNow };
        AudioFile.WriteWav16k(session.MicPath, []);
        AudioFile.WriteWav16k(session.SystemPath, []);
        CallTranscriptStore.Save(_directory, new CallTranscript { Lines = [Line("Human correction")] });
        string before = File.ReadAllText(session.TranscriptDataPath);
        using var cancellation = new CancellationTokenSource();
        await using var engine = new WhisperEngine(new WhisperEngineOptions { ModelPath = "unused.bin" });
        var transcriber = new CallTranscriber(new SpeechTranscriber(engine, null), () => new AppSettings());
        var progress = new CancellingProgress(cancellation);
        await Assert.ThrowsAsync<OperationCanceledException>(() => transcriber.TranscribeAsync(session, progress, cancellation.Token));
        Assert.Equal(before, File.ReadAllText(session.TranscriptDataPath));
        Assert.DoesNotContain(CallTranscriptionStage.Done, progress.Stages);
    }

    private sealed class CancellingProgress(CancellationTokenSource cancellation) : IProgress<CallTranscriptionProgress>
    {
        public List<CallTranscriptionStage> Stages { get; } = [];

        public void Report(CallTranscriptionProgress value)
        {
            Stages.Add(value.Stage);
            if (value.Stage == CallTranscriptionStage.FilteringBleed)
            {
                cancellation.Cancel();
            }
        }
    }
}
