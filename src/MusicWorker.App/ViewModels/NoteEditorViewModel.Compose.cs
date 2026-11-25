using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Services;
using MusicWorker.Core.Notes;

namespace MusicWorker.App.ViewModels;

/// <summary>Komponier-Hilfen im Noten-Editor: Akkorde, Akkordfolgen, Arpeggiator und Schlagzeug-Raster.</summary>
public sealed partial class NoteEditorViewModel
{
    private static readonly string[] LengthNames = ["1/4", "1/2", "1 Takt", "2 Takte"];
    private static readonly string[] Inversions = ["Grundstellung", "1. Umkehrung", "2. Umkehrung", "3. Umkehrung"];
    private static readonly string[] Octaves = ["2 (tief)", "3", "4 (Mitte)", "5", "6 (hoch)"];

    private int _chordRoot;
    private int _chordQuality;
    private int _chordOctave = 2;
    private int _chordInversion;
    private int _chordLength = 2;
    private int _progressionIndex;
    private int _progressionLength = 2;
    private int _progressionOctave = 2;
    private bool _progressionSevenths;
    private bool _progressionBass = true;
    private int _arpPattern;
    private int _arpStep = 1;
    private int _arpOctaves = 1;
    private double _arpGate = 80;
    private DrumPattern? _lastDrums;
    private int _lastDrumRepeats = 4;

    private long BarTicks => MusicalTime.TicksPerBar(BeatsPerBar);

    private long ChordTicks(int choice) => choice switch
    {
        0 => MusicalTime.TicksPerQuarter,
        1 => MusicalTime.TicksPerQuarter * 2,
        3 => BarTicks * 2,
        _ => BarTicks,
    };

    /// <summary>Einen Akkord am grünen Cursor einfügen; der Cursor rückt danach weiter.</summary>
    [RelayCommand]
    private void InsertChord()
    {
        if (Clip is null)
        {
            return;
        }

        var values = _dialogs.AskParameters(new ParameterRequest(
            "Akkord einfügen",
            "Der Akkord wird am grünen Cursor eingefügt (Klick ins Taktlineal setzt ihn). Danach rückt der Cursor weiter – so legst du schnell eine Begleitung an.",
            [
                new ChoiceParameter("Grundton", Chords.RootNames, _chordRoot),
                new ChoiceParameter("Art", Chords.All.Select(Chords.QualityName).ToArray(), _chordQuality),
                new ChoiceParameter("Oktave", Octaves, _chordOctave),
                new ChoiceParameter("Lage", Inversions, _chordInversion, "Umkehrungen legen den tiefsten Ton nach oben – so klingen Akkordwechsel weicher."),
                new ChoiceParameter("Länge", LengthNames, _chordLength),
            ]));
        if (values is null)
        {
            return;
        }

        (_chordRoot, _chordQuality, _chordOctave, _chordInversion, _chordLength) =
            ((int)values[0], (int)values[1], (int)values[2], (int)values[3], (int)values[4]);
        var quality = Chords.All[_chordQuality];
        int root = (12 * (_chordOctave + 3)) + _chordRoot; // Oktave 2 → C2 = MIDI 36
        long length = ChordTicks(_chordLength);
        long start = Math.Max(0, StepCursorTick);
        var chord = Chords.Notes(root, quality, start, length, DefaultVelocity, Math.Min(_chordInversion, Chords.Intervals(quality).Length - 1));
        InsertNotes(chord, $"Akkord {Chords.Name(root, quality)} eingefügt.");
        StepCursorTick = start + length;
    }

