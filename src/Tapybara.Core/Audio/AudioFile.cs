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
    public static float[] ReadMono16k(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new AudioFileReader(path);

        // Все каналы участвуют в моно-сигнале, включая центральный канал видео.
        ISampleProvider source = reader.WaveFormat.Channels switch
        {
            1 => reader,
            2 => new StereoToMonoSampleProvider(reader) { LeftVolume = 0.5f, RightVolume = 0.5f },
            _ => new DownmixSampleProvider(reader),
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
            cancellationToken.ThrowIfCancellationRequested();
            samples.AddRange(scratch.AsSpan(0, read));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return [.. samples];
    }

    /// <summary>Convert media using Windows codecs without loading the whole recording into memory.</summary>
    public static void ConvertToWav16k(string sourcePath, string outputPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new AudioFileReader(sourcePath);
        ISampleProvider source = reader.WaveFormat.Channels switch
        {
            1 => reader,
            2 => new StereoToMonoSampleProvider(reader) { LeftVolume = .5f, RightVolume = .5f },
            _ => new DownmixSampleProvider(reader),
        };
        if (source.WaveFormat.SampleRate != MicrophoneCapture.TargetSampleRate)
            source = new WdlResamplingSampleProvider(source, MicrophoneCapture.TargetSampleRate);
        using var writer = new WaveFileWriter(outputPath, new WaveFormat(MicrophoneCapture.TargetSampleRate, 16, 1));
        float[] buffer = new float[MicrophoneCapture.TargetSampleRate];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteSamples(buffer, 0, read);
        }
        cancellationToken.ThrowIfCancellationRequested();
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
    private sealed class DownmixSampleProvider(ISampleProvider source) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        private float[] _scratch = [];
        public int Read(float[] buffer, int offset, int count)
        {
            int channels = source.WaveFormat.Channels;
            if (_scratch.Length < count * channels) _scratch = new float[count * channels];
            int frames = source.Read(_scratch, 0, count * channels) / channels;
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0;
                for (int channel = 0; channel < channels; channel++) sum += _scratch[frame * channels + channel];
                buffer[offset + frame] = sum / channels;
            }
            return frames;
        }
    }

}
