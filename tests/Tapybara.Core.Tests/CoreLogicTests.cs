using Tapybara.Core.Audio;
using Tapybara.Core.Calls;
using Tapybara.Core.Speech;
using Tapybara.Core.Windows;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Подбор порога детектора речи.</summary>
public class VadCalibratorTests
{
    private static VadProbe Probe(float threshold, double seconds, int regions = 1) =>
        new(threshold, regions, TimeSpan.FromSeconds(seconds));

    [Fact]
    public void Recommend_ReturnsNull_WhenThereIsNoSpeech() =>
        Assert.Null(VadCalibrator.Recommend([Probe(0.15f, 0.1), Probe(0.35f, 0)]));

    [Fact]
    public void Recommend_ReturnsNull_ForEmptyProbes() =>
        Assert.Null(VadCalibrator.Recommend([]));

    /// <summary>
    /// Берётся не самый строгий прошедший порог, а на шаг мягче.
    /// </summary>
    /// <remarks>
    /// Образец снимается в тепличных условиях: человек сознательно считает
    /// вслух, чётко и ровно. Живая речь тише и небрежнее, и порог, прошедший
    /// впритык, на ней начнёт терять слова.
    /// </remarks>
    [Fact]
    public void Recommend_StepsBackFromTheStrictestPassingThreshold()
    {
        // 0.15..0.45 держат речь, 0.55 и 0.65 её теряют.
        VadProbe[] probes =
        [
            Probe(0.15f, 10),
            Probe(0.25f, 9.8),
            Probe(0.35f, 9.5),
            Probe(0.45f, 9.0),
            Probe(0.55f, 4.0),
            Probe(0.65f, 1.0),
        ];

        Assert.Equal(0.35f, VadCalibrator.Recommend(probes));
    }

    [Fact]
    public void Recommend_KeepsTheLowestThreshold_WhenOnlyItPasses()
    {
        VadProbe[] probes =
        [
            Probe(0.15f, 10),
            Probe(0.25f, 2),
            Probe(0.35f, 1),
        ];

        Assert.Equal(0.15f, VadCalibrator.Recommend(probes));
    }
}

/// <summary>Слияние участков речи перед распознаванием.</summary>
public class SpeechRegionMergeTests
{
    private static SpeechRegion Region(double from, double to) =>
        new(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to));

    [Fact]
    public void Merge_JoinsRegionsSeparatedByAShortGap()
    {
        IReadOnlyList<SpeechRegion> merged = SpeechTranscriber.Merge(
            [Region(0, 1), Region(1.3, 2.5)]);

        Assert.Single(merged);
        Assert.Equal(TimeSpan.Zero, merged[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(2.5), merged[0].End);
    }

    [Fact]
    public void Merge_KeepsRegionsSeparatedByALongGap()
    {
        IReadOnlyList<SpeechRegion> merged = SpeechTranscriber.Merge(
            [Region(0, 1), Region(5, 6)]);

        Assert.Equal(2, merged.Count);
    }

    /// <summary>
    /// Короткие ответы не должны пропадать.
    /// </summary>
    /// <remarks>
    /// Порог был 300 мс, и односложное «да» или «нет» могло под него попасть.
    /// В транскрипте разговора пропавшее «нет» меняет смысл сказанного.
    /// </remarks>
    [Fact]
    public void Merge_KeepsShortAnswers()
    {
        IReadOnlyList<SpeechRegion> merged = SpeechTranscriber.Merge([Region(3.0, 3.25)]);

        Assert.Single(merged);
    }

    [Fact]
    public void Merge_DropsSlivers()
    {
        IReadOnlyList<SpeechRegion> merged = SpeechTranscriber.Merge([Region(3.0, 3.05)]);

        Assert.Empty(merged);
    }

    [Fact]
    public void Merge_ReturnsEmptyForNoRegions() => Assert.Empty(SpeechTranscriber.Merge([]));
}

/// <summary>Имена папок звонков.</summary>
public class CallSessionNamingTests
{
    private static readonly DateTimeOffset Moment =
        new(2026, 3, 14, 9, 5, 30, TimeSpan.FromHours(3));

