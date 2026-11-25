namespace MusicWorker.Core.Notes;

/// <summary>Akkordarten.</summary>
public enum ChordQuality
{
    Major,
    Minor,
    Seventh,
    MajorSeventh,
    MinorSeventh,
    Diminished,
    Augmented,
    Sus2,
    Sus4,
}

/// <summary>Akkorde bauen und benennen.</summary>
public static class Chords
{
    /// <summary>Notennamen in deutscher Schreibweise (H statt B, B statt Bb).</summary>
    public static IReadOnlyList<string> RootNames { get; } = ["C", "Cis", "D", "Es", "E", "F", "Fis", "G", "As", "A", "B", "H"];

    public static IReadOnlyList<ChordQuality> All { get; } = Enum.GetValues<ChordQuality>();

    public static string QualityName(ChordQuality quality) => quality switch
    {
        ChordQuality.Major => "Dur",
        ChordQuality.Minor => "Moll",
        ChordQuality.Seventh => "Septakkord (7)",
        ChordQuality.MajorSeventh => "Großer Septakkord (maj7)",
        ChordQuality.MinorSeventh => "Moll-Septakkord (m7)",
        ChordQuality.Diminished => "Vermindert (dim)",
        ChordQuality.Augmented => "Übermäßig (aug)",
        ChordQuality.Sus2 => "sus2",
        ChordQuality.Sus4 => "sus4",
        _ => quality.ToString(),
    };

    /// <summary>Halbtonabstände vom Grundton.</summary>
    public static int[] Intervals(ChordQuality quality) => quality switch
    {
        ChordQuality.Major => [0, 4, 7],
        ChordQuality.Minor => [0, 3, 7],
        ChordQuality.Seventh => [0, 4, 7, 10],
        ChordQuality.MajorSeventh => [0, 4, 7, 11],
        ChordQuality.MinorSeventh => [0, 3, 7, 10],
        ChordQuality.Diminished => [0, 3, 6],
        ChordQuality.Augmented => [0, 4, 8],
        ChordQuality.Sus2 => [0, 2, 7],
        ChordQuality.Sus4 => [0, 5, 7],
        _ => [0, 4, 7],
    };

