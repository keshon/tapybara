using Tapybara.Core.Editing;

namespace Tapybara.Core.Calls;

/// <summary>Правки одного звонка с немедленным сохранением и историей в пределах сеанса приложения.</summary>
public sealed class CallEditSession(string directory, CallTranscript document)
{
    private sealed record Snapshot(CallTranscript Transcript, IReadOnlyDictionary<string, string> Names, IReadOnlyList<string> ChangedNames);
    private readonly DocumentHistory<Snapshot> _history = new();
    public CallTranscript Document { get; private set; } = document;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    /// <summary>Фоновая оценка может измениться, но другая правка документа отменяет старую историю.</summary>
    public void Synchronize(CallTranscript current)
    {
        if (!SameEdits(Document, current))
        {
            _history.Clear();
        }
        Document = current;
    }

    public bool Execute(Func<CallTranscript, CallTranscript> command, Func<CallSession, CallSession>? metadataChange = null)
    {
        CallTranscript before = Document;
        IReadOnlyDictionary<string, string> beforeNames = CallMeta.Load(directory)?.VoiceNames ?? new Dictionary<string, string>();
        IReadOnlyDictionary<string, string> afterNames = beforeNames;
        CallTranscript? updated = CallTranscriptStore.TryUpdate(directory, before.Revision, command, saved =>
        {
            CallSession? meta = CallMeta.Update(directory, current => (metadataChange?.Invoke(current) ?? current) with { Voices = saved.Voices });
            afterNames = meta?.VoiceNames ?? beforeNames;
        });
        if (updated is null)
        {
            ReloadAfterConflict();
            return false;
        }
        Document = updated;
        List<string> changedNames = [.. beforeNames.Keys.Union(afterNames.Keys)
            .Where(key => beforeNames.GetValueOrDefault(key) != afterNames.GetValueOrDefault(key))];
        if (!SameEdits(before, updated) || changedNames.Count > 0)
        {
            _history.Record(new Snapshot(before, beforeNames, changedNames), new Snapshot(updated, afterNames, changedNames));
        }
        return true;
    }

    public bool Undo() => _history.Undo(Restore);
    public bool Redo() => _history.Redo(Restore);

    private bool Restore(Snapshot snapshot)
    {
        CallTranscript? updated = CallTranscriptStore.TryUpdate(directory, Document.Revision, current => current with
        {
            Lines = snapshot.Transcript.Lines,
            EditedByHand = snapshot.Transcript.EditedByHand,
            VoicePrints = new Dictionary<string, float[]>(),
        }, saved =>
        {
            CallMeta.Update(directory, meta =>
            {
                var names = new Dictionary<string, string>(meta.VoiceNames);
                foreach (string key in snapshot.ChangedNames)
                {
                    if (snapshot.Names.TryGetValue(key, out string? name))
                    {
                        names[key] = name;
                    }
                    else
                    {
                        names.Remove(key);
                    }
                }
                return meta with { Voices = saved.Voices, VoiceNames = names };
            });
        });
        if (updated is null)
        {
            // Clear нельзя вызывать внутри Undo/Redo: они ещё держат запись истории.
            return false;
        }
        Document = updated;
        return true;
    }

    private void ReloadAfterConflict()
    {
        _history.Clear();
        if (CallTranscriptStore.Load(directory) is { } current)
        {
            Document = current;
        }
    }

    private static bool SameEdits(CallTranscript left, CallTranscript right) => left.EditedByHand == right.EditedByHand
        && left.Lines.Select(l => l with { Fit = null }).SequenceEqual(right.Lines.Select(l => l with { Fit = null }));
}