    [Fact]
    public void BuildFolderName_UsesTimestampWhenTriggerIsUnknown() =>
        Assert.Equal("2026-03-14 09-05-30", CallSession.BuildFolderName(Moment, null));

    [Fact]
    public void BuildFolderName_AppendsTrigger() =>
        Assert.Equal("2026-03-14 09-05-30 (Zoom)", CallSession.BuildFolderName(Moment, "Zoom"));

    [Fact]
    public void Sanitize_RemovesCharactersForbiddenInPaths() =>
        Assert.Equal("Zoom Meeting", CallSession.Sanitize("Zoom:/ Meeting?"));

    /// <summary>
    /// Windows молча отбрасывает точку и пробел в конце имени.
    /// </summary>
    /// <remarks>
    /// Из-за этого папка создавалась не там, где её потом искали по записанному
    /// в мете имени.
    /// </remarks>
    [Theory]
    [InlineData("Teams.", "Teams")]
    [InlineData("Teams ", "Teams")]
    [InlineData("Teams. . ", "Teams")]
    public void Sanitize_TrimsTrailingDotsAndSpaces(string input, string expected) =>
        Assert.Equal(expected, CallSession.Sanitize(input));

    /// <summary>Имена устройств папкой быть не могут — создание падает.</summary>
    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("COM1")]
    [InlineData("NUL.txt")]
    public void Sanitize_EscapesReservedDeviceNames(string input) =>
        Assert.StartsWith("_", CallSession.Sanitize(input), StringComparison.Ordinal);

    [Fact]
    public void Sanitize_CapsLength()
    {
        string sanitized = CallSession.Sanitize(new string('a', 200));

        Assert.True(sanitized.Length <= 40, $"длина {sanitized.Length}");
    }

    [Fact]
    public void Sanitize_ReturnsEmptyForBlankInput()
    {
        Assert.Equal(string.Empty, CallSession.Sanitize(null));
        Assert.Equal(string.Empty, CallSession.Sanitize("   "));
    }
}

/// <summary>Отображение сочетаний клавиш.</summary>
public class HotkeyComboTests
{
    [Fact]
    public void Default_IsCtrlAltD() => Assert.Equal("Ctrl+Alt+D", HotkeyCombo.Default.ToString());

    [Theory]
    [InlineData(HotkeyModifiers.Control, 0x41, "Ctrl+A")]
    [InlineData(HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x70, "Alt+Shift+F1")]
    [InlineData(HotkeyModifiers.Win, 0x20, "Win+Space")]
    [InlineData(HotkeyModifiers.Control, 0x30, "Ctrl+0")]
    [InlineData(HotkeyModifiers.Control, 0x1B, "Ctrl+Esc")]
    public void ToString_FormatsLikeWindowsDoes(HotkeyModifiers modifiers, int key, string expected) =>
        Assert.Equal(expected, new HotkeyCombo(modifiers, (ushort)key).ToString());

    /// <summary>
    /// Один Shift глобальным хоткеем быть не может.
    /// </summary>
    /// <remarks>
    /// ⇧D в качестве глобального сочетания отняло бы у системы ввод заглавной
    /// буквы D во всех приложениях сразу.
    /// </remarks>
    [Fact]
    public void IsUsableAsGlobal_RejectsShiftOnly() =>
        Assert.False(new HotkeyCombo(HotkeyModifiers.Shift, 0x44).IsUsableAsGlobal);

    [Fact]
    public void IsUsableAsGlobal_RejectsNoModifiers() =>
        Assert.False(new HotkeyCombo(HotkeyModifiers.None, 0x44).IsUsableAsGlobal);

    [Theory]
    [InlineData(HotkeyModifiers.Control)]
    [InlineData(HotkeyModifiers.Alt)]
    [InlineData(HotkeyModifiers.Win)]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Shift)]
    public void IsUsableAsGlobal_AcceptsRealModifiers(HotkeyModifiers modifiers) =>
        Assert.True(new HotkeyCombo(modifiers, 0x44).IsUsableAsGlobal);
}

