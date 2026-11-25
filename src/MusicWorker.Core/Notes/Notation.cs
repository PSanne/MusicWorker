namespace MusicWorker.Core.Notes;

/// <summary>Notenschlüssel.</summary>
public enum Clef
{
    Treble,
    Bass,
}

/// <summary>Tonart-Vorzeichen: positive Zahl = Kreuze, negative = Bs (−7 … 7).</summary>
public sealed record KeySignature(int Fifths)
{
    // Reihenfolge der Vorzeichen (Buchstaben 0 = C … 6 = H)
    private static readonly int[] SharpOrder = [3, 0, 4, 1, 5, 2, 6]; // F C G D A E H
    private static readonly int[] FlatOrder = [6, 2, 5, 1, 4, 0, 3]; // B(H) E A D G C F

    public static KeySignature CMajor { get; } = new(0);

    /// <summary>Vorzeichen zu einer Tonart (Grundton 0 = C … 11 = H).</summary>
    public static KeySignature FromKey(int tonic, bool minor)
    {
        int major = ((((tonic + (minor ? 3 : 0)) % 12) + 12) % 12);
        int fifths = major switch
        {
            0 => 0, 7 => 1, 2 => 2, 9 => 3, 4 => 4, 11 => 5, 6 => 6,
            5 => -1, 10 => -2, 3 => -3, 8 => -4, 1 => -5,
            _ => 0,
        };
        return new KeySignature(fifths);
    }

    /// <summary>Welche Buchstaben (0 = C … 6 = H) ein Vorzeichen tragen, in Schreibreihenfolge.</summary>
    public IReadOnlyList<int> Letters => Fifths >= 0 ? SharpOrder.Take(Fifths).ToArray() : FlatOrder.Take(-Fifths).ToArray();

    /// <summary>Vorzeichen eines Buchstabens laut Tonart (−1, 0, +1).</summary>
    public int AlterOf(int letter) => Letters.Contains(letter) ? Math.Sign(Fifths) : 0;
}

/// <summary>Geschriebene Tonhöhe: Buchstabe (0 = C … 6 = H), Vorzeichen (−1 b, 0, +1 #) und Oktave (4 = eingestrichen).</summary>
public readonly record struct SpelledPitch(int Letter, int Alter, int Octave)
{
    /// <summary>Stufe auf den Notenlinien (jede Linie und jeder Zwischenraum zählt eins).</summary>
    public int Step => (Octave * 7) + Letter;
}

/// <summary>Eine geschriebene Note innerhalb eines Ereignisses.</summary>
/// <param name="Pitch">MIDI-Tonhöhe.</param>
/// <param name="Spelling">Geschriebene Tonhöhe (Buchstabe, Vorzeichen, Oktave).</param>
/// <param name="Accidental">Anzuzeigendes Versetzungszeichen (−1 b, 0 Auflösungszeichen, +1 #) oder null.</param>
/// <param name="TieToNext">Haltebogen zur nächsten Note gleicher Höhe.</param>
public sealed record NotationNote(int Pitch, SpelledPitch Spelling, int? Accidental, bool TieToNext);

/// <summary>
/// Ein Ereignis im Takt: Note(n) oder Pause. <see cref="Value"/> ist der Notenwert
/// (1 = Ganze, 2 = Halbe, 4 = Viertel, 8 = Achtel, 16 = Sechzehntel), auf Wunsch punktiert.
/// </summary>
public sealed record NotationEvent(int Start, int Units, int Value, bool Dotted, IReadOnlyList<NotationNote> Notes, bool FullMeasure = false)
{
    public bool IsRest => Notes.Count == 0;

    /// <summary>Fortsetzung einer übergebundenen Note (ohne neues Versetzungszeichen).</summary>
    public bool IsTieContinuation { get; init; }
}

public sealed record NotationMeasure(int Number, IReadOnlyList<NotationEvent> Events);

public sealed record NotationStaff(Clef Clef, IReadOnlyList<NotationMeasure> Measures);

/// <summary>Eine Stimme der Partitur (eine Notenspur) mit einem oder zwei Notensystemen.</summary>
public sealed record NotationPart(string Name, IReadOnlyList<NotationStaff> Staves);

/// <summary>Fertig aufbereitete Partitur.</summary>
public sealed record Score(string Title, int BeatsPerBar, KeySignature Key, double Bpm, IReadOnlyList<NotationPart> Parts, int MeasureCount);

/// <summary>
/// Macht aus Noten (Ticks) ein Notenblatt: Takte, Notenwerte mit Haltebögen, Pausen, Vorzeichen und
/// passende Notenschlüssel. Die Darstellung ist bewusst einfach: Zeiten werden auf Sechzehntel gerundet,
/// gleichzeitig beginnende Noten werden zum Akkord.
/// </summary>
public static class Notation
{
    /// <summary>Ticks einer Sechzehntelnote – die kleinste dargestellte Einheit.</summary>
    public const int UnitTicks = MusicalTime.TicksPerQuarter / 4;

    private static readonly int[] NoteValues = [16, 12, 8, 6, 4, 3, 2, 1]; // in Sechzehnteln
    private static readonly int[] RestValues = [16, 8, 4, 2, 1];

