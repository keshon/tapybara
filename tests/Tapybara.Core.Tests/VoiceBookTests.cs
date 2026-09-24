using Tapybara.Core.Calls;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Книга голосов: запомнить, узнать, забыть.</summary>
public sealed class VoiceBookTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tapybara-voices-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_root, "voices.json");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>Вектор, указывающий «в сторону» <paramref name="axis"/>, с небольшим шумом.</summary>
    private static float[] Print(int axis, float noise = 0f, int seedAxis = -1)
    {
        var v = new float[8];
        v[axis] = 1f;
        if (seedAxis >= 0)
        {
            v[seedAxis] = noise;
        }

        return VoiceBook.Normalize(v);
    }

    [Fact]
    public void Similarity_IsOneForTheSameVoiceAndZeroForOrthogonal()
    {
        Assert.Equal(1.0, VoiceBook.Similarity(Print(0), Print(0)), precision: 6);
        Assert.Equal(0.0, VoiceBook.Similarity(Print(0), Print(1)), precision: 6);
    }

    /// <summary>Слепки разной длины — от разных моделей — несравнимы, а не «очень похожи».</summary>
    [Fact]
    public void Similarity_OfDifferentModelsIsZero() =>
        Assert.Equal(0.0, VoiceBook.Similarity([1f, 0f], [1f, 0f, 0f]));

    [Fact]
    public void Match_FindsTheLearnedVoice()
    {
        var book = new VoiceBook(FilePath);
        book.Learn("Кирилл", Print(0));
        book.Learn("Марина", Print(1));

        VoiceMatch? match = book.Match(Print(0, noise: 0.2f, seedAxis: 2));

        Assert.NotNull(match);
        Assert.Equal("Кирилл", match.Name);
    }

    /// <summary>
    /// Незнакомый голос не получает ничьего имени.
    /// </summary>
    /// <remarks>
    /// Регрессия той же природы, что и раздача имён по порядку: подсказка,
    /// которая всегда кого-то называет, — это угадывание с уверенным видом.
    /// </remarks>
    [Fact]
    public void Match_StaysSilentForAStranger()
    {
        var book = new VoiceBook(FilePath);
        book.Learn("Кирилл", Print(0));
        book.Learn("Марина", Print(1));

        Assert.Null(book.Match(Print(5)));
    }

    /// <summary>Два почти одинаково похожих кандидата — не повод выбирать одного.</summary>
    [Fact]
    public void Pick_StaysSilentWhenTwoPeopleAreEquallyLikely() =>
        Assert.Null(VoiceBook.Pick([new VoiceMatch("Кирилл", 0.80), new VoiceMatch("Дима", 0.78)]));

    [Fact]
    public void Pick_TakesAClearWinnerAboveTheThreshold()
    {
        VoiceMatch? match = VoiceBook.Pick([new VoiceMatch("Кирилл", 0.80), new VoiceMatch("Дима", 0.40)]);

        Assert.Equal("Кирилл", match?.Name);
    }

    /// <summary>Имя, уже отданное другому голосу этого звонка, второй раз не предлагается.</summary>
    [Fact]
    public void Match_SkipsNamesTakenOnThisCall()
    {
        var book = new VoiceBook(FilePath);
        book.Learn("Кирилл", Print(0));

        Assert.Null(book.Match(Print(0), exclude: ["Кирилл"]));
    }

    [Fact]
    public void Learn_KeepsOnlyTheNewestPrints()
    {
        var book = new VoiceBook(FilePath);
        for (int i = 0; i < VoiceBook.PrintsPerPerson + 3; i++)
        {
            var v = new float[16];
            v[i % 16] = 1f;
            v[(i + 1) % 16] = i * 0.1f;
            book.Learn("Кирилл", VoiceBook.Normalize(v));
        }

        Assert.Equal(VoiceBook.PrintsPerPerson, book.PrintsOf("Кирилл"));
    }

    [Fact]
    public void Learn_IgnoresTheSamePrintTwice()
    {
        var book = new VoiceBook(FilePath);
        book.Learn("Кирилл", Print(0));
        book.Learn("Кирилл", Print(0));

        Assert.Equal(1, book.PrintsOf("Кирилл"));
    }

    [Fact]
    public void Book_SurvivesARestart()
    {
        new VoiceBook(FilePath).Learn("Марина", Print(1));

        var reopened = new VoiceBook(FilePath);

        Assert.Equal("Марина", reopened.Match(Print(1))?.Name);
    }

    [Fact]
    public void Forget_AndClear_RemoveVoices()
    {
        var book = new VoiceBook(FilePath);
        book.Learn("Кирилл", Print(0));
        book.Learn("Марина", Print(1));

        book.Forget("Кирилл");
        Assert.Equal(["Марина"], book.People);

        book.Clear();
        Assert.Empty(book.People);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Rename_CarriesTheVoiceToTheNewName()
    {
        var book = new VoiceBook(FilePath);
        book.Learn("Кирил", Print(0));

        book.Rename("Кирил", "Кирилл");

        Assert.Equal("Кирилл", book.Match(Print(0))?.Name);
    }

    // --- слепки со звонка -----------------------------------------------------

    private static CallLine Theirs(double from, double to, string? voice) =>
        new(CallChannel.Theirs, TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), "текст", voice);

    [Fact]
    public void Gather_TakesTheLongestLinesUpToTheBudget()
    {
        const int rate = 100; // сто «сэмплов» в секунду — считать проще
        float[] audio = [.. Enumerable.Range(0, 100 * rate).Select(i => (float)i)];
        CallLine[] lines = [Theirs(0, 5, "A"), Theirs(10, 50, "A"), Theirs(60, 61, "A")];

        float[] speech = CallVoices.Gather(audio, lines, rate);

        Assert.Equal(CallVoices.PrintBudget.TotalSeconds * rate, speech.Length);
        Assert.Equal(10 * rate, speech[0]); // начинается с самой длинной реплики
    }

    [Fact]
    public void Prints_ArePerVoiceAndSkipVoicesWithTooLittleSpeech()
    {
        const int rate = 100;
        float[] audio = new float[100 * rate];
        var transcript = new CallTranscript
        {
            Lines = [Theirs(0, 20, "A"), Theirs(20, 21, "B")],
        };

        IReadOnlyDictionary<string, float[]> prints = CallVoices.Prints(audio, transcript, _ => [1f, 0f], rate);

        Assert.Equal(["A"], prints.Keys);
    }

    /// <summary>Без разделения голосов — один слепок на весь чужой канал.</summary>
    [Fact]
    public void Prints_CoverTheWholeOtherSideWhenVoicesWereNotSplit()
    {
        const int rate = 100;
        var transcript = new CallTranscript { Lines = [Theirs(0, 10, null)] };

        IReadOnlyDictionary<string, float[]> prints = CallVoices.Prints(new float[20 * rate], transcript, _ => [1f], rate);

        Assert.Equal([CallVoices.WholeOtherSide], prints.Keys);
    }
}
