using NAudio.Wave;
using Tapybara.Core.Audio;
using Tapybara.Core.Calls;
using Xunit;

namespace Tapybara.Core.Tests;

/// <summary>Запись дорожек звонка и их живучесть.</summary>
public class TimelineWavWriterTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "tapybara-tests", Guid.NewGuid().ToString("N"));

    public TimelineWavWriterTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Временная папка — не то, из-за чего тест должен падать.
        }

        GC.SuppressFinalize(this);
    }

    private string Path_(string name) => Path.Combine(_directory, name);

    private static float[] Chunk(double seconds, float amplitude = 0.2f)
    {
        float[] samples = new float[(int)(seconds * AudioCapture.TargetSampleRate)];
        Array.Fill(samples, amplitude);
        return samples;
    }

    [Fact]
    public void Write_KeepsDurationInStepWithTheData()
    {
        string path = Path_("plain.wav");
        using (var writer = new TimelineWavWriter(path))
        {
            writer.Write(Chunk(1), TimeSpan.FromSeconds(1));
            writer.Write(Chunk(1), TimeSpan.FromSeconds(2));

            Assert.Equal(2, writer.Duration.TotalSeconds, 2);
        }

        Assert.Equal(2, ReadDurationSeconds(path), 2);
    }

    /// <summary>
    /// Пропуск в опорных часах добивается тишиной.
    /// </summary>
    /// <remarks>
    /// Ровно ради этого класс и написан: loopback не отдаёт данных, пока
    /// собеседник молчит, и без добивки его дорожка была бы короче
    /// микрофонной на всю длину молчания.
    /// </remarks>
    [Fact]
    public void Write_PadsGapsAgainstTheReferenceClock()
    {
        string path = Path_("padded.wav");
        using (var writer = new TimelineWavWriter(path))
        {
            writer.Write(Chunk(1), TimeSpan.FromSeconds(1));

            // Пять секунд тишины в источнике, потом снова звук.
            writer.Write(Chunk(1), TimeSpan.FromSeconds(7));

            Assert.Equal(7, writer.Duration.TotalSeconds, 1);
        }

        Assert.Equal(7, ReadDurationSeconds(path), 1);
    }

    [Fact]
    public void Write_DoesNotPadWithinTolerance()
    {
        string path = Path_("tolerant.wav");
        using var writer = new TimelineWavWriter(path);

        // Порция пришла с отставанием в 50 мс — это норма, а не пропуск.
        writer.Write(Chunk(1), TimeSpan.FromSeconds(1.05));

        Assert.Equal(1, writer.Duration.TotalSeconds, 2);
    }

    [Fact]
    public void PadTo_ExtendsTheTrackToTheGivenLength()
    {
        string path = Path_("extended.wav");
        using (var writer = new TimelineWavWriter(path))
        {
            writer.Write(Chunk(1), TimeSpan.FromSeconds(1));
            writer.PadTo(TimeSpan.FromSeconds(4));
        }

        Assert.Equal(4, ReadDurationSeconds(path), 1);
    }

    [Fact]
    public void PadTo_DoesNothingWhenTheTrackIsAlreadyLonger()
    {
        string path = Path_("longer.wav");
        using (var writer = new TimelineWavWriter(path))
        {
            writer.Write(Chunk(3), TimeSpan.FromSeconds(3));
            writer.PadTo(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(3, ReadDurationSeconds(path), 1);
    }

    [Fact]
    public void Write_ClipsInsteadOfWrappingAround()
    {
        string path = Path_("clipped.wav");
        using (var writer = new TimelineWavWriter(path))
        {
            // Значения за пределами [-1, 1] при приведении к short переполнились
            // бы и превратились в громкий щелчок противоположного знака.
            writer.Write([2.5f, -3.0f, 0.5f], TimeSpan.FromSeconds(0));
        }

        float[] samples = AudioFile.ReadMono16k(path);

        Assert.True(samples[0] > 0.9f, $"было {samples[0]}");
        Assert.True(samples[1] < -0.9f, $"было {samples[1]}");
    }

    /// <summary>
    /// Заголовок обновляется по ходу записи, а не только при закрытии.
    /// </summary>
    /// <remarks>
    /// Это тест на самое дорогое из возможного: часовой разговор, потерянный
    /// из-за того, что процесс убили. Длины областей RIFF пишутся при
    /// закрытии файла, и без периодического обновления недописанный файл не
    /// открывается ничем.
    /// </remarks>
    [Fact]
    public void RefreshHeader_MakesTheFileReadableBeforeItIsClosed()
    {
        string path = Path_("live.wav");
        using var writer = new TimelineWavWriter(path);

        writer.Write(Chunk(2), TimeSpan.FromSeconds(2));
        writer.RefreshHeader();

        // Файл ещё открыт на запись — читаем его параллельно.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new WaveFileReader(stream);

        Assert.Equal(2, reader.TotalTime.TotalSeconds, 1);
    }

    private static double ReadDurationSeconds(string path)
    {
        using var reader = new WaveFileReader(path);
        return reader.TotalTime.TotalSeconds;
    }
}

/// <summary>Починка записей, оборванных не по-хорошему.</summary>
public class CallRepairTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "tapybara-tests", Guid.NewGuid().ToString("N"));

    public CallRepairTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Не наша забота.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Файл, у которого заголовок отстал от данных, — как после падения.</summary>
    private string WriteTruncatedHeaderWav(double seconds)
    {
        string path = Path.Combine(_directory, "broken.wav");

        var format = new WaveFormat(AudioCapture.TargetSampleRate, 16, 1);
        using (var writer = new WaveFileWriter(path, format))
        {
            for (int i = 0; i < (int)(seconds * AudioCapture.TargetSampleRate); i++)
            {
                writer.WriteSample(0.1f);
            }
        }

        BreakHeader(path);
        return path;
    }

    /// <summary>
    /// Обнулить длины в заголовке — ровно то, что остаётся, если процесс
    /// убили до закрытия файла.
    /// </summary>
    /// <remarks>
    /// Смещение области data ищем, а не берём равным сорока: длина служебной
    /// части WAV не фиксирована, и NAudio, например, пишет область fmt в
    /// восемнадцать байт вместо шестнадцати.
    /// </remarks>
    private static void BreakHeader(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        byte[] chunk = new byte[8];

        stream.Position = 12;
        while (stream.Position + 8 <= stream.Length)
        {
            long start = stream.Position;
            stream.ReadExactly(chunk);
            uint size = BitConverter.ToUInt32(chunk, 4);

            if (System.Text.Encoding.ASCII.GetString(chunk, 0, 4) == "data")
            {
                stream.Position = start + 4;
                stream.Write(new byte[4]);
                stream.Position = 4;
                stream.Write(new byte[4]);
                return;
            }

            stream.Position = start + 8 + size + (size % 2);
        }

        throw new InvalidOperationException("В файле нет области data.");
    }

    [Fact]
    public void RepairWav_RestoresTheDataLength()
    {
        string path = WriteTruncatedHeaderWav(3);

        Assert.True(CallRepair.RepairWav(path));

        using var reader = new WaveFileReader(path);
        Assert.Equal(3, reader.TotalTime.TotalSeconds, 1);
    }

    [Fact]
    public void RepairWav_LeavesHealthyFilesAlone()
    {
        string path = Path.Combine(_directory, "healthy.wav");
        var format = new WaveFormat(AudioCapture.TargetSampleRate, 16, 1);
        using (var writer = new WaveFileWriter(path, format))
        {
            for (int i = 0; i < AudioCapture.TargetSampleRate; i++)
            {
                writer.WriteSample(0.1f);
            }
        }

        Assert.False(CallRepair.RepairWav(path));
    }

    [Fact]
    public void RepairWav_IgnoresFilesThatAreNotWav()
    {
        string path = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(path, new string('x', 200));

        Assert.False(CallRepair.RepairWav(path));
    }

    [Fact]
    public void RepairWav_IgnoresMissingFiles() =>
        Assert.False(CallRepair.RepairWav(Path.Combine(_directory, "nothing-here.wav")));

    [Fact]
    public void RepairAll_FixesTracksAndUpdatesDuration()
    {
        string callDirectory = Path.Combine(_directory, "2026-03-14 09-05-30");
        Directory.CreateDirectory(callDirectory);

        var session = new CallSession
        {
            Directory = callDirectory,
            StartedAt = DateTimeOffset.Now,
            Duration = TimeSpan.Zero,
        };

        CallMeta.Save(session);

        foreach (string track in new[] { session.MicPath, session.SystemPath })
        {
            var format = new WaveFormat(AudioCapture.TargetSampleRate, 16, 1);
            using (var writer = new WaveFileWriter(track, format))
            {
                for (int i = 0; i < AudioCapture.TargetSampleRate * 2; i++)
                {
                    writer.WriteSample(0.1f);
                }
            }

            BreakHeader(track);
        }

        Assert.Equal(1, CallRepair.RepairAll(_directory));

        CallSession? repaired = CallMeta.Load(callDirectory);
        Assert.NotNull(repaired);
        Assert.True(repaired.Duration.TotalSeconds > 1.5, $"длительность {repaired.Duration}");
    }

    [Fact]
    public void RepairAll_ReturnsZeroForAnEmptyFolder() =>
        Assert.Equal(0, CallRepair.RepairAll(Path.Combine(_directory, "no-such-folder")));
}
