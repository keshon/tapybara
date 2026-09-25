using Tapybara.Core.Audio;
using Tapybara.Core.Calls;
using Tapybara.Core.Speech;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Отсев чужой речи, просочившейся в микрофонный канал.</summary>
public class BleedFilterTests
{
    /// <summary>Синтетическая дорожка: тон заданной громкости на заданном отрезке.</summary>
    private static float[] Tone(double totalSeconds, double fromSeconds, double toSeconds, float amplitude)
    {
        int total = (int)(totalSeconds * AudioCapture.TargetSampleRate);
        float[] samples = new float[total];

        int from = (int)(fromSeconds * AudioCapture.TargetSampleRate);
        int to = Math.Min((int)(toSeconds * AudioCapture.TargetSampleRate), total);

        for (int i = from; i < to; i++)
        {
            samples[i] = amplitude * MathF.Sin(2f * MathF.PI * 220f * i / AudioCapture.TargetSampleRate);
        }

        return samples;
    }

    private static TranscriptSegment Segment(double from, double to, string text) =>
        new(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), text);

    [Fact]
    public void Apply_ReturnsEmpty_WhenMicrophoneHasNothing()
    {
        BleedFilter.Result result = BleedFilter.Apply(
            [], [Segment(0, 1, "что-то")], EnergyEnvelope.Empty, EnergyEnvelope.Empty);

        Assert.Empty(result.Kept);
        Assert.Equal(0, result.RemovedTotal);
    }

    [Fact]
    public void Apply_KeepsOwnSpeech_WhenSystemChannelIsSilent()
    {
        float[] mic = Tone(4, 0, 4, 0.3f);
        float[] system = new float[mic.Length];

        BleedFilter.Result result = BleedFilter.Apply(
            [Segment(0, 2, "Моя реплика"), Segment(2, 4, "И вторая")],
            [],
            EnergyEnvelope.Build(mic),
            EnergyEnvelope.Build(system));

        Assert.Equal(2, result.Kept.Count);
        Assert.Equal(0, result.RemovedTotal);
    }

    /// <summary>
    /// Текстовое эхо: одна и та же фраза распозналась в обоих каналах.
    /// </summary>
    [Fact]
    public void Apply_RemovesTextEcho()
    {
        float[] mic = Tone(4, 0, 4, 0.3f);
        float[] system = Tone(4, 0, 4, 0.3f);

        BleedFilter.Result result = BleedFilter.Apply(
            [Segment(0, 2, "Давайте перенесём встречу на четверг")],
            [Segment(0, 2, "Давайте перенесем встречу на четверг")],
            EnergyEnvelope.Build(mic),
            EnergyEnvelope.Build(system));

        Assert.Empty(result.Kept);
        Assert.Equal(1, result.RemovedByText);
        Assert.Equal(0, result.RemovedByEnergy);
    }

    /// <summary>
    /// Настоящий звонок, где владелец микрофона молчал: из колонок в микрофон
    /// долетали куски длинных реплик собеседницы. Энергетика здесь бессильна —
    /// вся речь в микрофоне эхо, и опорная громкость канала сама на уровне эха.
    /// </summary>
    [Fact]
    public void Apply_RemovesEchoOfAPartOfALongLine()
    {
        TranscriptSegment[] theirs =
        [
            Segment(4.47, 5.87, "похвастаюсь"),
            Segment(6.78, 32.68, "Результатом работы, правда, моей, Инги, вот, Инга всю неделю мучила кампус и сделала такой красивый план продуктов, видите, вот, он в работе еще пока что, мы его сейчас пока пошарили там на узкий круг лиц, но скоро он будет доступен всем, он автоматически обновляется, он актуален,"),
            Segment(33.16, 38.92, "пока что, может быть, не полностью наполнен, но вот категории можно уже видеть, какие..."),
            Segment(39.86, 41.52, "Будут в работе точно."),
            Segment(47.84, 52.68, "Ну, клиентам, скорее всего, все-таки отдельно сделаем PDF-ку до 1 октября."),
            Segment(53.48, 54.28, "Постараемся успеть."),
            Segment(60.06, 64.06, "Так, 29. Ну, я думаю, можно начинать, в принципе. Нормально."),
            Segment(68.95, 75.29, "Так, у меня тут список, я пишу анонс, и, в общем-то, хочу и рассказать и вам, и пользователям."),
        ];
        TranscriptSegment[] mine =
        [
            Segment(7.22, 11.84, "Это был бы, что это было бы."),
            Segment(13.21, 18.27, "Вот, когда всю неделю мучила кампус, делала такой трещинный план."),
            Segment(21.50, 23.06, "Работа еще пока что."),
            Segment(40.09, 41.53, "Будут все проплатить после."),
            Segment(42.61, 42.81, "Вот."),
            Segment(51.29, 54.29, "Но 1 октября постараемся с ней."),
            Segment(61.18, 63.10, "Я думаю, можно узнать это."),
            Segment(72.02, 75.04, "И, в общем-то, хочу ей рассказать и вам, и вам."),
        ];

        BleedFilter.Result result = BleedFilter.Apply(mine, theirs, EnergyEnvelope.Empty, EnergyEnvelope.Empty);

        // Неразборчивое эхо текстом не поймать — его остаётся удалить руками.
        Assert.Equal(
            ["Это был бы, что это было бы.", "Будут все проплатить после.", "Вот."],
            result.Kept.Select(s => s.Text));
        Assert.Equal(5, result.RemovedByText);
    }

    /// <summary>Своя реплика поверх чужой, с общими словами, — не эхо.</summary>
    [Fact]
    public void Apply_KeepsOwnReplyThatSharesWordsWithTheOtherSide()
    {
        BleedFilter.Result result = BleedFilter.Apply(
            [Segment(3, 6, "Я думаю, до пятницы мы успеем всё."), Segment(7, 8, "Да.")],
            [Segment(0, 10, "Давайте обсудим, что мы успеем сделать до пятницы, да, и что перенесём на следующую неделю.")],
            EnergyEnvelope.Empty,
            EnergyEnvelope.Empty);

        Assert.Equal(2, result.Kept.Count);
    }

    [Fact]
    public void Apply_KeepsSimilarPhrasesThatDoNotOverlapInTime()
    {
        float[] mic = Tone(10, 0, 10, 0.3f);
        float[] system = Tone(10, 0, 10, 0.3f);

        BleedFilter.Result result = BleedFilter.Apply(
            [Segment(0, 2, "Давайте перенесём встречу")],
            [Segment(7, 9, "Давайте перенесём встречу")],
            EnergyEnvelope.Build(mic),
            EnergyEnvelope.Build(system));

        Assert.Single(result.Kept);
        Assert.Equal(0, result.RemovedTotal);
    }

    /// <summary>
    /// Энергетический признак: просочившаяся речь заметно тише собственной,
    /// а системный канал в этот момент активен.
    /// </summary>
    [Fact]
    public void Apply_RemovesQuietSegmentWhileSystemChannelIsLoud()
    {
        // Своя речь громкая в первой половине, тихое эхо — во второй.
        float[] mic = new float[10 * AudioCapture.TargetSampleRate];
        Copy(Tone(10, 0, 4, 0.4f), mic);
        Copy(Tone(10, 5, 9, 0.01f), mic);

        // Собеседник говорит ровно тогда, когда в микрофоне тихое эхо.
        float[] system = Tone(10, 5, 9, 0.4f);

        BleedFilter.Result result = BleedFilter.Apply(
            [Segment(0, 4, "Моя громкая реплика"), Segment(5, 9, "Совсем другой текст тут")],
            [Segment(5, 9, "Реплика собеседника целиком иная")],
            EnergyEnvelope.Build(mic),
            EnergyEnvelope.Build(system));

        Assert.Single(result.Kept);
        Assert.Equal("Моя громкая реплика", result.Kept[0].Text);
        Assert.Equal(1, result.RemovedByEnergy);
        Assert.Equal(0, result.RemovedByText);
    }

    private static void Copy(float[] source, float[] target)
    {
        for (int i = 0; i < source.Length && i < target.Length; i++)
        {
            if (source[i] != 0)
            {
                target[i] = source[i];
            }
        }
    }

    // --- метрика схожести ---------------------------------------------------

    [Fact]
    public void Similarity_IsOne_ForIdenticalStrings() =>
        Assert.Equal(1.0, BleedFilter.Similarity("привет мир", "привет мир"), 6);

    [Fact]
    public void Similarity_IsOne_ForTwoEmptyStrings() =>
        Assert.Equal(1.0, BleedFilter.Similarity(string.Empty, string.Empty), 6);

    [Fact]
    public void Similarity_IsZero_WhenOneSideIsEmpty() =>
        Assert.Equal(0.0, BleedFilter.Similarity("привет", string.Empty), 6);

    [Fact]
    public void Similarity_IsHigh_ForOneLetterDifference() =>
        Assert.True(BleedFilter.Similarity("перенесём встречу", "перенесем встречу") > 0.9);

    [Fact]
    public void Similarity_IsLow_ForUnrelatedText() =>
        Assert.True(BleedFilter.Similarity("собака", "кораблекрушение") < 0.4);

    [Fact]
    public void Similarity_IsSymmetric()
    {
        double forward = BleedFilter.Similarity("абракадабра", "абра кадабра");
        double backward = BleedFilter.Similarity("абра кадабра", "абракадабра");

        Assert.Equal(forward, backward, 6);
    }
}
