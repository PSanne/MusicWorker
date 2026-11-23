using MusicWorker.Core.Audio;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Editing;

/// <summary>
/// Das „Dokument“ des Audio-Editors: aktueller Puffer, Auswahl, Zwischenablage und Verlauf.
/// Alle Bearbeitungen laufen über diese Klasse und sind damit rückgängig machbar.
/// </summary>
public sealed class AudioEditor
{
    private readonly EditHistory _history;
    private SelectionRange _selection;
    private long _savedStateId;

    public AudioEditor(int undoCapacity = 50)
    {
        _history = new EditHistory(undoCapacity);
        Buffer = AudioBuffer.CreateEmpty(44100, 2);
        _savedStateId = _history.CurrentStateId;
    }

    /// <summary>Puffer oder Verlauf haben sich geändert (auch: Gespeichert-Status, Zwischenablage).</summary>
    public event EventHandler? Changed;

    /// <summary>Die Auswahl hat sich geändert.</summary>
    public event EventHandler? SelectionChanged;

    public AudioBuffer Buffer { get; private set; }

    public AudioBuffer? Clipboard { get; private set; }

    /// <summary>Aktuelle Auswahl; wird automatisch auf die Pufferlänge begrenzt.</summary>
    public SelectionRange Selection
    {
        get => _selection;
        set => SetSelection(value);
    }

    public bool HasAudio => !Buffer.IsEmpty;

    public bool HasSelection => !_selection.IsEmpty;

    public bool CanPaste => Clipboard is { IsEmpty: false } clip && clip.HasSameFormat(Buffer);

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public string? UndoDescription => _history.UndoDescription;

    public string? RedoDescription => _history.RedoDescription;

    /// <summary>True, wenn es seit dem Laden bzw. letzten Speichern Änderungen gibt.</summary>
    public bool IsModified => _history.CurrentStateId != _savedStateId;

    /// <summary>Lädt neues Audio und leert den Verlauf.</summary>
    public void Load(AudioBuffer buffer)
    {
        Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _history.Clear();
        _savedStateId = _history.CurrentStateId;
        _selection = default;
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkSaved()
    {
        _savedStateId = _history.CurrentStateId;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAll() => Selection = new SelectionRange(0, Buffer.FrameCount);

    public void Copy()
    {
        RequireSelection();
        Clipboard = Buffer.Slice(_selection.Start, _selection.Length);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Cut()
    {
        RequireSelection();
        Clipboard = Buffer.Slice(_selection.Start, _selection.Length);
        Execute(
            new ReplaceRangeCommand("Ausschneiden", _selection.Start, _selection.Length, EmptyLike(Buffer)),
            SelectionRange.Caret(_selection.Start));
    }

    public void Delete()
    {
        RequireSelection();
        Execute(
            new ReplaceRangeCommand("Löschen", _selection.Start, _selection.Length, EmptyLike(Buffer)),
            SelectionRange.Caret(_selection.Start));
    }

    /// <summary>
    /// Fügt die Zwischenablage am Cursor ein. Ist etwas markiert, wird die Markierung ersetzt.
    /// Danach ist der eingefügte Bereich markiert.
    /// </summary>
    public void Paste()
    {
        if (!CanPaste)
        {
            throw new InvalidOperationException("Die Zwischenablage ist leer oder hat ein anderes Audioformat.");
        }

        var clip = Clipboard!;
        Execute(
            new ReplaceRangeCommand("Einfügen", _selection.Start, _selection.Length, clip),
            new SelectionRange(_selection.Start, _selection.Start + clip.FrameCount));
    }

    /// <summary>Zuschneiden: behält nur die Auswahl und entfernt alles davor und danach.</summary>
    public void Trim()
    {
        RequireSelection();
        var (start, end) = (_selection.Start, _selection.End);
        var command = new CompositeEditCommand(
            "Zuschneiden",
            new ReplaceRangeCommand("Ende entfernen", end, Buffer.FrameCount - end, EmptyLike(Buffer)),
            new ReplaceRangeCommand("Anfang entfernen", 0, start, EmptyLike(Buffer)));
        Execute(command, new SelectionRange(0, end - start));
    }

    /// <summary>Wendet einen Effekt auf die Auswahl an – oder auf alles, wenn nichts markiert ist.</summary>
    public void ApplyEffect(IAudioEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (!HasAudio)
        {
            throw new InvalidOperationException("Es ist kein Audio geladen.");
        }

        bool wholeFile = !HasSelection;
        var range = wholeFile ? new SelectionRange(0, Buffer.FrameCount) : _selection;
        var processed = effect.Apply(Buffer.Slice(range.Start, range.Length));
        var selectionAfter = wholeFile
            ? _selection
            : new SelectionRange(range.Start, range.Start + processed.FrameCount);

        Execute(new ReplaceRangeCommand(effect.Name, range.Start, range.Length, processed), selectionAfter);
    }

    public void Undo()
    {
        if (!_history.TryUndo(out var entry))
        {
            return;
        }

        Buffer = entry.Command.Revert(Buffer);
        _selection = entry.SelectionBefore.Clamp(Buffer.FrameCount);
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!_history.TryRedo(out var entry))
        {
            return;
        }

        Buffer = entry.Command.Apply(Buffer);
        _selection = entry.SelectionAfter.Clamp(Buffer.FrameCount);
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Execute(IEditCommand command, SelectionRange selectionAfter)
    {
        var before = _selection;
        Buffer = command.Apply(Buffer);
        _history.Record(command, before, selectionAfter);
        _selection = selectionAfter.Clamp(Buffer.FrameCount);
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetSelection(SelectionRange value)
    {
        var clamped = value.Clamp(Buffer.FrameCount);
        if (clamped == _selection)
        {
            return;
        }

        _selection = clamped;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RequireSelection()
    {
        if (!HasSelection)
        {
            throw new InvalidOperationException("Bitte zuerst einen Bereich markieren.");
        }
    }

    private static AudioBuffer EmptyLike(AudioBuffer buffer) =>
        AudioBuffer.CreateEmpty(buffer.SampleRate, buffer.Channels);
}
