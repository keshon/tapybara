using Tapybara.Core.Speech;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Сопоставление распознанных реплик с найденными голосами.</summary>
public class SpeakerAssignmentTests
{
    private static SpeakerSpan Span(double from, double to, int speaker) =>
        new(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), speaker);

    private static int? At(double from, double to, params SpeakerSpan[] spans) =>
        SpeakerAssignment.For(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), spans);

    [Fact]
    public void For_PicksTheVoiceInsideWhichTheLineFalls() =>
        Assert.Equal(1, At(3, 4, Span(0, 2, 0), Span(2, 6, 1)));

    /// <summary>
    /// Реплика на стыке достаётся тому, кто говорил в ней дольше.
    /// </summary>
    /// <remarks>
    /// Люди перебивают друг друга, и реплика часто начинается на хвосте чужой
    /// фразы. Выбор «по тому, кто был в начале» отдавал бы такие реплики не
    /// тому человеку — тихо и на всём протяжении разговора.
    /// </remarks>
    [Fact]
    public void For_PrefersTheLongerOverlapOverTheEarlierOne() =>
        Assert.Equal(1, At(1.8, 5, Span(0, 2, 0), Span(2, 6, 1)));

    [Fact]
    public void For_ReturnsNothingWhenNoVoiceOverlaps() =>
        Assert.Null(At(10, 12, Span(0, 2, 0), Span(2, 6, 1)));

    [Fact]
    public void For_ReturnsNothingWithoutVoices() =>
        Assert.Null(SpeakerAssignment.For(TimeSpan.Zero, TimeSpan.FromSeconds(5), []));

    // --- подписи -------------------------------------------------------------

    [Fact]
    public void Label_GivesNamesInOrderOfFirstAppearance()
    {
        IReadOnlyDictionary<int, string> labels = SpeakerAssignment.Label(
            [Span(5, 6, 7), Span(0, 3, 2)],
            ["Кирилл", "Марина"],
            "Собеседник");

        Assert.Equal("Кирилл", labels[2]); // заговорил первым
        Assert.Equal("Марина", labels[7]);
    }

    /// <summary>
    /// Номера кластеров идут с дырами, а подписи — подряд.
    /// </summary>
    /// <remarks>
    /// На записи с единственным голосом разделитель вернул кластер номер 2,
    /// и пользователь увидел бы «Собеседник 2» там, где собеседник один.
    /// </remarks>
    [Fact]
    public void Label_NumbersUnnamedVoicesFromOneRegardlessOfClusterIds()
    {
        IReadOnlyDictionary<int, string> labels = SpeakerAssignment.Label(
            [Span(0, 3, 4), Span(4, 6, 9)],
            [],
            "Собеседник");

        Assert.Equal("Собеседник 1", labels[4]);
        Assert.Equal("Собеседник 2", labels[9]);
    }

    /// <summary>Голосу сверх числа имён достаётся номер, а не чужое имя.</summary>
    [Fact]
    public void Label_DoesNotHandOutAWrongNameWhenVoicesOutnumberPeople()
    {
        IReadOnlyDictionary<int, string> labels = SpeakerAssignment.Label(
            [Span(0, 3, 0), Span(4, 6, 1), Span(7, 9, 2)],
            ["Кирилл"],
            "Собеседник");

        Assert.Equal("Кирилл", labels[0]);
        Assert.Equal("Собеседник 2", labels[1]);
        Assert.Equal("Собеседник 3", labels[2]);
    }

    [Fact]
    public void Label_ReturnsNothingWithoutVoices() =>
        Assert.Empty(SpeakerAssignment.Label([], ["Кирилл"], "Собеседник"));
}
