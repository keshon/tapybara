using Tapybara.Core.Audio;
using Tapybara.Core.Calls;
using Xunit;

namespace Tapybara.Core.Tests;

public sealed class CallSessionCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tapybara-copy-" + Guid.NewGuid().ToString("N"));
    public CallSessionCopyTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task DuplicatePreservesDocumentsAndAudioButEditsAreIndependent()
    {
        string source = Path.Combine(_root, "source");
        var original = new CallSession { Directory = source, StartedAt = DateTimeOffset.UtcNow,
            ImportedMedia = true, ImportLanguage = "ru", SpeakerCountHint = 3, Title = "Original",
            VoiceNames = new Dictionary<string, string> { ["A"] = "Anna" } };
        CallMeta.Save(original);
        AudioFile.WriteWav16k(original.SystemPath, new float[16000]);
        var line = new CallLine(CallChannel.Theirs, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Corrected", "A")
            { HumanEdits = CallLineEdits.Text | CallLineEdits.Speaker };
        CallTranscriptStore.Save(source, new CallTranscript { Lines = [line], EditedByHand = true });
        File.WriteAllText(original.TranscriptPath, "# Original transcript");
        File.WriteAllText(Path.Combine(source, CallLibrary.NoteFileName), "Notes");
        File.WriteAllText(Path.Combine(source, "temporary.tmp"), "not session data");
        string[] files = Directory.GetFiles(source);
        var before = files.ToDictionary(file => Path.GetFileName(file)!, File.ReadAllBytes);
        var copy = await CallSessionCopy.DuplicateAsync(source, _root, "Original (copy)");
        Assert.NotEqual(source, copy.Directory);
        Assert.Equal("Original (copy)", CallMeta.Load(copy.Directory)!.Title);
        Assert.Equal(original.StartedAt, copy.StartedAt);
        Assert.True(copy.ImportedMedia);
        Assert.Equal("ru", copy.ImportLanguage);
        Assert.Equal("Anna", copy.VoiceNames["A"]);
        Assert.Equal(3, copy.SpeakerCountHint);
        foreach (string name in new[] { CallSession.SystemFileName, CallSession.TranscriptDataFileName,
            CallSession.TranscriptFileName, CallLibrary.NoteFileName })
        {
            Assert.Equal(before[name], File.ReadAllBytes(Path.Combine(copy.Directory, name)));
        }
        Assert.False(File.Exists(Path.Combine(copy.Directory, "temporary.tmp")));
        CallMeta.Update(copy.Directory, s => s with { Title = "Changed copy" });
        File.WriteAllText(copy.SystemPath, "changed");
        foreach (string file in files) { Assert.Equal(before[Path.GetFileName(file)], File.ReadAllBytes(file)); }
        Assert.DoesNotContain(Directory.GetDirectories(_root), d => Path.GetFileName(d).StartsWith(".copy-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelledCopyPublishesNothing()
    {
        string source = Path.Combine(_root, "source");
        CallMeta.Save(new CallSession { Directory = source, StartedAt = DateTimeOffset.UtcNow });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CallSessionCopy.DuplicateAsync(source, _root, "copy", cancellation.Token));
        Assert.Single(Directory.GetDirectories(_root));
    }

    [Fact]
    public void StagingSessionsAreNotVisibleInLibrary()
    {
        string staging = Path.Combine(_root, ".copy-staging");
        var session = new CallSession { Directory = staging, StartedAt = DateTimeOffset.UtcNow };
        CallMeta.Save(session);
        AudioFile.WriteWav16k(session.SystemPath, new float[16000]);
        Assert.Null(CallLibrary.Describe(staging));
        Assert.Empty(CallLibrary.Scan(_root));
    }

    [Fact]
    public async Task LockedAudioLeavesNoPartialCopy()
    {
        string source = Path.Combine(_root, "source");
        var session = new CallSession { Directory = source, StartedAt = DateTimeOffset.UtcNow };
        CallMeta.Save(session);
        AudioFile.WriteWav16k(session.MicPath, new float[16000]);
        using var locked = new FileStream(session.MicPath, FileMode.Open, FileAccess.Write, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => CallSessionCopy.DuplicateAsync(source, _root, "copy"));
        Assert.Single(Directory.GetDirectories(_root));
        Assert.Equal("source", Path.GetFileName(Directory.GetDirectories(_root)[0]));
    }
}
