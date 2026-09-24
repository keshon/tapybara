using Tapybara.Core.Dictation;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>История диктовок на диске.</summary>
public sealed class DictationJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tapybara-journal-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_root, "dictations.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static DictationEntry At(int minute, string text) =>
        new(new DateTimeOffset(2026, 9, 24, 10, minute, 0, TimeSpan.FromHours(3)), text);

    [Fact]
    public void Load_IsEmptyBeforeTheFirstDictation() =>
        Assert.Empty(new DictationJournal(FilePath).Load());

    [Fact]
    public void Load_ReturnsNewestFirst()
    {
        var journal = new DictationJournal(FilePath);
        journal.Append(At(1, "первая"));
        journal.Append(At(2, "вторая"));

        Assert.Equal(["вторая", "первая"], journal.Load().Select(e => e.Text));
    }

    /// <summary>Текст с переводами строк и кавычками переживает запись построчно.</summary>
    [Fact]
    public void Append_KeepsMultilineTextIntact()
    {
        var journal = new DictationJournal(FilePath);
        const string text = "Абзац один.\n\nАбзац «два» — с C# и \"кавычками\".";
        journal.Append(At(1, text));

        Assert.Equal(text, journal.Load().Single().Text);
    }

    /// <summary>
    /// Одна оборванная строка не отнимает остальную историю.
    /// </summary>
    /// <remarks>
    /// Процесс могли убить посреди дописывания. Потерять из-за этого все
    /// прошлые диктовки — ровно то, от чего история должна страховать.
    /// </remarks>
    [Fact]
    public void Load_SkipsABrokenLine()
    {
        var journal = new DictationJournal(FilePath);
        journal.Append(At(1, "целая"));
        File.AppendAllText(FilePath, "{\"At\":\"2026-09-24T10:02:00+03:00\",\"Tex");
        File.AppendAllText(FilePath, "\n");
        journal.Append(At(3, "тоже целая"));

        Assert.Equal(["тоже целая", "целая"], journal.Load().Select(e => e.Text));
    }

    [Fact]
    public void Remove_TakesOutOnlyThatDictation()
    {
        var journal = new DictationJournal(FilePath);
        DictationEntry keep = At(1, "оставить");
        DictationEntry drop = At(2, "убрать");
        journal.Append(keep);
        journal.Append(drop);

        journal.Remove(drop);

        Assert.Equal([keep], journal.Load());
    }

    [Fact]
    public void Clear_LeavesNothingOnDisk()
    {
        var journal = new DictationJournal(FilePath);
        journal.Append(At(1, "что-то личное"));

        journal.Clear();

        Assert.Empty(journal.Load());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Append_IgnoresEmptyText()
    {
        var journal = new DictationJournal(FilePath);
        journal.Append(At(1, "   "));

        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Changed_FiresOnEveryEdit()
    {
        var journal = new DictationJournal(FilePath);
        int changes = 0;
        journal.Changed += () => changes++;

        DictationEntry entry = At(1, "текст");
        journal.Append(entry);
        journal.Remove(entry);
        journal.Clear();

        Assert.Equal(3, changes);
    }
}
