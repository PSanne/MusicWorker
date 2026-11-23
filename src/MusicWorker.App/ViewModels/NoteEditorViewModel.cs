using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Audio;
using MusicWorker.App.Services;
using MusicWorker.Core.Notes;
using MusicWorker.Core.Synthesis;

namespace MusicWorker.App.ViewModels;

/// <summary>Was die Piano-Roll vom Editor möchte.</summary>
public enum PianoRollAction
{
    /// <summary>Neue Note an <see cref="PianoRollEdit.Note"/> (Beginn wird gerastert); danach kann ihre Länge gezogen werden.</summary>
    Add,

    /// <summary>Noten auswählen (<see cref="PianoRollEdit.Additive"/> = zur Auswahl hinzufügen/umschalten).</summary>
    Select,

    /// <summary>Ziehen beginnt an <see cref="PianoRollEdit.Note"/> (Anker).</summary>
    BeginDrag,

    /// <summary>Auswahl um DeltaTicks/DeltaPitch verschieben (relativ zum Zugbeginn).</summary>
    Move,

    /// <summary>Länge der Auswahl um DeltaTicks ändern (relativ zum Zugbeginn).</summary>
    Resize,

    /// <summary>Anschlagstärke (<see cref="PianoRollEdit.Value"/>) für die Note bzw. Auswahl setzen.</summary>
    Velocity,

    EndDrag,

    Delete,

    /// <summary>Ton vorhören (Taste links gedrückt).</summary>
    Preview,

    PreviewEnd,

    /// <summary>Abspielposition/Eingabe-Cursor an <see cref="PianoRollEdit.Tick"/> setzen.</summary>
    Seek,
}

/// <summary>Eine Bearbeitung aus der Piano-Roll.</summary>
public sealed record PianoRollEdit(
    PianoRollAction Action,
    Note Note = default,
    IReadOnlyList<Note>? Notes = null,
    bool Additive = false,
    long DeltaTicks = 0,
    int DeltaPitch = 0,
    int Value = 0,
    long Tick = 0);

/// <summary>Auswahl eines Rasters, z. B. „1/16“.</summary>
public sealed record GridChoice(int Division, string Name);

/// <summary>Ein Klang aus einer SoundFont-Datei.</summary>
public sealed record PresetChoice(int Bank, int Preset, string Name)
{
    public override string ToString() => Bank == 128 ? $"Drums: {Name}" : $"{Preset + 1:000} {Name}";
}

/// <summary>
/// Noten-Editor: bearbeitet die Noten eines Notenclips aus dem Mischpult (Piano-Roll, Anschlag,
/// Quantisieren, Humanisieren, Transponieren, Längen, Stimmaufteilung), spielt Töne über die
/// Bildschirmtastatur bzw. ein MIDI-Keyboard und nimmt sie auf Wunsch schrittweise auf.
/// </summary>
public sealed partial class NoteEditorViewModel : ObservableObject, IDisposable
{
    private const int MaxUndo = 100;

    private readonly MixerViewModel _mixer;
    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;
    private readonly LiveSynthService _live = new();
    private readonly MidiInputService _midi = new();
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly List<(IReadOnlyList<Note> Notes, long Length)> _undo = [];
    private readonly Stack<(IReadOnlyList<Note> Notes, long Length)> _redo = new();
    private readonly HashSet<int> _held = [];
    private IReadOnlyList<Note> _clipboard = [];

