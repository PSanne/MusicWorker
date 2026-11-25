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

    /// <summary>Marker der Datei, nach Position sortiert. Sie wandern bei Schnitten mit und sind rückgängig machbar.</summary>
    public IReadOnlyList<Marker> Markers { get; private set; } = MarkerList.Empty;

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

    /// <summary>Lädt neues Audio (optional mit Markern) und leert den Verlauf.</summary>
    public void Load(AudioBuffer buffer, IReadOnlyList<Marker>? markers = null)
    {
        Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        Markers = markers is null
            ? MarkerList.Empty
            : MarkerList.Sorted(markers.Where(m => m.Frame >= 0 && m.Frame <= buffer.FrameCount));
        _history.Clear();
        _savedStateId = _history.CurrentStateId;
        _selection = default;
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Kennzeichnet das Dokument als ungespeichert (z. B. nach einer Wiederherstellung).</summary>
    public void MarkUnsaved()
    {
        _savedStateId = -1;
        Changed?.Invoke(this, EventArgs.Empty);
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

    /// <summary>Bereich, auf den ein Effekt wirkt: die Auswahl – oder alles, wenn nichts markiert ist.</summary>
    public SelectionRange EffectRange => HasSelection ? _selection : new SelectionRange(0, Buffer.FrameCount);

    /// <summary>Wendet einen Effekt auf die Auswahl an – oder auf alles, wenn nichts markiert ist.</summary>
    public void ApplyEffect(IAudioEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        RequireAudio();
        var range = EffectRange;
        var processed = effect.Apply(Buffer.Slice(range.Start, range.Length));
        ApplyEffectResult(effect.Name, range, processed);
    }

    /// <summary>
    /// Übernimmt ein bereits berechnetes Effekt-Ergebnis für <paramref name="range"/>.
    /// So kann die (evtl. langsame) Berechnung im Hintergrund laufen, während der Editor
    /// selbst nur im UI-Thread verändert wird.
    /// </summary>
    public void ApplyEffectResult(string description, SelectionRange range, AudioBuffer processed)
    {
        ArgumentNullException.ThrowIfNull(processed);
        RequireAudio();
        if (range.End > Buffer.FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(range), "Der Bereich liegt außerhalb des Audios.");
        }

        bool wasSelection = HasSelection && range == _selection;
        var selectionAfter = wasSelection
            ? new SelectionRange(range.Start, range.Start + processed.FrameCount)
            : _selection;

        Execute(new ReplaceRangeCommand(description, range.Start, range.Length, processed), selectionAfter);
    }

    /// <summary>
    /// Übernimmt einen komplett neu berechneten Puffer (z. B. nach kanalweiser Tempoänderung,
    /// bei der sich die Gesamtlänge ändern kann) als einen rückgängig machbaren Schritt.
    /// </summary>
    public void ApplyBufferResult(string description, AudioBuffer newBuffer, SelectionRange selectionAfter)
    {
        ArgumentNullException.ThrowIfNull(newBuffer);
        RequireAudio();
        Execute(new ReplaceBufferCommand(description, newBuffer), selectionAfter);
    }

    /// <summary>
    /// Macht aus einer Mehrkanal-Datei eine Mono-Datei, die nur den angegebenen Kanal enthält
    /// (0 = links, 1 = rechts). Rückgängig machbar.
    /// </summary>
    public void KeepChannel(int channel)
    {
        RequireAudio();
        if (Buffer.Channels < 2)
        {
            throw new InvalidOperationException("Das Audio hat nur einen Kanal.");
        }

        string description = channel switch
        {
            0 => "Nur linken Kanal behalten",
            1 => "Nur rechten Kanal behalten",
            _ => $"Nur Kanal {channel + 1} behalten",
        };
        Execute(new ReplaceBufferCommand(description, Buffer.GetChannel(channel)), _selection);
    }

    /// <summary>Fügt am Cursor (Anfang der Auswahl) Stille ein und markiert sie.</summary>
    public void InsertSilence(TimeSpan duration)
    {
        RequireAudio();
        int frames = (int)Math.Round(duration.TotalSeconds * Buffer.SampleRate);
        if (frames <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Die Dauer muss größer als 0 sein.");
        }

        int position = _selection.Start;
        var silence = AudioBuffer.CreateSilence(Buffer.SampleRate, Buffer.Channels, frames);
        Execute(
            new ReplaceRangeCommand("Stille einfügen", position, 0, silence),
            new SelectionRange(position, position + frames));
    }

    // ───────────── Marker ─────────────

    /// <summary>Setzt einen Marker; liefert seinen Index in <see cref="Markers"/>.</summary>
    public int AddMarker(long frame, string? name = null)
    {
        RequireAudio();
        var marker = new Marker(
            string.IsNullOrWhiteSpace(name) ? MarkerList.NextName(Markers) : name.Trim(),
            Math.Clamp(frame, 0, Buffer.FrameCount));
        ExecuteMarkers("Marker setzen", MarkerList.Add(Markers, marker));
        return IndexOf(marker);
    }

    public void RenameMarker(int index, string name)
    {
        RequireMarker(index);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == Markers[index].Name)
        {
            return;
        }

        ExecuteMarkers("Marker umbenennen", MarkerList.ReplaceAt(Markers, index, Markers[index] with { Name = name.Trim() }));
    }

    /// <summary>Verschiebt einen Marker; liefert seinen neuen Index.</summary>
    public int MoveMarker(int index, long frame)
    {
        RequireMarker(index);
        var moved = Markers[index] with { Frame = Math.Clamp(frame, 0, Buffer.FrameCount) };
        if (moved.Frame == Markers[index].Frame)
        {
            return index;
        }

        ExecuteMarkers("Marker verschieben", MarkerList.ReplaceAt(Markers, index, moved));
        return IndexOf(moved);
    }

    public void RemoveMarker(int index)
    {
        RequireMarker(index);
        ExecuteMarkers("Marker löschen", MarkerList.RemoveAt(Markers, index));
    }

    public void Undo()
    {
        if (!_history.TryUndo(out var entry))
        {
            return;
        }

        Buffer = entry.Command.Revert(Buffer);
        Markers = entry.MarkersBefore ?? Markers;
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
        Markers = entry.MarkersAfter ?? Markers;
        _selection = entry.SelectionAfter.Clamp(Buffer.FrameCount);
        Changed?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Execute(IEditCommand command, SelectionRange selectionAfter)
    {
        var before = _selection;
        var markersBefore = Markers;
        long lengthBefore = Buffer.FrameCount;
        Buffer = command.Apply(Buffer);
        Markers = command.MapMarkers(markersBefore, lengthBefore);
        _history.Record(command, before, selectionAfter, markersBefore, Markers);
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

    private void RequireAudio()
    {
        if (!HasAudio)
        {
            throw new InvalidOperationException("Es ist kein Audio geladen.");
        }
    }

    private void ExecuteMarkers(string description, IReadOnlyList<Marker> markers) =>
        Execute(new MarkerEditCommand(description, markers), _selection);

    private int IndexOf(Marker marker)
    {
        for (int i = 0; i < Markers.Count; i++)
        {
            if (ReferenceEquals(Markers[i], marker))
            {
                return i;
            }
        }

        return -1;
    }

    private void RequireMarker(int index)
    {
        if (index < 0 || index >= Markers.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Diesen Marker gibt es nicht.");
        }
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

    /// <summary>Ändert nur die Marker – das Audio bleibt gleich.</summary>
    private sealed class MarkerEditCommand(string description, IReadOnlyList<Marker> markers) : IEditCommand
    {
        public string Description { get; } = description;

        public AudioBuffer Apply(AudioBuffer buffer) => buffer;

        public AudioBuffer Revert(AudioBuffer buffer) => buffer;

        public IReadOnlyList<Marker> MapMarkers(IReadOnlyList<Marker> current, long lengthBefore) => markers;
    }
}
