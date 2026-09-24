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
}