    /// <summary>Partitur aus mehreren Stimmen (Noten mit Ticks ab Songanfang).</summary>
    public static Score Build(string title, IEnumerable<(string Name, IReadOnlyList<Note> Notes)> parts, int beatsPerBar, KeySignature key, double bpm)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(key);
        beatsPerBar = Math.Clamp(beatsPerBar, 1, 16);
        int unitsPerBar = beatsPerBar * 4;
        var quantized = parts.Select(p => (p.Name, Notes: Quantize(p.Notes))).ToArray();
        int end = quantized.SelectMany(p => p.Notes).Select(n => n.End).DefaultIfEmpty(0).Max();
        int measures = Math.Max(1, (end + unitsPerBar - 1) / unitsPerBar);

        var result = new List<NotationPart>();
        foreach (var (name, notes) in quantized)
        {
            var staves = new List<NotationStaff>();
            foreach (var (clef, staffNotes) in SplitStaves(notes))
            {
                staves.Add(new NotationStaff(clef, BuildMeasures(staffNotes, measures, unitsPerBar, key)));
            }

            result.Add(new NotationPart(name, staves));
        }

        return new Score(title, beatsPerBar, key, bpm, result, measures);
    }

    /// <summary>Schreibweise einer Tonhöhe in einer Tonart (Kreuz-Tonarten mit #, B-Tonarten mit b).</summary>
    public static SpelledPitch Spell(int pitch, KeySignature key)
    {
        int pc = ((pitch % 12) + 12) % 12;
        int octave = (pitch / 12) - 1;
        int? white = pc switch { 0 => 0, 2 => 1, 4 => 2, 5 => 3, 7 => 4, 9 => 5, 11 => 6, _ => null };
        if (white is { } letter)
        {
            return new SpelledPitch(letter, 0, octave);
        }

        bool sharp = key.Fifths > 0 || (key.Fifths == 0 && pc is 1 or 6 or 8); // C-Dur: Cis, Es, Fis, Gis, B
        int below = pc - 1; // weiße Taste darunter
        int above = pc + 1;
        int Letter(int whitePc) => whitePc switch { 0 => 0, 2 => 1, 4 => 2, 5 => 3, 7 => 4, 9 => 5, _ => 6 };
        return sharp ? new SpelledPitch(Letter(below), 1, octave) : new SpelledPitch(Letter(above), -1, octave);
    }

    /// <summary>Position auf dem Notensystem: 0 = unterste Linie, 8 = oberste Linie, ungerade = Zwischenraum.</summary>
    public static int StaffPosition(SpelledPitch pitch, Clef clef) => pitch.Step - (clef == Clef.Treble ? 30 : 18); // E4 bzw. G2

    /// <summary>Zerlegt eine Dauer (Sechzehntel ab <paramref name="start"/>) in darstellbare Notenwerte, die keine Taktgrenze überschreiten.</summary>
    public static IReadOnlyList<(int Start, int Units, int Value, bool Dotted)> SplitDuration(int start, int units, int unitsPerBar, bool rest = false)
    {
        var pieces = new List<(int, int, int, bool)>();
        int position = start;
        int remaining = units;
        while (remaining > 0)
        {
            int inBar = position % unitsPerBar;
            int room = Math.Min(remaining, unitsPerBar - inBar);
            int chosen = 1;
            foreach (int v in rest ? RestValues : NoteValues)
            {
                bool offBeat = inBar % 4 != 0;
                bool crossesBeat = offBeat && inBar + v > ((inBar / 4) + 1) * 4; // Synkopen am Schlag teilen
                if (v <= room && inBar % Alignment(v) == 0 && !crossesBeat)
                {
                    chosen = v;
                    break;
                }
            }

            var (value, dotted) = Describe(chosen);
            pieces.Add((position, chosen, value, dotted));
            position += chosen;
            remaining -= chosen;
        }

        return pieces;
    }

    /// <summary>
    /// Welche Ereignisse eines Takts mit einem Balken verbunden werden: aufeinanderfolgende Achtel/Sechzehntel
    /// (keine Pausen) innerhalb desselben Schlags.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<int>> BeamGroups(NotationMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(measure);
        var groups = new List<IReadOnlyList<int>>();
        var current = new List<int>();
        int beat = -1;
        for (int i = 0; i < measure.Events.Count; i++)
        {
            var e = measure.Events[i];
            int eventBeat = e.Start / 4;
            bool beamable = !e.IsRest && e.Value >= 8;
            if (!beamable || eventBeat != beat || (e.Start + e.Units - 1) / 4 != eventBeat)
            {
                Flush();
            }

            if (beamable && (e.Start + e.Units - 1) / 4 == eventBeat)
            {
                beat = eventBeat;
                current.Add(i);
            }
        }

        Flush();
        return groups;

        void Flush()
        {
            if (current.Count >= 2)
            {
                groups.Add(current.ToArray());
            }

            current.Clear();
            beat = -1;
        }
    }

    /// <summary>Notenschlüssel bzw. Klaviersystem passend zum Tonumfang.</summary>
    private static IEnumerable<(Clef Clef, QNote[] Notes)> SplitStaves(QNote[] notes)
    {
        if (notes.Length == 0 || notes.All(n => n.Pitch >= 55))
        {
            return [(Clef.Treble, notes)];
        }

        if (notes.All(n => n.Pitch <= 64))
        {
            return [(Clef.Bass, notes)];
        }

        return [(Clef.Treble, notes.Where(n => n.Pitch >= 60).ToArray()), (Clef.Bass, notes.Where(n => n.Pitch < 60).ToArray())];
    }

    private static IReadOnlyList<NotationMeasure> BuildMeasures(QNote[] notes, int measureCount, int unitsPerBar, KeySignature key)
    {
        int total = measureCount * unitsPerBar;
        var events = new List<NotationEvent>[measureCount];
        for (int m = 0; m < measureCount; m++)
        {
            events[m] = [];
        }

        // Akkorde: gleichzeitig beginnende Noten; Dauer bis zum frühesten Ende bzw. zum nächsten Einsatz
        var starts = notes.Select(n => n.Start).Distinct().OrderBy(s => s).ToArray();
        int time = 0;
        for (int i = 0; i <= starts.Length; i++)
        {
            int next = i < starts.Length ? starts[i] : total;
            if (next > time)
            {
                AddRest(time, next - time);
            }

            if (i == starts.Length)
            {
                break;
            }

            int start = starts[i];
            var chord = notes.Where(n => n.Start == start).GroupBy(n => n.Pitch).Select(g => g.First()).OrderBy(n => n.Pitch).ToArray();
            int end = chord.Min(n => n.End);
            if (i + 1 < starts.Length)
            {
                end = Math.Min(end, starts[i + 1]);
            }

            end = Math.Min(Math.Max(end, start + 1), total);
            var pieces = SplitDuration(start, end - start, unitsPerBar);
            for (int p = 0; p < pieces.Count; p++)
            {
                var (pieceStart, units, value, dotted) = pieces[p];
                bool tie = p < pieces.Count - 1;
                var written = chord.Select(n => new NotationNote(n.Pitch, Spell(n.Pitch, key), null, tie)).ToArray();
                events[pieceStart / unitsPerBar].Add(new NotationEvent(pieceStart % unitsPerBar, units, value, dotted, written)
                {
                    IsTieContinuation = p > 0,
                });
            }

            time = end;
        }

        var measures = new List<NotationMeasure>();
        for (int m = 0; m < measureCount; m++)
        {
            var list = events[m];
            if (list.All(e => e.IsRest))
            {
                list = [new NotationEvent(0, unitsPerBar, 1, false, [], FullMeasure: true)];
            }

            measures.Add(new NotationMeasure(m + 1, SetAccidentals(list, key)));
        }

        return measures;

        void AddRest(int from, int length)
        {
            foreach (var (pieceStart, units, value, dotted) in SplitDuration(from, length, unitsPerBar, rest: true))
            {
                events[pieceStart / unitsPerBar].Add(new NotationEvent(pieceStart % unitsPerBar, units, value, dotted, []));
            }
        }
    }

    /// <summary>Versetzungszeichen nach den üblichen Regeln: gelten bis zum Taktende, Tonart-Vorzeichen werden nicht wiederholt.</summary>
    private static IReadOnlyList<NotationEvent> SetAccidentals(List<NotationEvent> events, KeySignature key)
    {
        var current = new Dictionary<int, int>(); // Stufe → gültiges Vorzeichen im Takt
        var result = new List<NotationEvent>();
        foreach (var e in events)
        {
            if (e.IsRest)
            {
                result.Add(e);
                continue;
            }

            var notes = new List<NotationNote>();
            foreach (var n in e.Notes)
            {
                int step = n.Spelling.Step;
                int valid = current.TryGetValue(step, out int a) ? a : key.AlterOf(n.Spelling.Letter);
                int? accidental = null;
                if (n.Spelling.Alter != valid && !e.IsTieContinuation)
                {
                    accidental = n.Spelling.Alter;
                }

                current[step] = n.Spelling.Alter;
                notes.Add(n with { Accidental = accidental });
            }

            result.Add(e with { Notes = notes });
        }

        return result;
    }

    private static QNote[] Quantize(IReadOnlyList<Note> notes) =>
        notes.Where(n => n.Pitch is >= 0 and <= 127)
            .Select(n =>
            {
                int start = (int)Math.Round(Math.Max(0, n.Start) / (double)UnitTicks);
                int end = (int)Math.Round(Math.Max(0, n.End) / (double)UnitTicks);
                return new QNote(n.Pitch, start, Math.Max(start + 1, end));
            })
            .ToArray();

    private static int Alignment(int units) => units switch
    {
        >= 4 => 4,
        3 or 2 => 2,
        _ => 1,
    };

    private static (int Value, bool Dotted) Describe(int units) => units switch
    {
        16 => (1, false),
        12 => (2, true),
        8 => (2, false),
        6 => (4, true),
        4 => (4, false),
        3 => (8, true),
        2 => (8, false),
        _ => (16, false),
    };

    private readonly record struct QNote(int Pitch, int Start, int End);
}