    // Ziehen in der Piano-Roll
    private IReadOnlyList<Note>? _dragBase;
    private ImmutableHashSet<Note> _dragSelection = [];
    private Note _dragAnchor;
    private bool _dragUndoRecorded;
    private int _previewPitch = -1;
    private bool _mixerUndoRecorded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClip), nameof(Title), nameof(Track))]
    private ClipViewModel? _clip;

    [ObservableProperty]
    private IReadOnlyList<Note> _notes = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionText))]
    private ImmutableHashSet<Note> _selection = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LengthBarsText))]
    private long _lengthTicks = MusicalTime.TicksPerBar(4) * 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridTicks))]
    private GridChoice _grid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GridTicks))]
    private bool _isTriplet;

    [ObservableProperty]
    private bool _isSnapEnabled = true;

    /// <summary>Länge neuer Noten (die zuletzt gezogene Länge wird übernommen).</summary>
    [ObservableProperty]
    private long _newNoteLength = MusicalTime.TicksPerQuarter;

    [ObservableProperty]
    private int _defaultVelocity = 100;

    [ObservableProperty]
    private long _playheadTick;

    /// <summary>Schritt-Aufnahme: Gespielte Töne werden am Cursor eingefügt, danach rückt er um einen Rasterschritt vor.</summary>
    [ObservableProperty]
    private bool _isStepRecording;

    [ObservableProperty]
    private long _stepCursorTick;

    /// <summary>Tiefste Oktave der Bildschirmtastatur (C dieser Oktave).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OctaveText), nameof(LowestKey))]
    private int _keyboardOctave = 3;

    [ObservableProperty]
    private ImmutableHashSet<int> _pressedKeys = [];

    [ObservableProperty]
    private int _version;

    [ObservableProperty]
    private BuiltInInstrument _selectedBuiltIn = BuiltInInstrument.Piano;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSoundFont), nameof(SoundFontName))]
    private string? _soundFontPath;

    [ObservableProperty]
    private IReadOnlyList<PresetChoice> _presets = [];

    [ObservableProperty]
    private PresetChoice? _selectedPreset;

    [ObservableProperty]
    private IReadOnlyList<string> _midiInputs = [];

    [ObservableProperty]
    private string? _selectedMidiInput;

    private bool _syncingInstrument;

    public NoteEditorViewModel(MixerViewModel mixer, IDialogService dialogs, SettingsService settings)
    {
        _mixer = mixer;
        _dialogs = dialogs;
        _settings = settings;
        _grid = Grids[2];
        _mixer.PropertyChanged += OnMixerPropertyChanged;
        _mixer.Tracks.CollectionChanged += OnMixerTracksChanged;
        _midi.NoteOn += (pitch, velocity) => Post(() => KeyDown(pitch, velocity));
        _midi.NoteOff += pitch => Post(() => KeyUp(pitch));
        RefreshMidiInputs();
    }

    public static IReadOnlyList<GridChoice> Grids { get; } =
    [
        new(4, "1/4"), new(8, "1/8"), new(16, "1/16"), new(32, "1/32"),
    ];

    public IReadOnlyList<GridChoice> GridChoices => Grids;

    public IReadOnlyList<BuiltInInstrument> BuiltInChoices => Instruments.All;

    public bool HasClip => Clip is not null;

    public TrackViewModel? Track => Clip?.Track;

    public long GridTicks => MusicalTime.GridTicks(Grid.Division, IsTriplet);

    public int BeatsPerBar => _mixer.BeatsPerBar;

    public bool IsDrums => Track?.Instrument?.IsDrums == true;

    public bool HasSoundFont => !string.IsNullOrEmpty(SoundFontPath);

    public string SoundFontName => HasSoundFont ? Path.GetFileNameWithoutExtension(SoundFontPath)! : string.Empty;

    public int LowestKey => (KeyboardOctave + 1) * 12;

    public string OctaveText => $"C{KeyboardOctave} – C{KeyboardOctave + 3}";

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string Title => Clip is { } clip
        ? $"„{clip.Track.Name}“ ab Takt {_mixer.FramesToTicks(clip.StartFrame) / MusicalTime.TicksPerBar(_mixer.BeatsPerBar) + 1}"
        : "Kein Notenclip geöffnet";

    public string LengthBarsText
    {
        get => (LengthTicks / (double)MusicalTime.TicksPerBar(_mixer.BeatsPerBar)).ToString("0.##", CultureInfo.CurrentCulture);
        set
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double bars) && Clip is not null)
            {
                long length = (long)Math.Round(Math.Clamp(bars, 0.25, 999) * MusicalTime.TicksPerBar(_mixer.BeatsPerBar));
                if (length != LengthTicks)
                {
                    PushUndo();
                    Commit(Notes, length);
                }
            }

            OnPropertyChanged();
        }
    }

    public string SelectionText => Selection.Count == 0
        ? $"{Notes.Count} Noten"
        : $"{Selection.Count} von {Notes.Count} Noten gewählt";

    // ───────────── Öffnen ─────────────

    /// <summary>Öffnet einen Notenclip aus dem Mischpult.</summary>
    public void Open(ClipViewModel clip)
    {
        if (!clip.IsNoteClip)
        {
            return;
        }

        Clip = clip;
        _undo.Clear();
        _redo.Clear();
        _mixerUndoRecorded = false;
        Notes = clip.Notes!;
        LengthTicks = clip.LengthTicks;
        Selection = [];
        StepCursorTick = 0;
        SyncInstrumentFromTrack();
        UpdatePlayhead();
        NotifyUndo();
        Version++;
    }

    /// <summary>Schließt den Editor (z. B. wenn der Clip gelöscht wurde).</summary>
    public void Close()
    {
        Clip = null;
        Notes = [];
        Selection = [];
        Version++;
    }

    // ───────────── Piano-Roll ─────────────

    [RelayCommand]
    private void Edit(PianoRollEdit? edit)
    {
        if (edit is null || Clip is null)
        {
            if (edit is { Action: PianoRollAction.Preview })
            {
                Preview(edit.Note.Pitch);
            }
            else if (edit is { Action: PianoRollAction.PreviewEnd })
            {
                EndPreview();
            }

            return;
        }

        switch (edit.Action)
        {
            case PianoRollAction.Add:
                AddNote(edit.Note);
                break;
            case PianoRollAction.Select:
                Select(edit.Notes ?? [], edit.Additive);
                break;
            case PianoRollAction.BeginDrag:
                BeginDrag(edit.Note, edit.Additive);
                break;
            case PianoRollAction.Move:
                DragMove(edit.DeltaTicks, edit.DeltaPitch);
                break;
            case PianoRollAction.Resize:
                DragResize(edit.DeltaTicks);
                break;
            case PianoRollAction.Velocity:
                SetVelocity(edit.Note, edit.Value);
                break;
            case PianoRollAction.EndDrag:
                EndDrag();
                break;
            case PianoRollAction.Delete:
                DeleteNotes(edit.Notes ?? (edit.Note == default ? [] : [edit.Note]));
                break;
            case PianoRollAction.Preview:
                Preview(edit.Note.Pitch);
                break;
            case PianoRollAction.PreviewEnd:
                EndPreview();
                break;
            case PianoRollAction.Seek:
                StepCursorTick = Math.Max(0, Snap(edit.Tick));
                _mixer.Seek(Clip.StartFrame + _mixer.TicksToFrames(StepCursorTick));
                break;
        }
    }

    private void AddNote(Note note)
    {
        long start = Math.Max(0, IsSnapEnabled ? SnapDown(note.Start) : note.Start);
        var added = new Note(Math.Clamp(note.Pitch, 0, 127), start, Math.Max(1, NewNoteLength), DefaultVelocity);
        PushUndo();
        Commit(NoteTools.Sorted(Notes.Append(added)), Math.Max(LengthTicks, added.End));
        Selection = [added];
        _dragBase = Notes;
        _dragSelection = Selection;
        _dragAnchor = added;
        _dragUndoRecorded = true; // der Rückgängig-Schritt für das Hinzufügen gilt auch für das Ziehen der Länge
        Preview(added.Pitch);
    }

    private void Select(IReadOnlyList<Note> notes, bool additive)
    {
        if (!additive)
        {
            Selection = [.. notes];
            return;
        }

        var builder = Selection.ToBuilder();
        foreach (var note in notes)
        {
            if (!builder.Remove(note))
            {
                builder.Add(note);
            }
        }

        Selection = builder.ToImmutable();
    }

    private void BeginDrag(Note anchor, bool additive)
    {
        if (!Selection.Contains(anchor))
        {
            Selection = additive ? Selection.Add(anchor) : [anchor];
        }

        _dragBase = Notes;
        _dragSelection = Selection;
        _dragAnchor = anchor;
        _dragUndoRecorded = false;
        Preview(anchor.Pitch);
    }

    private void DragMove(long deltaTicks, int deltaPitch)
    {
        if (_dragBase is null)
        {
            return;
        }

        long target = _dragAnchor.Start + deltaTicks;
        long delta = (IsSnapEnabled ? Snap(target) : target) - _dragAnchor.Start;
        delta = Math.Max(delta, -_dragSelection.Select(n => n.Start).DefaultIfEmpty(0).Min());
        int pitch = Math.Clamp(deltaPitch, -_dragSelection.Min(n => n.Pitch), 127 - _dragSelection.Max(n => n.Pitch));
        var moved = _dragSelection.Select(n => n with { Start = n.Start + delta, Pitch = n.Pitch + pitch }).ToArray();
        ApplyDrag(moved);
        if (_dragAnchor.Pitch + pitch != _previewPitch)
        {
            Preview(_dragAnchor.Pitch + pitch);
        }
    }

    private void DragResize(long deltaTicks)
    {
        if (_dragBase is null)
        {
            return;
        }

        long end = _dragAnchor.End + deltaTicks;
        long delta = (IsSnapEnabled ? Math.Max(_dragAnchor.Start + GridTicks, Snap(end)) : end) - _dragAnchor.End;
        long minimum = Math.Max(1, GridTicks / 4);
        var resized = _dragSelection.Select(n => n with { Length = Math.Max(minimum, n.Length + delta) }).ToArray();
        ApplyDrag(resized);
        NewNoteLength = Math.Max(minimum, _dragAnchor.Length + delta); // neue Noten bekommen die zuletzt gezogene Länge
    }

    private void ApplyDrag(IReadOnlyList<Note> changed)
    {
        if (!_dragUndoRecorded)
        {
            PushUndo(_dragBase!, LengthTicks);
            _dragUndoRecorded = true;
        }

        var rest = _dragBase!.Where(n => !_dragSelection.Contains(n));
        Commit(NoteTools.Sorted(rest.Concat(changed)), Math.Max(LengthTicks, NoteTools.EndOf(changed)));
        Selection = [.. changed];
    }

    private void SetVelocity(Note note, int velocity)
    {
        velocity = Math.Clamp(velocity, 1, 127);
        var targets = Selection.Contains(note) ? Selection : [note];
        if (_dragBase is null)
        {
            _dragBase = Notes;
            _dragSelection = targets;
            _dragAnchor = note;
            _dragUndoRecorded = false;
        }

        var changed = _dragSelection.Select(n => n with { Velocity = velocity }).ToArray();
        ApplyDrag(changed);
        DefaultVelocity = velocity;
    }

    private void EndDrag()
    {
        _dragBase = null;
        _dragSelection = [];
        EndPreview();
    }

    private void DeleteNotes(IReadOnlyList<Note> notes)
    {
        var remove = notes.ToHashSet();
        if (remove.Count == 0 || !Notes.Any(remove.Contains))
        {
            return;
        }

        PushUndo();
        Commit(Notes.Where(n => !remove.Contains(n)).ToArray(), LengthTicks);
        Selection = Selection.Except(remove);
    }

    // ───────────── Befehle (wirken auf die Auswahl oder – ohne Auswahl – auf alle Noten) ─────────────

    [RelayCommand]
    private void SelectAll() => Selection = [.. Notes];

    [RelayCommand]
    public void DeleteSelected() => DeleteNotes(Selection.ToArray());

    [RelayCommand]
    public void Copy()
    {
        var source = Selection.Count > 0 ? Selection.ToArray() : [];
        if (source.Length == 0)
        {
            return;
        }

        long first = source.Min(n => n.Start);
        _clipboard = NoteTools.Move(source, -first);
        _mixer.StatusText = $"{source.Length} Noten kopiert – Strg+V fügt sie am Cursor ein.";
    }

    [RelayCommand]
    public void Cut()
    {
        Copy();
        DeleteSelected();
    }

    [RelayCommand]
    public void Paste()
    {
        if (_clipboard.Count == 0 || Clip is null)
        {
            return;
        }

        var pasted = NoteTools.Move(_clipboard, StepCursorTick);
        PushUndo();
        Commit(NoteTools.Sorted(Notes.Concat(pasted)), Math.Max(LengthTicks, NoteTools.EndOf(pasted)));
        Selection = [.. pasted];
        StepCursorTick = NoteTools.EndOf(pasted);
    }

    [RelayCommand]
    private void Quantize()
    {
        var request = new ValueRequest("Quantisieren", $"Noten auf das Raster ({Grid.Name}{(IsTriplet ? " Triole" : string.Empty)}) rücken – Stärke:",
            "100 % = genau aufs Raster. Kleinere Werte rücken nur ein Stück, das klingt weniger maschinell.",
            Minimum: 10, Maximum: 100, Value: 100, SmallChange: 10, Unit: "%");
        if (_dialogs.AskValue(request) is { } strength)
        {
            Apply(notes => NoteTools.Quantize(notes, GridTicks, strength / 100), "quantisiert");
        }
    }

    [RelayCommand]
    private void Humanize()
    {
        var request = new ValueRequest("Humanisieren", "Wie stark sollen Timing und Anschlag zufällig schwanken?",
            "Kleine Abweichungen lassen gezeichnete Noten lebendiger klingen – wie von Hand gespielt.",
            Minimum: 5, Maximum: 100, Value: 30, SmallChange: 5, Unit: "%");
        if (_dialogs.AskValue(request) is { } amount)
        {
            long timing = (long)(MusicalTime.TicksPerQuarter / 8.0 * amount / 100); // bis zu 1/32
            int velocity = (int)Math.Round(20 * amount / 100);
            Apply(notes => NoteTools.Humanize(notes, timing, velocity, Environment.TickCount), "humanisiert");
        }
    }

    [RelayCommand]
    private void Transpose(string? semitones)
    {
        if (int.TryParse(semitones, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            Apply(notes => NoteTools.Transpose(notes, value), $"um {value:+0;-0} Halbtöne transponiert");
        }
    }

    [RelayCommand]
    private void TransposeBy()
    {
        var request = new ValueRequest("Transponieren", "Um wie viele Halbtöne verschieben?",
            "12 = eine Oktave höher, -12 = eine Oktave tiefer, 7 = eine Quinte höher.",
            Minimum: -36, Maximum: 36, Value: 0, SmallChange: 1, Unit: "Halbtöne");
        if (_dialogs.AskValue(request) is { } value && (int)Math.Round(value) != 0)
        {
            Transpose(((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture));
        }
    }

    [RelayCommand]
    private void ScaleLength(string? factor)
    {
        if (double.TryParse(factor, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            Apply(notes => NoteTools.ScaleLengths(notes, value), value < 1 ? "verkürzt" : "verlängert");
        }
    }

    [RelayCommand]
    private void LengthToGrid() => Apply(notes => NoteTools.SetLengths(notes, GridTicks), $"auf {Grid.Name} gesetzt");

    [RelayCommand]
    private void Legato() => Apply(NoteTools.Legato, "gebunden (Legato)");

    [RelayCommand]
    private void SetVelocityTo()
    {
        var request = new ValueRequest("Anschlagstärke", "Anschlagstärke (1 = ganz leise, 127 = maximal):",
            "Gilt für die gewählten Noten (ohne Auswahl für alle). Einzelne Noten änderst du im Anschlag-Feld unter den Noten.",
            Minimum: 1, Maximum: 127, Value: DefaultVelocity, SmallChange: 5, Unit: string.Empty);
        if (_dialogs.AskValue(request) is { } value)
        {
            Apply(notes => NoteTools.SetVelocity(notes, (int)Math.Round(value)), "im Anschlag geändert");
        }
    }

    [RelayCommand]
    private void Crescendo(string? direction)
    {
        bool up = direction != "down";
        Apply(notes => NoteTools.VelocityRamp(notes, up ? 45 : 115, up ? 115 : 45), up ? "lauter werdend (Crescendo)" : "leiser werdend (Decrescendo)");
    }

    /// <summary>Stimmaufteilung: jede Stimme wird eine eigene Notenspur (die Quelle wird stummgeschaltet).</summary>
    [RelayCommand]
    private void SplitVoices()
    {
        if (Clip is not { } clip || Notes.Count == 0)
        {
            return;
        }

        var request = new ValueRequest("Stimmen aufteilen", "Auf wie viele Stimmen (Spuren) sollen die Noten verteilt werden?",
            "Gleichzeitige Noten werden von oben nach unten verteilt: Stimme 1 = höchste Note (Melodie), die letzte Stimme sammelt die tiefsten (Bass).",
            Minimum: 2, Maximum: 6, Value: 2, SmallChange: 1, Unit: "Stimmen");
        if (_dialogs.AskValue(request) is not { } value)
        {
            return;
        }

        int voices = (int)Math.Round(value);
        var split = NoteTools.SplitVoices(Notes, voices);
        _mixer.SplitVoicesToTracks(clip, split, voices);
    }

    [RelayCommand]
    public void Undo()
    {
        if (_undo.Count == 0 || Clip is null)
        {
            return;
        }

        _redo.Push((Notes, LengthTicks));
        var (notes, length) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Commit(notes, length);
        Selection = Selection.Intersect(notes);
        NotifyUndo();
    }

    [RelayCommand]
    public void Redo()
    {
        if (_redo.Count == 0 || Clip is null)
        {
            return;
        }

        _undo.Add((Notes, LengthTicks));
        var (notes, length) = _redo.Pop();
        Commit(notes, length);
        NotifyUndo();
    }

    // ───────────── Tastatur & Klang ─────────────

    /// <summary>Taste gedrückt (Bildschirmtastatur, Computertastatur oder MIDI-Keyboard).</summary>
    public void KeyDown(int pitch, int velocity = 100)
    {
        if (pitch is < 0 or > 127 || !_held.Add(pitch))
        {
            return;
        }

        PressedKeys = PressedKeys.Add(pitch);
        _ = _live.SetInstrumentAsync(CurrentInstrument());
        _live.NoteOn(pitch, velocity);
        if (IsStepRecording && Clip is not null)
        {
            var note = new Note(pitch, StepCursorTick, Math.Max(1, GridTicks), velocity);
            if (_held.Count == 1)
            {
                PushUndo(); // ein Rückgängig-Schritt pro Schritt (auch für Akkorde)
            }

            Commit(NoteTools.Sorted(Notes.Append(note)), Math.Max(LengthTicks, note.End));
            Selection = _held.Count == 1 ? [note] : Selection.Add(note);
        }
    }

    public void KeyUp(int pitch)
    {
        if (!_held.Remove(pitch))
        {
            return;
        }

        PressedKeys = PressedKeys.Remove(pitch);
        _live.NoteOff(pitch);
        if (IsStepRecording && _held.Count == 0 && Clip is not null)
        {
            StepCursorTick += Math.Max(1, GridTicks); // Akkord fertig → einen Schritt weiter
        }
    }

    /// <summary>Alle gehaltenen Töne beenden (z. B. Fenster verliert den Fokus).</summary>
    public void ReleaseAllKeys()
    {
        foreach (int pitch in _held.ToArray())
        {
            KeyUp(pitch);
        }

        _live.AllNotesOff();
    }

    /// <summary>Computertastatur als Klaviatur (QWERTZ): Y–M = untere Oktave, Q–U = obere. Liefert die Tonhöhe oder -1.</summary>
    public int PitchForKey(string key)
    {
        int offset = key switch
        {
            "Y" => 0, "S" => 1, "X" => 2, "D" => 3, "C" => 4, "V" => 5, "G" => 6, "B" => 7, "H" => 8, "N" => 9, "J" => 10, "M" => 11, "OemComma" => 12,
            "Q" => 12, "D2" => 13, "W" => 14, "D3" => 15, "E" => 16, "R" => 17, "D5" => 18, "T" => 19, "D6" => 20, "Z" => 21, "D7" => 22, "U" => 23,
            "I" => 24, "D9" => 25, "O" => 26, "D0" => 27, "P" => 28,
            _ => -1,
        };
        return offset < 0 ? -1 : Math.Clamp(LowestKey + offset, 0, 127);
    }

    [RelayCommand]
    private void OctaveDown() => KeyboardOctave = Math.Max(0, KeyboardOctave - 1);

    [RelayCommand]
    private void OctaveUp() => KeyboardOctave = Math.Min(7, KeyboardOctave + 1);

    [RelayCommand]
    private void PlayKey(int? pitch)
    {
        if (pitch is { } p)
        {
            KeyDown(p);
        }
    }

    [RelayCommand]
    private void ReleaseKey(int? pitch)
    {
        if (pitch is { } p)
        {
            KeyUp(p);
        }
    }

    [RelayCommand]
    private async Task LoadSoundFontAsync()
    {
        if (_dialogs.ShowOpenFileDialog("SoundFont-Dateien (*.sf2)|*.sf2|Alle Dateien (*.*)|*.*") is not { } path)
        {
            return;
        }

        try
        {
            _mixer.StatusText = $"Lade SoundFont „{Path.GetFileName(path)}“ …";
            var presets = await Task.Run(() => SoundFontLibrary.Presets(path));
            if (presets.Count == 0)
            {
                _dialogs.ShowError("Die SoundFont-Datei enthält keine Klänge.");
                return;
            }

            _settings.Settings.LastSoundFont = path;
            _settings.Save();
            _syncingInstrument = true;
            SoundFontPath = path;
            Presets = presets.Select(p => new PresetChoice(p.Bank, p.Preset, p.Name)).ToArray();
            _syncingInstrument = false;
            int program = Track?.Instrument is { } current ? Instruments.ToGeneralMidi(current.BuiltIn) : 0;
            SelectedPreset = IsDrums
                ? Presets.FirstOrDefault(p => p.Bank == 128) ?? Presets[0]
                : Presets.FirstOrDefault(p => p.Bank == 0 && p.Preset == program) ?? Presets[0];
            _mixer.StatusText = $"SoundFont geladen: {Presets.Count} Klänge – wähle rechts daneben den gewünschten Klang.";
        }
        catch (Exception ex)
        {
            _syncingInstrument = false;
            _dialogs.ShowError($"Die SoundFont-Datei konnte nicht geladen werden.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private void UseBuiltIn()
    {
        SoundFontPath = null;
        Presets = [];
        SelectedPreset = null;
        if (Track is { } track)
        {
            track.Instrument = new InstrumentSpec(SelectedBuiltIn);
        }
    }

    [RelayCommand]
    private void RefreshMidiInputs()
    {
        var devices = MidiInputService.Devices();
        MidiInputs = ["Kein MIDI-Eingang", .. devices];
        SelectedMidiInput ??= MidiInputs[0];
    }

    /// <summary>Wiedergabegerät für das Live-Spiel.</summary>
    public void SetOutputDevice(int deviceNumber) => _live.SetDevice(deviceNumber);

    public void Dispose()
    {
        _mixer.PropertyChanged -= OnMixerPropertyChanged;
        _mixer.Tracks.CollectionChanged -= OnMixerTracksChanged;
        _midi.Dispose();
        _live.Dispose();
    }

    partial void OnSelectedBuiltInChanged(BuiltInInstrument value)
    {
        if (!_syncingInstrument && Track is { } track)
        {
            SoundFontPath = null;
            Presets = [];
            track.Instrument = new InstrumentSpec(value);
            _ = _live.SetInstrumentAsync(track.Instrument);
            OnPropertyChanged(nameof(IsDrums));
        }
    }

    partial void OnSelectedPresetChanged(PresetChoice? value)
    {
        if (!_syncingInstrument && value is not null && Track is { } track && SoundFontPath is { } path)
        {
            track.Instrument = new InstrumentSpec(SelectedBuiltIn, path, value.Bank, value.Preset, value.Name);
            _ = _live.SetInstrumentAsync(track.Instrument);
            OnPropertyChanged(nameof(IsDrums));
            Version++;
        }
    }

    partial void OnSelectedMidiInputChanged(string? value)
    {
        int index = value is null ? -1 : MidiInputs.ToList().IndexOf(value) - 1;
        if (_midi.Open(index) is { } error)
        {
            _mixer.StatusText = $"Der MIDI-Eingang konnte nicht geöffnet werden: {error}";
        }
        else if (index >= 0)
        {
            _mixer.StatusText = $"MIDI-Eingang „{value}“ aktiv – gespielte Töne erklingen mit dem Instrument der Spur.";
        }
    }

    partial void OnGridChanged(GridChoice value) => NewNoteLength = GridTicks;

    partial void OnIsTripletChanged(bool value) => NewNoteLength = GridTicks;

    partial void OnSelectionChanged(ImmutableHashSet<Note> value) => Version++;

    partial void OnNotesChanged(IReadOnlyList<Note> value) => OnPropertyChanged(nameof(SelectionText));

    // ───────────── Hilfen ─────────────

    /// <summary>Wendet eine Bearbeitung auf die Auswahl (bzw. alle Noten) an.</summary>
    private void Apply(Func<IReadOnlyList<Note>, IReadOnlyList<Note>> operation, string done)
    {
        if (Clip is null || Notes.Count == 0)
        {
            return;
        }

        var targets = Selection.Count > 0 ? Notes.Where(Selection.Contains).ToArray() : Notes.ToArray();
        var changed = operation(targets);
        var rest = Selection.Count > 0 ? Notes.Where(n => !Selection.Contains(n)) : [];
        PushUndo();
        Commit(NoteTools.Sorted(rest.Concat(changed)), Math.Max(LengthTicks, NoteTools.EndOf(changed)));
        if (Selection.Count > 0)
        {
            Selection = [.. changed];
        }

        _mixer.StatusText = $"{targets.Length} Noten {done}.";
    }

    /// <summary>Neue Noten übernehmen: Clip im Mischpult aktualisieren (Klang wird neu berechnet).</summary>
    private void Commit(IReadOnlyList<Note> notes, long lengthTicks)
    {
        Notes = notes;
        LengthTicks = lengthTicks;
        if (Clip is { } clip)
        {
            _mixer.UpdateNoteClip(clip, notes, lengthTicks);
        }

        Version++;
    }

    private void PushUndo() => PushUndo(Notes, LengthTicks);

    private void PushUndo(IReadOnlyList<Note> notes, long length)
    {
        if (!_mixerUndoRecorded)
        {
            _mixer.RecordNoteUndo(); // im Mischpult lässt sich die ganze Notenbearbeitung als ein Schritt zurücknehmen
            _mixerUndoRecorded = true;
        }

        _undo.Add((notes, length));
        if (_undo.Count > MaxUndo)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        NotifyUndo();
    }

    private void NotifyUndo()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private long Snap(long tick) => (long)Math.Round(tick / (double)GridTicks) * GridTicks;

    private long SnapDown(long tick) => (long)Math.Floor(tick / (double)GridTicks) * GridTicks;

    private InstrumentSpec CurrentInstrument() => Track?.Instrument ?? new InstrumentSpec(SelectedBuiltIn);

    private void Preview(int pitch)
    {
        EndPreview();
        _ = _live.SetInstrumentAsync(CurrentInstrument());
        _previewPitch = pitch;
        _live.NoteOn(pitch, DefaultVelocity);
    }

    private void EndPreview()
    {
        if (_previewPitch >= 0)
        {
            _live.NoteOff(_previewPitch);
            _previewPitch = -1;
        }
    }

    private void SyncInstrumentFromTrack()
    {
        _syncingInstrument = true;
        var spec = Track?.Instrument ?? new InstrumentSpec();
        SelectedBuiltIn = spec.BuiltIn;
        if (spec.IsSoundFont && File.Exists(spec.SoundFontPath))
        {
            SoundFontPath = spec.SoundFontPath;
            try
            {
                Presets = SoundFontLibrary.Presets(spec.SoundFontPath!).Select(p => new PresetChoice(p.Bank, p.Preset, p.Name)).ToArray();
                SelectedPreset = Presets.FirstOrDefault(p => p.Bank == spec.Bank && p.Preset == spec.Preset);
            }
            catch (Exception)
            {
                Presets = [];
            }
        }
        else
        {
            SoundFontPath = null;
            Presets = [];
            SelectedPreset = null;
        }

        _syncingInstrument = false;
        OnPropertyChanged(nameof(IsDrums));
        _ = _live.SetInstrumentAsync(spec);
    }

    private void UpdatePlayhead()
    {
        if (Clip is { } clip)
        {
            PlayheadTick = _mixer.FramesToTicks(_mixer.PlayheadFrame - clip.StartFrame);
        }
    }

    private void OnMixerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MixerViewModel.PlayheadFrame):
                UpdatePlayhead();
                break;
            case nameof(MixerViewModel.BeatsPerBar):
                OnPropertyChanged(nameof(BeatsPerBar));
                OnPropertyChanged(nameof(LengthBarsText));
                Version++;
                break;
            case nameof(MixerViewModel.Bpm):
                UpdatePlayhead();
                break;
        }
    }

    /// <summary>Nach Rückgängig im Mischpult (oder gelöschter Spur) den geöffneten Clip wiederfinden.</summary>
    private void OnMixerTracksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Clip is { } clip && !_mixer.Tracks.Any(t => t.Clips.Contains(clip)))
        {
            Post(() =>
            {
                if (Clip is { } current && !_mixer.Tracks.Any(t => t.Clips.Contains(current)))
                {
                    if (_mixer.SelectedClip is { IsNoteClip: true } selected)
                    {
                        Open(selected);
                    }
                    else
                    {
                        Close();
                    }
                }
            });
        }
    }

    private void Post(Action action)
    {
        if (_ui is null)
        {
            action();
        }
        else
        {
            _ui.Post(_ => action(), null);
        }
    }
}