    /// <summary>Eine ganze Akkordfolge in einer Tonart einfügen (ab dem Takt des Cursors).</summary>
    [RelayCommand]
    private void InsertProgression()
    {
        if (Clip is null)
        {
            return;
        }

        var key = _mixer.ProjectKey;
        var progressions = Chords.Progressions;
        var values = _dialogs.AskParameters(new ParameterRequest(
            "Akkordfolge einfügen",
            key is null
                ? "Wähle Tonart und Folge – die Akkorde passen automatisch zur Tonart und werden eng geführt (kleine Sprünge zwischen den Akkorden)."
                : $"Erkannte Tonart des Projekts: {key.Name}. Die Akkorde passen automatisch zur Tonart und werden eng geführt.",
            [
                new ChoiceParameter("Tonart (Grundton)", Chords.RootNames, key?.Tonic ?? _chordRoot),
                new ChoiceParameter("Tongeschlecht", ["Dur", "Moll"], key?.Minor == true ? 1 : 0, "Moll-Folgen klingen in Moll, Dur-Folgen in Dur am natürlichsten – du kannst aber frei kombinieren."),
                new ChoiceParameter("Folge", progressions.Select(p => p.Name + (p.Minor ? " (Moll)" : string.Empty)).ToArray(), _progressionIndex),
                new ChoiceParameter("Dauer je Akkord", LengthNames, _progressionLength),
                new ChoiceParameter("Oktave", Octaves, _progressionOctave),
                new ChoiceParameter("Vierklänge (Septimen)", ["Nein", "Ja"], _progressionSevenths ? 1 : 0, "Septimen klingen jazziger und voller."),
                new ChoiceParameter("Grundton als Bass dazu", ["Nein", "Ja"], _progressionBass ? 1 : 0),
            ]));
        if (values is null)
        {
            return;
        }

        int tonic = (int)values[0];
        bool minor = (int)values[1] == 1;
        _progressionIndex = (int)values[2];
        _progressionLength = (int)values[3];
        _progressionOctave = (int)values[4];
        _progressionSevenths = (int)values[5] == 1;
        _progressionBass = (int)values[6] == 1;
        var progression = progressions[_progressionIndex];
        long start = Math.Max(0, StepCursorTick) / BarTicks * BarTicks;
        long chordTicks = ChordTicks(_progressionLength);
        var notes = Chords.Progression(progression, tonic, minor, _progressionOctave + 2, start, chordTicks, _progressionSevenths, DefaultVelocity, _progressionBass);
        string names = string.Join(" – ", progression.Degrees.Select(d =>
        {
            var (root, quality) = Chords.Diatonic(tonic, minor, d, _progressionSevenths);
            return Chords.Name(root, quality);
        }));
        InsertNotes(notes, $"Akkordfolge eingefügt: {names}");
        StepCursorTick = start + (chordTicks * progression.Degrees.Count);
    }

    /// <summary>Gewählte Akkorde (bzw. alle) als gebrochene Akkorde spielen.</summary>
    [RelayCommand]
    private void Arpeggiate()
    {
        if (Clip is null || Notes.Count == 0)
        {
            return;
        }

        string[] steps = ["1/8", "1/16", "1/8-Triole", "1/32"];
        var values = _dialogs.AskParameters(new ParameterRequest(
            "Arpeggiator",
            "Gleichzeitig angeschlagene Noten (Akkorde) werden nacheinander gespielt – wie ein Harfen- oder Gitarrenpicking. Wirkt auf die Auswahl, ohne Auswahl auf alle Noten.",
            [
                new ChoiceParameter("Reihenfolge", Arpeggiator.All.Select(Arpeggiator.PatternName).ToArray(), _arpPattern),
                new ChoiceParameter("Tempo der Töne", steps, _arpStep),
                new SliderParameter("Oktaven", 1, 3, _arpOctaves, 1, string.Empty, "Mehr Oktaven: der Akkord wird nach oben weitergespielt."),
                new SliderParameter("Tonlänge", 20, 100, _arpGate, 5, "%", "Kurz = gezupft, 100 % = gebunden."),
            ]));
        if (values is null)
        {
            return;
        }

        _arpPattern = (int)values[0];
        _arpStep = (int)values[1];
        _arpOctaves = (int)Math.Round(values[2]);
        _arpGate = values[3];
        long step = _arpStep switch
        {
            0 => MusicalTime.GridTicks(8),
            2 => MusicalTime.GridTicks(8, triplet: true),
            3 => MusicalTime.GridTicks(32),
            _ => MusicalTime.GridTicks(16),
        };
        var pattern = Arpeggiator.All[_arpPattern];
        Apply(notes => Arpeggiator.Apply(notes, step, pattern, _arpOctaves, _arpGate / 100, seed: Environment.TickCount), $"als Arpeggio ({Arpeggiator.PatternName(pattern)}) gespielt");
    }