/// <summary>Огибающая громкости.</summary>
public class EnergyEnvelopeTests
{
    [Fact]
    public void LevelDb_IsMinusHundred_ForEmptyEnvelope() =>
        Assert.Equal(-100, EnergyEnvelope.Empty.LevelDb(TimeSpan.Zero, TimeSpan.FromSeconds(1)));

    [Fact]
    public void LevelDb_ReflectsAmplitude()
    {
        float[] loud = new float[AudioCapture.TargetSampleRate];
        float[] quiet = new float[AudioCapture.TargetSampleRate];
        for (int i = 0; i < loud.Length; i++)
        {
            loud[i] = 0.5f;
            quiet[i] = 0.05f;
        }

        double loudDb = EnergyEnvelope.Build(loud).LevelDb(TimeSpan.Zero, TimeSpan.FromSeconds(1));
        double quietDb = EnergyEnvelope.Build(quiet).LevelDb(TimeSpan.Zero, TimeSpan.FromSeconds(1));

        // Разница амплитуд в 10 раз — это 20 дБ.
        Assert.Equal(20, loudDb - quietDb, 1);
    }

    [Fact]
    public void LevelDb_ClampsRangeToTheRecording()
    {
        float[] samples = new float[AudioCapture.TargetSampleRate];
        Array.Fill(samples, 0.25f);

        EnergyEnvelope envelope = EnergyEnvelope.Build(samples);

        // Запрос далеко за концом записи не должен ни падать, ни врать.
        Assert.Equal(-100, envelope.LevelDb(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void Duration_MatchesTheSourceLength()
    {
        float[] samples = new float[AudioCapture.TargetSampleRate * 3];

        Assert.Equal(TimeSpan.FromSeconds(3), EnergyEnvelope.Build(samples).Duration);
    }
}

/// <summary>Нормализация громкости.</summary>
public class AudioNormalizerTests
{
    [Fact]
    public void Normalize_LeavesSilenceAlone()
    {
        float[] silence = new float[AudioCapture.TargetSampleRate];

        Assert.Same(silence, AudioNormalizer.Normalize(silence));
    }

    [Fact]
    public void Normalize_MakesQuietSpeechLouder()
    {
        float[] quiet = new float[AudioCapture.TargetSampleRate];
        for (int i = 0; i < quiet.Length; i++)
        {
            quiet[i] = 0.01f * MathF.Sin(2f * MathF.PI * 220f * i / AudioCapture.TargetSampleRate);
        }

        float[] normalized = AudioNormalizer.Normalize(quiet);

        Assert.True(normalized.Max(Math.Abs) > quiet.Max(Math.Abs) * 2);
        Assert.True(normalized.Max(Math.Abs) <= 1.0f);
    }

    [Fact]
    public void Normalize_DoesNotTurnLoudRecordingsDown()
    {
        float[] loud = new float[AudioCapture.TargetSampleRate];
        for (int i = 0; i < loud.Length; i++)
        {
            loud[i] = 0.9f * MathF.Sin(2f * MathF.PI * 220f * i / AudioCapture.TargetSampleRate);
        }

        Assert.Same(loud, AudioNormalizer.Normalize(loud));
    }

    [Fact]
    public void NormalizeInPlace_MatchesTheCopyingVersion()
    {
        float[] source = new float[AudioCapture.TargetSampleRate];
        for (int i = 0; i < source.Length; i++)
        {
            source[i] = 0.02f * MathF.Sin(2f * MathF.PI * 220f * i / AudioCapture.TargetSampleRate);
        }

        float[] copy = AudioNormalizer.Normalize(source);
        float[] inPlace = [.. source];
        AudioNormalizer.NormalizeInPlace(inPlace);

        Assert.Equal(copy, inPlace);
    }

    [Fact]
    public void HasAudibleContent_IsFalseForSilence() =>
        Assert.False(AudioNormalizer.HasAudibleContent(new float[AudioCapture.TargetSampleRate]));

    [Fact]
    public void HasAudibleContent_IsTrueForSpeechLikeLevels()
    {
        float[] samples = new float[AudioCapture.TargetSampleRate];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = 0.2f * MathF.Sin(2f * MathF.PI * 220f * i / AudioCapture.TargetSampleRate);
        }

        Assert.True(AudioNormalizer.HasAudibleContent(samples));
    }
}
