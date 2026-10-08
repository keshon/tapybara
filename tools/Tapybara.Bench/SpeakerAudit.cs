using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.Wave;
using Tapybara.Core.Audio;
using Tapybara.Core.Calls;
using Tapybara.Core.Settings;
using Tapybara.Core.Speech;

namespace Tapybara.Bench;

/// <summary>Ограниченный прогон по исходной дорожке, без миграции или записи звонка.</summary>
internal static class SpeakerAudit
{
    private static readonly JsonSerializerOptions ReadOptions = new() { Converters = { new JsonStringEnumConverter() } };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    internal static void Run(string directory, double start, double duration, int speakers, float? threshold)
    {
        if (!double.IsFinite(start) || start < 0 || !double.IsFinite(duration) || duration <= 0 || duration > 120)
        { throw new ArgumentOutOfRangeException(nameof(duration), "Начало >= 0; длина отрезка > 0 и <= 120 секунд."); }
        ArgumentOutOfRangeException.ThrowIfNegative(speakers);
        if (threshold is { } value && (!float.IsFinite(value) || value <= 0 || value > 1))
        { throw new ArgumentOutOfRangeException(nameof(threshold)); }

        var defaults = new AppSettings();
        string segmentation = ModelLocator.Resolve(defaults.VoiceSegmentationModelFileName)
            ?? throw new FileNotFoundException("Нет модели сегментации.");
        string embedding = ModelLocator.Resolve(defaults.VoiceEmbeddingModelFileName)
            ?? throw new FileNotFoundException("Нет модели слепков.");

        // Load мигрирует старые документы на диске. Диагностика читает только JSON.
        CallTranscript transcript = JsonSerializer.Deserialize<CallTranscript>(File.ReadAllText(
            Path.Combine(directory, CallSession.TranscriptDataFileName)), ReadOptions)
            ?? throw new InvalidDataException("Нет транскрипта.");
        if (transcript.Version > 2) { throw new InvalidDataException("Неизвестная версия транскрипта."); }

        using var reader = new AudioFileReader(Path.Combine(directory, CallSession.SystemFileName));
        if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1)
        { throw new InvalidDataException("Диагностика ждёт исходный system.wav: 16 кГц, моно."); }
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(start, reader.TotalTime.TotalSeconds);
        reader.CurrentTime = TimeSpan.FromSeconds(start);
        TimeSpan offset = reader.CurrentTime;
        int count = (int)Math.Ceiling(Math.Min(duration, (reader.TotalTime - offset).TotalSeconds) * 16000);
        var samples = new float[count];
        int read = 0;
        while (read < count)
        {
            int chunk = reader.Read(samples, read, count - read);
            if (chunk == 0) { break; }
            read += chunk;
        }
        if (read == 0) { throw new InvalidDataException("Отрезок не содержит сэмплов."); }
        if (read != count) { Array.Resize(ref samples, read); }
        samples = AudioNormalizer.Normalize(samples);

        var timer = Stopwatch.StartNew();
        using var diarizer = new SpeakerDiarizer(new SpeakerDiarizerOptions
        {
            SegmentationModelPath = segmentation, EmbeddingModelPath = embedding,
            ClusterThreshold = threshold ?? (float)defaults.VoiceSplitThreshold,
        });
        double loadSeconds = timer.Elapsed.TotalSeconds;
        timer.Restart();
        IReadOnlyList<SpeakerSpan> local = diarizer.Split(samples, speakers);
        double workSeconds = timer.Elapsed.TotalSeconds;
        SpeakerSpan[] spans = [.. local.Select(s => s with { Start = s.Start + offset, End = s.End + offset })];
        TimeSpan end = offset + TimeSpan.FromSeconds(read / 16000.0);
        var lines = transcript.Lines.Where(l => l.Channel == CallChannel.Theirs && l.End > offset && l.Start < end)
            .Select(line =>
            {
                TimeSpan from = line.Start > offset ? line.Start : offset;
                TimeSpan to = line.End < end ? line.End : end;
                SpeakerCoverageResult coverage = SpeakerCoverage.Measure(from, to, spans);
                return new
                {
                    line.Id, Start = line.Start.TotalSeconds, End = line.End.TotalSeconds,
                    Partial = from != line.Start || to != line.End,
                    HumanSpeaker = line.HumanEdits.HasFlag(CallLineEdits.Speaker),
                    Winner = SpeakerAssignment.For(from, to, spans),
                    coverage.Covered, coverage.Simultaneous, coverage.Totals, coverage.Intervals,
                };
            }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Version = 1, Scope = "excerpt-local clusters; no word alignment; no text attribution",
            Start = offset.TotalSeconds, End = end.TotalSeconds, ExpectedSpeakers = speakers,
            Threshold = threshold ?? (float)defaults.VoiceSplitThreshold, NormalizeAudio = true,
            SegmentationModel = Path.GetFileName(segmentation), EmbeddingModel = Path.GetFileName(embedding),
            LoadSeconds = loadSeconds, WorkSeconds = workSeconds, Spans = spans, Lines = lines,
        }, WriteOptions));
    }
}
