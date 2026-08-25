using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Tapybara.Core.Audio;

/// <summary>
/// Чтение и запись звука на диск в том же формате, в котором работает движок
/// распознавания (16 кГц моно). Нужен, чтобы один раз записать эталонный
/// образец и потом честно сравнивать на нём разные модели.
/// </summary>
public static class AudioFile
{
    /// <summary>Прочитать любой поддерживаемый файл и привести к 16 кГц моно float32.</summary>
    public static float[] ReadMono16k(string path)
    {
        using var reader = new AudioFileReader(path);

        // Больше двух каналов StereoToMonoSampleProvider не понимает — там
        // берём первый канал, как и при живом захвате.
        ISampleProvider source = reader.WaveFormat.Channels switch
        {
            1 => reader,
            2 => new StereoToMonoSampleProvider(reader) { LeftVolume = 0.5f, RightVolume = 0.5f },
            _ => new MultiplexingSampleProvider([reader], 1),
        };

        if (source.WaveFormat.SampleRate != MicrophoneCapture.TargetSampleRate)
        {
            source = new WdlResamplingSampleProvider(source, MicrophoneCapture.TargetSampleRate);
        }

        var samples = new List<float>((int)(reader.TotalTime.TotalSeconds * MicrophoneCapture.TargetSampleRate) + 1024);
        float[] scratch = new float[MicrophoneCapture.TargetSampleRate];
        int read;
        while ((read = source.Read(scratch, 0, scratch.Length)) > 0)
        {
            samples.AddRange(scratch.AsSpan(0, read));
        }

        return [.. samples];
    }

    /// <summary>Сохранить запись в WAV 16 кГц моно, 16 бит — чтобы её можно было послушать.</summary>
    public static void WriteWav16k(string path, ReadOnlySpan<float> samples)
    {
        var format = new WaveFormat(MicrophoneCapture.TargetSampleRate, 16, 1);
        using var writer = new WaveFileWriter(path, format);

        foreach (float sample in samples)
        {
            // Клиппинг обязателен: float за пределами [-1, 1] при приведении
            // к short переполнится и превратится в громкий щелчок.
            writer.WriteSample(Math.Clamp(sample, -1f, 1f));
        }
    }
}