    /// <summary>Schlagzeug-Raster öffnen: Muster anklicken oder fertige Rhythmen wählen und ab dem Takt des Cursors einfügen.</summary>
    [RelayCommand]
    private void EditDrums()
    {
        if (Clip is null)
        {
            return;
        }

        long start = Math.Max(0, StepCursorTick) / BarTicks * BarTicks;
        long stepTicks = MusicalTime.GridTicks(16);
        int steps = BeatsPerBar * 4;
        var existing = DrumPattern.FromNotes(Notes.Where(n => n.Start >= start && n.Start < start + BarTicks), start, stepTicks, steps);
        var initial = existing.Count > 0 ? existing : _lastDrums is { } last && last.StepCount == steps ? last : new DrumPattern(steps);
        var result = _dialogs.EditDrumPattern(new DrumGridRequest(initial, _lastDrumRepeats, BeatsPerBar, (int)(start / BarTicks) + 1, IsDrums));
        if (result is null)
        {
            return;
        }

        _lastDrums = result.Pattern.Clone();
        _lastDrumRepeats = result.Repeats;
        var drumNotes = result.Pattern.ToNotes(start, stepTicks, result.Repeats, DefaultVelocity);
        long end = start + (BarTicks * result.Repeats);
        var drumPitches = DrumPattern.Sounds.Select(s => s.Pitch).ToHashSet();
        var kept = result.ReplaceExisting
            ? Notes.Where(n => n.Start < start || n.Start >= end || !drumPitches.Contains(n.Pitch))
            : Notes;
        PushUndo();
        Commit(NoteTools.Sorted(kept.Concat(drumNotes)), Math.Max(LengthTicks, end));
        Selection = [.. drumNotes];
        _mixer.StatusText = $"Schlagzeug: {result.Repeats} Takt(e) ab Takt {(start / BarTicks) + 1} eingefügt ({drumNotes.Count} Schläge)."
            + (IsDrums ? string.Empty : " Tipp: Wähle als Instrument „Schlagzeug“, damit es auch wie ein Schlagzeug klingt.");
    }

    /// <summary>Notenblatt aller Notenspuren (ohne Schlagzeug) anzeigen – zum Drucken oder als PDF.</summary>
    [RelayCommand]
    private void ShowSheetMusic()
    {
        var parts = _mixer.Tracks
            .Where(t => t.IsNoteTrack && t.Instrument?.IsDrums != true)
            .Select(t => new SheetMusicPart(t.Name, NoteTools.Sorted(t.Clips
                .Where(c => c.Notes is not null)
                .SelectMany(c => NoteTools.Move(c.Notes!.Where(n => n.Start < c.LengthTicks), _mixer.FramesToTicks(c.StartFrame))))))
            .Where(p => p.Notes.Count > 0)
            .ToArray();
        if (parts.Length == 0)
        {
            _mixer.StatusText = "Für das Notenblatt fehlen Noten: Lege eine Notenspur an und zeichne oder spiele ein paar Noten ein (Schlagzeug wird nicht als Noten gezeigt).";
            return;
        }

        var key = _mixer.ProjectKey ?? MusicWorker.Core.Analysis.MusicAnalysis.DetectKey(parts.SelectMany(p => p.Notes));
        string title = _mixer.ProjectPath is { } path ? System.IO.Path.GetFileNameWithoutExtension(path) : "Mein Lied";
        _dialogs.ShowSheetMusic(new SheetMusicRequest(title, parts, BeatsPerBar, _mixer.Tempo.BaseBpm, key?.Tonic ?? 0, key?.Minor ?? false));
    }

    private void InsertNotes(IReadOnlyList<Note> added, string status)
    {
        PushUndo();
        Commit(NoteTools.Sorted(Notes.Concat(added)), Math.Max(LengthTicks, NoteTools.EndOf(added)));
        Selection = [.. added];
        _mixer.StatusText = status;
    }
}
