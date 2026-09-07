using Tapybara.Core.Calls;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Когда в транскрипте звонка ставится подпись «[время] Имя:».</summary>
public class TranscriptHeaderTests
{
    private static TimeSpan At(double minutes) => TimeSpan.FromMinutes(minutes);

    /// <summary>
    /// Первая реплика получает подпись, и это НЕ должно ничего переполнять.
    /// </summary>
    /// <remarks>
    /// Отсутствие предыдущей подписи задавалось как <c>TimeSpan.MinValue</c>,
    /// и вычитание его из времени реплики роняло сборку транскрипта
    /// переполнением на первой же строке. Час записанного разговора при этом
    /// не собирался вовсе — запись оставалась, а прочитать её было нельзя.
    /// </remarks>
    [Fact]
    public void FirstUtteranceGetsAHeaderAndDoesNotOverflow() =>
        Assert.True(CallTranscriber.NeedsHeader(At(0), "Кирилл", headerAt: null, headerSpeaker: null));

    [Fact]
    public void FirstUtteranceOfALongCallDoesNotOverflowEither() =>
        Assert.True(CallTranscriber.NeedsHeader(At(60), "Кирилл", headerAt: null, headerSpeaker: null));

    [Fact]
    public void ChangeOfSpeakerGetsAHeader() =>
        Assert.True(CallTranscriber.NeedsHeader(At(1), "Марина", At(0.9), "Кирилл"));

    /// <summary>Подряд идущие реплики одного человека не штампуются именем.</summary>
    [Fact]
    public void SameSpeakerShortlyAfterDoesNot() =>
        Assert.False(CallTranscriber.NeedsHeader(At(1), "Кирилл", At(0.9), "Кирилл"));

    /// <summary>А в длинном монологе подпись всё-таки повторяется.</summary>
    [Fact]
    public void SameSpeakerAfterALongStretchGetsAHeaderAgain() =>
        Assert.True(CallTranscriber.NeedsHeader(At(4), "Кирилл", At(1), "Кирилл"));

    [Fact]
    public void TheIntervalIsTwoMinutes()
    {
        Assert.False(CallTranscriber.NeedsHeader(At(2.9), "Кирилл", At(1), "Кирилл"));
        Assert.True(CallTranscriber.NeedsHeader(At(3.0), "Кирилл", At(1), "Кирилл"));
    }
}
