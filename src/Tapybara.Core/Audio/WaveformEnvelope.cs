using NAudio.Wave;

namespace Tapybara.Core.Audio;

/// <summary>Пиковая огибающая физического источника; ограниченная память без полного массива записи.</summary>
public sealed class WaveformEnvelope
{
    public static WaveformEnvelope Empty { get; } = new([], TimeSpan.Zero, TimeSpan.Zero);
    public IReadOnlyList<float> Peaks { get; }
    public TimeSpan Duration { get; }
    public TimeSpan BinDuration { get; }
    private WaveformEnvelope(float[] peaks, TimeSpan duration, TimeSpan binDuration)
    {
        Peaks = Array.AsReadOnly(peaks);
        Duration = duration;
        BinDuration = binDuration;
    }

    public static WaveformEnvelope Read(string path, int maximumBins = 6000, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBins, 1);
        cancellationToken.ThrowIfCancellationRequested();
        using IDisposable access = AudioSourceAccess.Acquire(path, cancellationToken);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new WaveFileReader(stream);
        ISampleProvider samples = reader.ToSampleProvider();
        int channels = samples.WaveFormat.Channels;
        int rate = samples.WaveFormat.SampleRate;
        long frames = reader.Length / reader.WaveFormat.BlockAlign;
        if (frames == 0) { return Empty; }
        long perBin = Math.Max(1, (frames - 1) / maximumBins + 1);
        float[] peaks = new float[(int)((frames - 1) / perBin + 1)];
        float[] buffer = new float[4096 * channels];
        long frame = 0;
        int count;
        while ((count = samples.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int offset = 0; offset + channels <= count && frame < frames; offset += channels, frame++)
            {
                float peak = 0;
                for (int channel = 0; channel < channels; channel++)
                {
                    float value = Math.Abs(buffer[offset + channel]);
                    if (float.IsFinite(value)) { peak = Math.Max(peak, Math.Min(value, 1)); }
                }
                int bin = (int)(frame / perBin);
                peaks[bin] = Math.Max(peaks[bin], peak);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (frame != frames) { throw new InvalidDataException("Audio source ended before its declared frame count."); }
        return new WaveformEnvelope(peaks, TimeSpan.FromSeconds(frame / (double)rate), TimeSpan.FromSeconds(perBin / (double)rate));
    }
}
