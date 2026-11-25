namespace MusicWorker.Core.Editing;

/// <summary>Ein Eintrag im Verlauf, inklusive Auswahl vor und nach der Bearbeitung.</summary>
public sealed record HistoryEntry(
    IEditCommand Command,
    SelectionRange SelectionBefore,
    SelectionRange SelectionAfter,
    long StateId,
    IReadOnlyList<Marker>? MarkersBefore = null,
    IReadOnlyList<Marker>? MarkersAfter = null);

/// <summary>
/// Verlauf für Rückgängig/Wiederholen mit begrenzter Tiefe.
/// Jeder Zustand bekommt eine eindeutige ID – so lässt sich zuverlässig erkennen,
/// ob das Dokument seit dem letzten Speichern verändert wurde.
/// </summary>
public sealed class EditHistory
{
    private readonly LinkedList<HistoryEntry> _undo = new();
    private readonly Stack<HistoryEntry> _redo = new();
    private long _nextStateId = 1;
    private long _baseStateId;

    public EditHistory(int capacity = 50)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoDescription => _undo.Last?.Value.Command.Description;

    public string? RedoDescription => _redo.TryPeek(out var entry) ? entry.Command.Description : null;

    /// <summary>ID des aktuellen Dokumentzustands.</summary>
    public long CurrentStateId => _undo.Last?.Value.StateId ?? _baseStateId;

    public HistoryEntry Record(
        IEditCommand command, SelectionRange before, SelectionRange after,
        IReadOnlyList<Marker>? markersBefore = null, IReadOnlyList<Marker>? markersAfter = null)
    {
        var entry = new HistoryEntry(command, before, after, _nextStateId++, markersBefore, markersAfter);
        _undo.AddLast(entry);
        _redo.Clear();

        while (_undo.Count > Capacity)
        {
            // Der älteste Schritt fällt weg; sein Ergebniszustand wird zum neuen Ausgangszustand.
            _baseStateId = _undo.First!.Value.StateId;
            _undo.RemoveFirst();
        }

        return entry;
    }

    public bool TryUndo(out HistoryEntry entry)
    {
        if (_undo.Last is null)
        {
            entry = null!;
            return false;
        }

        entry = _undo.Last.Value;
        _undo.RemoveLast();
        _redo.Push(entry);
        return true;
    }

    public bool TryRedo(out HistoryEntry entry)
    {
        if (!_redo.TryPop(out entry!))
        {
            return false;
        }

        _undo.AddLast(entry);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _baseStateId = _nextStateId++;
    }
}
