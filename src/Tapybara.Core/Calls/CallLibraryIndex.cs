using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Calls;

/// <summary>Кэш библиотеки для последовательного фонового сканирования, без UI и живого состояния записи.</summary>
public sealed class CallLibraryIndex
{
    private sealed record Cached(string Stamp, CallLibraryItem Item);
    private string? _root;
    private readonly Dictionary<string, Cached> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CallLibraryItem> Scan(string root, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string fullRoot = Path.GetFullPath(root);
        if (!fullRoot.Equals(_root, StringComparison.OrdinalIgnoreCase))
        {
            _cache.Clear();
            _root = fullRoot;
        }
        string[] directories;
        try
        {
            directories = Directory.Exists(root) ? Directory.GetDirectories(root) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Не удалось обновить библиотеку звонков.", ex);
            return [.. _cache.Values.Select(c => c.Item).OrderByDescending(i => i.Entry.Session.StartedAt)];
        }
        var found = new HashSet<string>(directories, StringComparer.OrdinalIgnoreCase);
        foreach (string gone in _cache.Keys.Where(k => !found.Contains(k)).ToArray())
        {
            _cache.Remove(gone);
        }
        List<CallLibraryItem> items = [];
        foreach (string directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string stamp = string.Join('|', new[] { CallSession.MetaFileName, CallSession.TranscriptDataFileName,
                    CallSession.TranscriptFileName, CallSession.MicFileName, CallSession.SystemFileName, CallLibrary.NoteFileName }
                    .Select(name => Stamp(Path.Combine(directory, name))));
                if (_cache.TryGetValue(directory, out Cached? cached) && cached.Stamp == stamp)
                {
                    items.Add(cached.Item);
                    continue;
                }
                if (CallLibrary.Describe(directory) is not { } entry)
                {
                    _cache.Remove(directory);
                    continue;
                }
                CallTranscript? transcript = entry.HasTranscriptData ? CallTranscriptStore.Load(directory) : null;
                string text = transcript is not null ? string.Join('\n', transcript.Lines.Select(l => l.Text))
                    : File.Exists(entry.Session.TranscriptPath) ? File.ReadAllText(entry.Session.TranscriptPath) : string.Empty;
                string searchable = string.Join('\n', entry.Session.Title, entry.Session.Trigger,
                    string.Join(' ', entry.Session.Participants), string.Join(' ', entry.Session.VoiceNames.Values), text);
                var item = new CallLibraryItem(entry, searchable);
                _cache[directory] = new Cached(stamp, item);
                items.Add(item);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"Не удалось проиндексировать {directory}.", ex);
                if (_cache.TryGetValue(directory, out Cached? cached))
                {
                    items.Add(cached.Item);
                }
            }
        }
        return [.. items.OrderByDescending(i => i.Entry.Session.StartedAt)];
    }

    private static string Stamp(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? $"{file.LastWriteTimeUtc.Ticks}:{file.Length}" : "-";
    }
}

/// <summary>Данные поиска, приготовленные вне UI-потока.</summary>
public sealed record CallLibraryItem(CallEntry Entry, string SearchText);

/// <summary>Единые критерии; верхняя дата включительна в местном календаре.</summary>
public sealed record CallLibraryFilter(string Query = "", string? Person = null, CallState? State = null,
    DateOnly? From = null, DateOnly? To = null)
{
    public bool Matches(CallLibraryItem item)
    {
        DateOnly date = DateOnly.FromDateTime(item.Entry.Session.StartedAt.LocalDateTime);
        return (Query.Trim().Length == 0 || item.SearchText.Contains(Query.Trim(), StringComparison.CurrentCultureIgnoreCase))
            && (State is null || item.Entry.State == State)
            && (From is null || date >= From) && (To is null || date <= To)
            && (Person is null || item.Entry.Session.Participants.Concat(item.Entry.Session.VoiceNames.Values)
                .Contains(Person, StringComparer.OrdinalIgnoreCase));
    }
}