    /// <summary>Kurzname wie „Am“, „G7“, „Cmaj7“, „H°“.</summary>
    public static string Name(int root, ChordQuality quality)
    {
        string name = RootNames[((root % 12) + 12) % 12];
        return name + quality switch
        {
            ChordQuality.Minor => "m",
            ChordQuality.Seventh => "7",
            ChordQuality.MajorSeventh => "maj7",
            ChordQuality.MinorSeventh => "m7",
            ChordQuality.Diminished => "°",
            ChordQuality.Augmented => "+",
            ChordQuality.Sus2 => "sus2",
            ChordQuality.Sus4 => "sus4",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Tonhöhen eines Akkords. <paramref name="root"/> ist die MIDI-Tonhöhe des Grundtons;
    /// jede Umkehrung legt den untersten Ton eine Oktave nach oben.
    /// </summary>
    public static int[] Build(int root, ChordQuality quality, int inversion = 0)
    {
        var pitches = Intervals(quality).Select(i => root + i).ToList();
        for (int i = 0; i < Math.Clamp(inversion, 0, pitches.Count - 1); i++)
        {
            int lowest = pitches[0];
            pitches.RemoveAt(0);
            pitches.Add(lowest + 12);
        }

        return [.. pitches.Select(p => Math.Clamp(p, 0, 127))];
    }

    /// <summary>Akkord als Noten (alle Töne gleichzeitig).</summary>
    public static IReadOnlyList<Note> Notes(int root, ChordQuality quality, long start, long length, int velocity = 90, int inversion = 0) =>
        [.. Build(root, quality, inversion).Select(p => new Note(p, start, Math.Max(1, length), velocity))];

    /// <summary>
    /// Leitereigener Akkord auf Stufe <paramref name="degree"/> (0 = I … 6 = VII) in Dur oder natürlichem Moll –
    /// Dreiklang oder (mit <paramref name="seventh"/>) Vierklang. Liefert Grundton (0 … 11) und Akkordart.
    /// </summary>
    public static (int Root, ChordQuality Quality) Diatonic(int tonic, bool minor, int degree, bool seventh = false)
    {
        int[] scale = minor ? [0, 2, 3, 5, 7, 8, 10] : [0, 2, 4, 5, 7, 9, 11];
        degree = ((degree % 7) + 7) % 7;
        int Step(int offset)
        {
            int index = degree + offset;
            return scale[index % 7] + (12 * (index / 7));
        }

        int third = Step(2) - Step(0);
        int fifth = Step(4) - Step(0);
        int sev = Step(6) - Step(0);
        var quality = (third, fifth) switch
        {
            (4, 7) => seventh ? (sev == 10 ? ChordQuality.Seventh : ChordQuality.MajorSeventh) : ChordQuality.Major,
            (3, 7) => seventh ? ChordQuality.MinorSeventh : ChordQuality.Minor,
            (3, 6) => ChordQuality.Diminished,
            (4, 8) => ChordQuality.Augmented,
            _ => ChordQuality.Major,
        };
        return ((tonic + scale[degree]) % 12, quality);
    }

    /// <summary>Bekannte Akkordfolgen (Stufen 0 = I … 6 = VII).</summary>
    public static IReadOnlyList<ChordProgression> Progressions { get; } =
    [
        new("Pop: I – V – vi – IV", false, [0, 4, 5, 3]),
        new("Gefühlvoll: vi – IV – I – V", false, [5, 3, 0, 4]),
        new("Klassisch: I – IV – V – I", false, [0, 3, 4, 0]),
        new("50er-Jahre: I – vi – IV – V", false, [0, 5, 3, 4]),
        new("Jazz: ii – V – I – I", false, [1, 4, 0, 0]),
        new("Blues (12 Takte): I I I I – IV IV I I – V IV I V", false, [0, 0, 0, 0, 3, 3, 0, 0, 4, 3, 0, 4]),
        new("Moll-Pop: i – VI – III – VII", true, [0, 5, 2, 6]),
        new("Moll klassisch: i – iv – v – i", true, [0, 3, 4, 0]),
        new("Andalusisch: i – VII – VI – V", true, [0, 6, 5, 4]),
    ];

    /// <summary>
    /// Noten einer Akkordfolge in einer Tonart: jeder Akkord dauert <paramref name="chordTicks"/>.
    /// Die Akkorde werden eng geführt (jeweils die Umkehrung, die am nächsten am vorigen Akkord liegt).
    /// </summary>
    public static IReadOnlyList<Note> Progression(
        ChordProgression progression, int tonic, bool minor, int octave, long start, long chordTicks, bool sevenths = false, int velocity = 90, bool withBass = false)
    {
        var notes = new List<Note>();
        int[]? previous = null;
        int baseRoot = (12 * (octave + 1)) + (((tonic % 12) + 12) % 12);
        for (int i = 0; i < progression.Degrees.Count; i++)
        {
            int degree = progression.Degrees[i];
            var (root, quality) = Diatonic(tonic, minor, degree, sevenths);

            // Durchschnittlich so liegen wie der vorige Akkord (oder um den Grundton der Tonart).
            double center = previous?.Average() ?? baseRoot + 5;
            int[] best = [];
            double bestDistance = double.MaxValue;
            for (int octaveShift = -2; octaveShift <= 2; octaveShift++)
            {
                int r = (12 * (octave + 1 + octaveShift)) + root;
                for (int inv = 0; inv < Intervals(quality).Length; inv++)
                {
                    var candidate = Build(r, quality, inv);
                    double distance = Math.Abs(candidate.Average() - center);
                    if (distance < bestDistance - 1e-9)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }

            long at = start + (i * chordTicks);
            notes.AddRange(best.Select(p => new Note(p, at, chordTicks, velocity)));
            if (withBass)
            {
                int bass = (12 * octave) + root; // eine Oktave unter der Akkordlage
                notes.Add(new Note(Math.Clamp(bass, 0, 127), at, chordTicks, Math.Min(127, velocity + 5)));
            }

            previous = best;
        }

        return NoteTools.Sorted(notes);
    }
}

/// <summary>Eine Akkordfolge: Name, ob sie für Moll gedacht ist, und die Stufen.</summary>
public sealed record ChordProgression(string Name, bool Minor, IReadOnlyList<int> Degrees);

/// <summary>Reihenfolge, in der der Arpeggiator die Töne eines Akkords spielt.</summary>
public enum ArpeggioPattern
{
    Up,
    Down,
    UpDown,
    Alberti,
    Random,
}

/// <summary>Arpeggiator: macht aus gehaltenen Akkorden eine Folge einzelner Töne.</summary>
public static class Arpeggiator
{
    public static IReadOnlyList<ArpeggioPattern> All { get; } = Enum.GetValues<ArpeggioPattern>();

    public static string PatternName(ArpeggioPattern pattern) => pattern switch
    {
        ArpeggioPattern.Up => "Aufwärts",
        ArpeggioPattern.Down => "Abwärts",
        ArpeggioPattern.UpDown => "Auf und ab",
        ArpeggioPattern.Alberti => "Alberti (tief – hoch – Mitte – hoch)",
        ArpeggioPattern.Random => "Zufällig",
        _ => pattern.ToString(),
    };

    /// <summary>
    /// Gleichzeitig beginnende Noten werden als Akkord behandelt und über ihre Dauer in Schritten von
    /// <paramref name="stepTicks"/> nacheinander gespielt (über <paramref name="octaves"/> Oktaven).
    /// <paramref name="gate"/> ist die Länge jedes Tons im Verhältnis zum Schritt (0,1 … 1).
    /// Einzelne Noten bleiben unverändert, wenn nur eine Oktave gewählt ist.
    /// </summary>
    public static IReadOnlyList<Note> Apply(IEnumerable<Note> notes, long stepTicks, ArpeggioPattern pattern, int octaves = 1, double gate = 0.9, int seed = 1)
    {
        stepTicks = Math.Max(1, stepTicks);
        octaves = Math.Clamp(octaves, 1, 4);
        gate = Math.Clamp(gate, 0.1, 1);
        var random = new Random(seed);
        var result = new List<Note>();
        foreach (var group in notes.GroupBy(n => n.Start).OrderBy(g => g.Key))
        {
            var chord = group.OrderBy(n => n.Pitch).ToArray();
            if (chord.Length == 1 && octaves == 1)
            {
                result.Add(chord[0]);
                continue;
            }

            var pitches = Enumerable.Range(0, octaves)
                .SelectMany(o => chord.Select(n => n.Pitch + (12 * o)))
                .Where(p => p <= 127)
                .ToArray();
            var sequence = Sequence(pitches, pattern);
            long start = group.Key;
            long end = chord.Max(n => n.End);
            int velocity = (int)Math.Round(chord.Average(n => n.Velocity));
            int step = 0;
            for (long t = start; t < end; t += stepTicks, step++)
            {
                int pitch = pattern == ArpeggioPattern.Random
                    ? pitches[random.Next(pitches.Length)]
                    : sequence[step % sequence.Length];
                long length = Math.Max(1, Math.Min((long)Math.Round(stepTicks * gate), end - t));
                result.Add(new Note(pitch, t, length, velocity, chord[0].Voice));
            }
        }

        return NoteTools.Sorted(result);
    }

    private static int[] Sequence(int[] pitches, ArpeggioPattern pattern)
    {
        switch (pattern)
        {
            case ArpeggioPattern.Down:
                return [.. pitches.Reverse()];
            case ArpeggioPattern.UpDown:
                return pitches.Length <= 2 ? pitches : [.. pitches, .. pitches.Reverse().Skip(1).Take(pitches.Length - 2)];
            case ArpeggioPattern.Alberti:
                if (pitches.Length < 3)
                {
                    return pitches;
                }

                int low = pitches[0];
                int high = pitches[^1];
                int middle = pitches[pitches.Length / 2];
                return [low, high, middle, high];
            default:
                return pitches;
        }
    }
}

/// <summary>Ein Schlagzeug-Instrument im Raster (General-MIDI-Tonhöhe).</summary>
public sealed record DrumSound(string Name, int Pitch);

/// <summary>
/// Schlagzeug-Muster für das Raster: pro Instrument ein An/Aus je Schritt (16 Schritte = ein Takt in Sechzehnteln).
/// </summary>
public sealed class DrumPattern
{
    /// <summary>Die Zeilen des Rasters, von oben nach unten.</summary>
    public static IReadOnlyList<DrumSound> Sounds { get; } =
    [
        new("Becken (Crash)", 49),
        new("Ride-Becken", 51),
        new("Hi-Hat offen", 46),
        new("Hi-Hat geschlossen", 42),
        new("Klatschen", 39),
        new("Snare", 38),
        new("Rimshot", 37),
        new("Tom hoch", 50),
        new("Tom mittel", 47),
        new("Tom tief", 45),
        new("Kuhglocke", 56),
        new("Bass-Trommel", 36),
    ];

    private readonly bool[,] _steps;

    public DrumPattern(int steps = 16)
    {
        StepCount = Math.Clamp(steps, 1, 64);
        _steps = new bool[Sounds.Count, StepCount];
    }

    public int StepCount { get; }

    public bool this[int sound, int step]
    {
        get => _steps[sound, step];
        set => _steps[sound, step] = value;
    }

    /// <summary>Gesetzte Schritte insgesamt.</summary>
    public int Count
    {
        get
        {
            int count = 0;
            foreach (bool on in _steps)
            {
                count += on ? 1 : 0;
            }

            return count;
        }
    }

    public DrumPattern Clone()
    {
        var copy = new DrumPattern(StepCount);
        Array.Copy(_steps, copy._steps, _steps.Length);
        return copy;
    }

    /// <summary>
    /// Noten des Musters ab <paramref name="start"/>, <paramref name="repeats"/>-mal hintereinander.
    /// Schläge auf den Vierteln werden etwas betont.
    /// </summary>
    public IReadOnlyList<Note> ToNotes(long start, long stepTicks, int repeats = 1, int velocity = 100)
    {
        var notes = new List<Note>();
        long length = Math.Max(1, stepTicks / 2);
        for (int r = 0; r < Math.Max(1, repeats); r++)
        {
            for (int step = 0; step < StepCount; step++)
            {
                long at = start + (((long)r * StepCount) + step) * stepTicks;
                bool onBeat = step % 4 == 0;
                for (int s = 0; s < Sounds.Count; s++)
                {
                    if (_steps[s, step])
                    {
                        int v = Math.Clamp(onBeat ? velocity : velocity - 15, 1, 127);
                        notes.Add(new Note(Sounds[s].Pitch, at, length, v));
                    }
                }
            }
        }

        return NoteTools.Sorted(notes);
    }

    /// <summary>Liest ein Muster aus vorhandenen Noten (ab <paramref name="start"/>, ein Durchgang).</summary>
    public static DrumPattern FromNotes(IEnumerable<Note> notes, long start, long stepTicks, int steps = 16)
    {
        var pattern = new DrumPattern(steps);
        foreach (var note in notes)
        {
            double position = (note.Start - start) / (double)Math.Max(1, stepTicks);
            int step = (int)Math.Round(position);
            int sound = Sounds.ToList().FindIndex(s => s.Pitch == note.Pitch);
            if (sound >= 0 && step >= 0 && step < pattern.StepCount && Math.Abs(position - step) < 0.25)
            {
                pattern[sound, step] = true;
            }
        }

        return pattern;
    }

    /// <summary>Fertige Muster (je ein Takt mit 16 Schritten).</summary>
    public static IReadOnlyList<(string Name, DrumPattern Pattern)> Presets { get; } =
    [
        ("Rock", Make(kick: "x.......x.x.....", snare: "....x.......x...", closedHat: "x.x.x.x.x.x.x.x.")),
        ("Pop", Make(kick: "x.....x...x.....", snare: "....x.......x...", closedHat: "x.x.x.x.x.x.x.x.", clap: "............x...")),
        ("Disco / House", Make(kick: "x...x...x...x...", clap: "....x.......x...", openHat: "..x...x...x...x.", closedHat: "x...x...x...x...")),
        ("Hip-Hop", Make(kick: "x......x..x.....", snare: "....x.......x...", closedHat: "x.x.x.x.x.x.x.xx")),
        ("Ballade", Make(kick: "x.........x.....", rim: "....x.......x...", closedHat: "x...x...x...x...")),
        ("Funk", Make(kick: "x..x..x...x..x..", snare: "....x..x.x..x...", closedHat: "xxxxxxxxxxxxxxxx")),
        ("Reggae (One Drop)", Make(kick: "........x.......", rim: "........x.......", closedHat: "..x...x...x...x.")),
        ("Shuffle", Make(kick: "x.....x.x.......", snare: "....x.......x...", closedHat: "x..xx..xx..xx..x")),
        ("Bossa Nova", Make(kick: "x..xx..xx..xx..x", rim: "x..x..x...x..x..", closedHat: "x.x.x.x.x.x.x.x.")),
        ("Walzer (3/4)", Make(kick: "x...........", snare: "....x...x...", closedHat: "x...x...x...", steps: 12)),
    ];

    private static DrumPattern Make(
        string? kick = null, string? snare = null, string? closedHat = null, string? openHat = null, string? clap = null, string? rim = null, int steps = 16)
    {
        var pattern = new DrumPattern(steps);
        void Set(string? row, int pitch)
        {
            if (row is null)
            {
                return;
            }

            int sound = Sounds.ToList().FindIndex(s => s.Pitch == pitch);
            for (int i = 0; i < Math.Min(row.Length, steps); i++)
            {
                pattern[sound, i] = row[i] == 'x';
            }
        }

        Set(kick, 36);
        Set(snare, 38);
        Set(closedHat, 42);
        Set(openHat, 46);
        Set(clap, 39);
        Set(rim, 37);
        return pattern;
    }
}
