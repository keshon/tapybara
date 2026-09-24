using Tapybara.Core.Calls;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Проверка реплик по слепкам голоса и выбор цитат.</summary>
public sealed class VoiceCheckTests
{
    /// <summary>Слепок «в сторону» оси <paramref name="axis"/>, чуть сдвинутый к <paramref name="lean"/>.</summary>
    private static float[] Print(int axis, int lean = -1, float amount = 0f)
    {
        var v = new float[8];
        v[axis] = 1f;
        if (lean >= 0)
        {
            v[lean] = amount;
        }

        return v;
    }

    private static CallLine Line(double at, string text, string? voice, double seconds = 6) =>
        new(CallChannel.Theirs, TimeSpan.FromSeconds(at), TimeSpan.FromSeconds(at + seconds), text, voice);

    /// <summary>Слепок по тексту реплики: текст начинается с оси — «0…», «1…».</summary>
    private static float[]? ByText(CallLine line) => Print(line.Text[0] - '0', line.Text.Length > 1 && line.Text[1] != ' ' ? line.Text[1] - '0' : -1, 0.3f);

    [Fact]
    public void Check_LeavesAConsistentVoiceAlone()
    {
        var transcript = new CallTranscript
        {
            Lines = [Line(0, "0 a", "A"), Line(10, "0 b", "A"), Line(20, "0 c", "A"), Line(30, "1 d", "B"), Line(40, "1 e", "B")],
        };

        CallTranscript checkedOne = VoiceCheck.Check(transcript, ByText);

        Assert.True(checkedOne.VoicesChecked);
        Assert.All(checkedOne.Lines, l => Assert.False(l.IsDoubtful));
        Assert.Equal(["A", "A", "A", "B", "B"], checkedOne.Lines.Select(l => l.Voice));
    }

    /// <summary>
    /// Реплика, явно звучащая как другой найденный голос, переезжает к нему сама.
    /// </summary>
    [Fact]
    public void Check_MovesALineThatClearlySoundsLikeAnotherVoice()
    {
        var transcript = new CallTranscript
        {
            Lines =
            [
                Line(0, "0 a", "A"), Line(10, "0 b", "A"), Line(20, "0 c", "A"), Line(30, "0 d", "A"),
                Line(40, "1 stray", "A"),
                Line(50, "1 e", "B"), Line(60, "1 f", "B"), Line(70, "1 g", "B"),
            ],
        };

        CallTranscript checkedOne = VoiceCheck.Check(transcript, ByText);

        CallLine stray = checkedOne.Lines.Single(l => l.Text == "1 stray");
        Assert.Equal("B", stray.Voice);
        Assert.False(stray.IsDoubtful);
    }

    /// <summary>
    /// Голоса не разделялись, а на той стороне был ещё человек: его реплики
    /// под сомнением, и проверка говорит, что голосов, похоже, больше.
    /// </summary>
    [Fact]
    public void Check_FlagsAnUnaccountedVoiceOnAnUnsplitCall()
    {
        var transcript = new CallTranscript
        {
            Lines =
            [
                .. Enumerable.Range(0, 8).Select(i => Line(i * 10, $"0 vitya {i}", null)),
                .. Enumerable.Range(0, 4).Select(i => Line(100 + (i * 10), $"2 someone {i}", null)),
            ],
        };

        CallTranscript checkedOne = VoiceCheck.Check(transcript, ByText);

        Assert.All(checkedOne.Lines.Where(l => l.Text.StartsWith('2')), l => Assert.True(l.IsDoubtful));
        Assert.All(checkedOne.Lines.Where(l => l.Text.StartsWith('0')), l => Assert.False(l.IsDoubtful));
        Assert.True(VoiceCheck.MoreVoicesLikely(checkedOne, out TimeSpan doubtful));
        Assert.Equal(TimeSpan.FromSeconds(24), doubtful);
    }

    [Fact]
    public void MoreVoicesLikely_IgnoresASingleOddLine()
    {
        var transcript = new CallTranscript
        {
            Lines =
            [
                .. Enumerable.Range(0, 20).Select(i => Line(i * 10, $"0 {i}", null)),
                Line(300, "2 odd", null),
            ],
        };

        Assert.False(VoiceCheck.MoreVoicesLikely(VoiceCheck.Check(transcript, ByText), out _));
    }

    [Fact]
    public void Check_DoesNotJudgeShortLines()
    {
        var transcript = new CallTranscript
        {
            Lines = [Line(0, "0 a", null), Line(10, "0 b", null), Line(20, "2 yes", null, seconds: 2)],
        };

        CallTranscript checkedOne = VoiceCheck.Check(transcript, ByText);

        Assert.Null(checkedOne.Lines[2].Fit);
    }

    /// <summary>Назначенное человеком снимает сомнение: это вернее замера.</summary>
    [Fact]
    public void Reassign_ClearsTheDoubt()
    {
        CallLine doubtful = Line(0, "x", "A") with { Fit = 0.3 };
        var transcript = new CallTranscript { Lines = [doubtful, Line(10, "y", "B")] };

        CallTranscript moved = CallVoices.Reassign(transcript, doubtful, "B");

        Assert.Equal("B", moved.Lines[0].Voice);
        Assert.Null(moved.Lines[0].Fit);
    }

    /// <summary>
    /// Цитаты — не самые длинные, а надёжные и из разных частей разговора.
    /// </summary>
    [Fact]
    public void PickQuotes_SkipsDoubtfulAndSpreadsOverTheCall()
    {
        List<CallLine> own =
        [
            Line(10, "early, sure enough to quote", "A") with { Fit = 0.85 },
            Line(20, "early and best of all voices", "A") with { Fit = 0.93 },
            Line(300, "middle, the longest line of the whole call by far", "A", seconds: 18) with { Fit = 0.42 },
            Line(310, "middle, sure", "A") with { Fit = 0.88 },
            Line(600, "late, sure", "A") with { Fit = 0.86 },
            Line(610, "late, a monologue too long to listen to", "A", seconds: 40) with { Fit = 0.9 },
        ];

        IReadOnlyList<CallLine> quotes = CallVoices.PickQuotes(own);

        Assert.Equal(["early and best of all voices", "middle, sure", "late, sure"], quotes.Select(q => q.Text));
    }

    [Fact]
    public void PickQuotes_FallsBackToDoubtfulWhenThatIsAllThereIs()
    {
        List<CallLine> own = [Line(10, "only doubtful", "A") with { Fit = 0.3 }];

        Assert.Single(CallVoices.PickQuotes(own));
    }
}
