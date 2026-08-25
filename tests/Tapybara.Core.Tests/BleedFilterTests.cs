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
