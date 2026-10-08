namespace Tapybara.Core.Audio;

/// <summary>Ограниченный кэш источников по размерам/времени файла; чтение и проверка выполняются вне UI.</summary>
public sealed class WaveformCache(int capacity = 8)
{
    private sealed record Entry((long Length, long Ticks) Stamp, WaveformEnvelope Envelope);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _order = new();

    public Task<WaveformEnvelope> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        string key = Path.GetFullPath(path);
        cancellationToken.ThrowIfCancellationRequested();
        var before = Stamp(key);
        if (before is null) { return WaveformEnvelope.Empty; }
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry? entry) && entry.Stamp == before)
            {
                return entry.Envelope;
            }
        }
        WaveformEnvelope envelope = WaveformEnvelope.Read(key, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (Stamp(key) != before) { throw new IOException("Audio source changed while its envelope was being read."); }
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_entries.ContainsKey(key)) { _order.Enqueue(key); }
            _entries[key] = new Entry(before.Value, envelope);
            while (_entries.Count > capacity) { _entries.Remove(_order.Dequeue()); }
        }
        return envelope;
    }, cancellationToken);

    private static (long Length, long Ticks)? Stamp(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? (file.Length, file.LastWriteTimeUtc.Ticks) : null;
    }
}
