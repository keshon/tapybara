namespace Tapybara.Core.Editing;

/// <summary>Ограниченная история неизменяемых снимков, независимая от UI и хранилища.</summary>
public sealed class DocumentHistory<T>(int capacity = 100)
{
    private readonly List<(T Before, T After)> _undo = [];
    private readonly Stack<(T Before, T After)> _redo = [];

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Record(T before, T after)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _undo.Add((before, after));
        if (_undo.Count > capacity)
        {
            _undo.RemoveAt(0);
        }
        _redo.Clear();
    }

    public bool Undo(Func<T, bool> restore)
    {
        if (!CanUndo)
        {
            return false;
        }
        var entry = _undo[^1];
        if (!restore(entry.Before))
        {
            return false;
        }
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Push(entry);
        return true;
    }

    public bool Redo(Func<T, bool> restore)
    {
        if (!CanRedo)
        {
            return false;
        }
        var entry = _redo.Peek();
        if (!restore(entry.After))
        {
            return false;
        }
        _redo.Pop();
        _undo.Add(entry);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
