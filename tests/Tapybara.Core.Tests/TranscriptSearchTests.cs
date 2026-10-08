using Tapybara.Core.Calls;
using Tapybara.Core.Editing;
using Xunit;

namespace Tapybara.Core.Tests;

public sealed class TranscriptSearchTests
{
    [Fact]
    public void LiteralSearchKeepsExactOffsetsAndDoesNotExpandSimilarForms()
    {
        Assert.Equal(new[] { new TextMatch(0, 3), new TextMatch(8, 3) }, TextSearch.Find("[a] and [A]", "[a]"));
        Assert.Empty(TextSearch.Find("договор договоры", "догавор"));
        Assert.Equal(new[] { new TextMatch(0, 2), new TextMatch(2, 2) }, TextSearch.Find("aaaaa", "aa"));
        Assert.Empty(TextSearch.Find("anything", ""));
    }
    [Fact]
    public void ReplaceAllIsOneUndoableTextOnlyTransaction()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tapybara-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var a = new CallLine(CallChannel.Theirs, TimeSpan.Zero, TimeSpan.FromSeconds(3), "Тест [a] тест", "A")
            { Id = Guid.NewGuid(), HumanEdits = CallLineEdits.Speaker, TimingEstimated = true };
            var b = new CallLine(CallChannel.Mine, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), "тестовый тест", null) { Id = Guid.NewGuid() };
            CallTranscriptStore.Save(directory, new CallTranscript { Lines = [a, b] });
            var session = new CallEditSession(directory, CallTranscriptStore.Load(directory)!);
            Assert.True(session.Execute(t => TranscriptSearch.ReplaceAll(t, "тест", "$1")));
            Assert.Equal("$1 [a] $1", session.Document.Lines[0].Text);
            Assert.Equal("$1овый $1", session.Document.Lines[1].Text);
            Assert.Equal(a.Id, session.Document.Lines[0].Id);
            Assert.Equal(a.Start, session.Document.Lines[0].Start);
            Assert.Equal(a.End, session.Document.Lines[0].End);
            Assert.Equal(a.Voice, session.Document.Lines[0].Voice);
            Assert.Equal(a.Channel, session.Document.Lines[0].Channel);
            Assert.True(session.Document.Lines[0].TimingEstimated);
            Assert.True(session.Document.Lines[0].HumanEdits.HasFlag(CallLineEdits.Speaker));
            Assert.True(session.Document.Lines[0].HumanEdits.HasFlag(CallLineEdits.Text));
            Assert.Equal(new[] { a, b }, session.Document.OriginalLines);
            Assert.True(session.Undo());
            Assert.Equal(new[] { a, b }, session.Document.Lines);
            Assert.False(session.CanUndo);
            Assert.True(session.Redo());
            Assert.Equal("$1 [a] $1", CallTranscriptStore.Load(directory)!.Lines[0].Text);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    [Fact]
    public void EmptyReplacementDeletesLiterallyAndNoMatchPreservesDocument()
    {
        var line = new CallLine(CallChannel.Mine, TimeSpan.Zero, TimeSpan.FromSeconds(3), "one.one", null) { Id = Guid.NewGuid() };
        var doc = new CallTranscript { Lines = [line] };
        Assert.Equal(".", TranscriptSearch.ReplaceAll(doc, "one", "").Lines[0].Text);
        Assert.Same(doc, TranscriptSearch.ReplaceAll(doc, "", "x"));
        Assert.Same(doc, TranscriptSearch.ReplaceAll(doc, "missing", "x"));
        Assert.Same(doc, TranscriptSearch.ReplaceAll(doc, "one", "one"));
    }
}
