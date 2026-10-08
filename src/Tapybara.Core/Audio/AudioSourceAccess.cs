namespace Tapybara.Core.Audio;

/// <summary>Короткоживущие ворота файла: починка ждёт огибающую вместо пропуска восстановления из-за sharing violation.</summary>
internal static class AudioSourceAccess
{
    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int References { get; set; }
    }
    private static readonly object RegistryGate = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);

    public static IDisposable Acquire(string path, CancellationToken cancellationToken = default)
    {
        string key = Path.GetFullPath(path);
        Entry entry;
        lock (RegistryGate)
        {
            if (!Entries.TryGetValue(key, out entry!)) { Entries[key] = entry = new Entry(); }
            entry.References++;
        }
        try { entry.Gate.Wait(cancellationToken); }
        catch { ReleaseReference(key, entry); throw; }
        return new Lease(key, entry);
    }

    private static void ReleaseReference(string key, Entry entry)
    {
        lock (RegistryGate)
        {
            if (--entry.References == 0)
            {
                Entries.Remove(key);
                entry.Gate.Dispose();
            }
        }
    }

    private sealed class Lease(string key, Entry entry) : IDisposable
    {
        private Entry? _entry = entry;
        public void Dispose()
        {
            Entry? current = Interlocked.Exchange(ref _entry, null);
            if (current is null) { return; }
            current.Gate.Release();
            ReleaseReference(key, current);
        }
    }
}
