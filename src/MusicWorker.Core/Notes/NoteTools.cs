namespace MusicWorker.Core.Notes;

/// <summary>
/// Bearbeitungen für Noten: Quantisieren, Humanisieren, Transponieren, Längen und Anschlag ändern,
/// Stimmen aufteilen, Teilen und Verbinden. Alle Funktionen liefern neue Listen (sortiert nach Beginn
/// und Tonhöhe) und lassen die Eingabe unverändert.
/// </summary>
public static class NoteTools
{
    public const int MinPitch = 0;
    public const int MaxPitch = 127;

    /// <summary>Sortiert nach Beginn, dann Tonhöhe.</summary>
    public static IReadOnlyList<Note> Sorted(IEnumerable<Note> notes) =>
        notes.OrderBy(n => n.Start).ThenBy(n => n.Pitch).ToArray();

    /// <summary>
    /// Quantisieren: rückt Notenanfänge auf das Raster. <paramref name="strength"/> 1 = ganz, 0,5 = halb so weit
    /// (klingt weniger starr). Mit <paramref name="ends"/> werden auch die Notenenden aufs Raster gesetzt.
    /// </summary>
    public static IReadOnlyList<Note> Quantize(IEnumerable<Note> notes, long gridTicks, double strength = 1, bool ends = false)
    {
        gridTicks = Math.Max(1, gridTicks);
        strength = Math.Clamp(strength, 0, 1);
        return Sorted(notes.Select(n =>
        {
            long target = (long)Math.Round(n.Start / (double)gridTicks) * gridTicks;
            long start = n.Start + (long)Math.Round((target - n.Start) * strength);
            long length = n.Length;
            if (ends)
            {
                long end = start + length;
                long endTarget = Math.Max(target + gridTicks, (long)Math.Round(end / (double)gridTicks) * gridTicks);
                length = Math.Max(1, end + (long)Math.Round((endTarget - end) * strength) - start);
            }

            return n with { Start = Math.Max(0, start), Length = Math.Max(1, length) };
        }));
    }

    /// <summary>
    /// Humanisieren: verschiebt Notenanfänge zufällig um bis zu ±<paramref name="timingTicks"/> und ändert die
    /// Anschlagstärke um bis zu ±<paramref name="velocityRange"/>. Mit festem <paramref name="seed"/> wiederholbar.
    /// </summary>
    public static IReadOnlyList<Note> Humanize(IEnumerable<Note> notes, long timingTicks, int velocityRange, int seed = 1)
    {
        var random = new Random(seed);
        return Sorted(notes.Select(n =>
        {
            long shift = timingTicks > 0 ? random.NextInt64(-timingTicks, timingTicks + 1) : 0;
            int velocity = velocityRange > 0 ? n.Velocity + random.Next(-velocityRange, velocityRange + 1) : n.Velocity;
            return n with { Start = Math.Max(0, n.Start + shift), Velocity = Math.Clamp(velocity, 1, 127) };
        }));
    }

    /// <summary>Transponieren um Halbtöne (12 = eine Oktave). Noten außerhalb des MIDI-Bereichs bleiben am Rand.</summary>
    public static IReadOnlyList<Note> Transpose(IEnumerable<Note> notes, int semitones) =>
        Sorted(notes.Select(n => n with { Pitch = Math.Clamp(n.Pitch + semitones, MinPitch, MaxPitch) }));

    /// <summary>Notenlängen mit einem Faktor ändern (0,5 = halb so lang/staccato, 2 = doppelt so lang).</summary>
    public static IReadOnlyList<Note> ScaleLengths(IEnumerable<Note> notes, double factor) =>
        Sorted(notes.Select(n => n with { Length = Math.Max(1, (long)Math.Round(n.Length * Math.Max(0.01, factor))) }));

    /// <summary>Alle Notenlängen auf einen festen Wert setzen (z. B. einen Rasterschritt).</summary>
    public static IReadOnlyList<Note> SetLengths(IEnumerable<Note> notes, long length) =>
        Sorted(notes.Select(n => n with { Length = Math.Max(1, length) }));

    /// <summary>Legato: Jede Note klingt bis zum Beginn der nächsten (späteren) Note derselben Stimme.</summary>
    public static IReadOnlyList<Note> Legato(IEnumerable<Note> notes)
    {
        var sorted = Sorted(notes);
        var result = new List<Note>(sorted.Count);
        foreach (var voice in sorted.GroupBy(n => n.Voice))
        {
            var list = voice.ToArray();
            for (int i = 0; i < list.Length; i++)
            {
                long next = list.Skip(i + 1).Select(n => n.Start).FirstOrDefault(s => s > list[i].Start, -1);
                result.Add(next > 0 ? list[i] with { Length = next - list[i].Start } : list[i]);
            }
        }

        return Sorted(result);
    }

    /// <summary>Anschlagstärke aller Noten auf einen Wert setzen.</summary>
    public static IReadOnlyList<Note> SetVelocity(IEnumerable<Note> notes, int velocity) =>
        Sorted(notes.Select(n => n with { Velocity = Math.Clamp(velocity, 1, 127) }));

    /// <summary>Anschlagstärke skalieren und verschieben (z. B. Dynamik verringern: Faktor 0,5, Versatz 32).</summary>
    public static IReadOnlyList<Note> ScaleVelocity(IEnumerable<Note> notes, double factor, int offset = 0) =>
        Sorted(notes.Select(n => n with { Velocity = Math.Clamp((int)Math.Round(n.Velocity * factor) + offset, 1, 127) }));

    /// <summary>Anschlagstärke von <paramref name="from"/> nach <paramref name="to"/> verlaufen lassen (Crescendo/Decrescendo).</summary>
    public static IReadOnlyList<Note> VelocityRamp(IEnumerable<Note> notes, int from, int to)
    {
        var sorted = Sorted(notes);
        if (sorted.Count == 0)
        {
            return sorted;
        }

        long first = sorted[0].Start;
        long last = sorted[^1].Start;
        return sorted.Select(n =>
        {
            double t = last == first ? 0 : (n.Start - first) / (double)(last - first);
            return n with { Velocity = Math.Clamp((int)Math.Round(from + (to - from) * t), 1, 127) };
        }).ToArray();
    }

    /// <summary>
    /// Stimmaufteilung: Gleichzeitig klingende Noten werden von oben nach unten auf Stimmen verteilt
    /// (Stimme 0 = höchste Note, z. B. Melodie; die letzte Stimme sammelt alle tieferen Noten).
    /// </summary>
    public static IReadOnlyList<Note> SplitVoices(IEnumerable<Note> notes, int voices)
    {
        voices = Math.Max(1, voices);
        var sorted = Sorted(notes);
        var result = new Note[sorted.Count];
        for (int i = 0; i < sorted.Count; i++)
        {
            var note = sorted[i];
            // Rang = Anzahl höherer Noten, die zum Beginn dieser Note klingen
            int higher = sorted.Count(o => o.Pitch > note.Pitch && o.Start <= note.Start && o.End > note.Start);
            result[i] = note with { Voice = Math.Min(higher, voices - 1) };
        }

        return result;
    }

    /// <summary>Stimmaufteilung an einem Splitpunkt: Noten ab <paramref name="splitPitch"/> = Stimme 0 (rechte Hand), darunter Stimme 1.</summary>
    public static IReadOnlyList<Note> SplitAtPitch(IEnumerable<Note> notes, int splitPitch) =>
        Sorted(notes.Select(n => n with { Voice = n.Pitch >= splitPitch ? 0 : 1 }));

    /// <summary>
    /// Teilt Noten an <paramref name="tick"/>: Links bleiben die Noten davor (über die Grenze klingende
    /// werden gekürzt), rechts die Noten danach (relativ zum Schnitt, angeschnittene beginnen bei 0).
    /// </summary>
    public static (IReadOnlyList<Note> Left, IReadOnlyList<Note> Right) Split(IEnumerable<Note> notes, long tick)
    {
        var left = new List<Note>();
        var right = new List<Note>();
        foreach (var n in notes)
        {
            if (n.End <= tick)
            {
                left.Add(n);
            }
            else if (n.Start >= tick)
            {
                right.Add(n with { Start = n.Start - tick });
            }
            else
            {
                left.Add(n with { Length = tick - n.Start });
                right.Add(n with { Start = 0, Length = n.End - tick });
            }
        }

        return (Sorted(left), Sorted(right));
    }

    /// <summary>Verbindet zwei Notenlisten; die zweite beginnt bei <paramref name="offset"/> Ticks.</summary>
    public static IReadOnlyList<Note> Merge(IEnumerable<Note> first, IEnumerable<Note> second, long offset) =>
        Sorted(first.Concat(second.Select(n => n with { Start = n.Start + offset })));

    /// <summary>Verschiebt Noten in der Zeit (und optional in der Tonhöhe); nichts rutscht vor den Anfang.</summary>
    public static IReadOnlyList<Note> Move(IEnumerable<Note> notes, long ticks, int semitones = 0) =>
        Sorted(notes.Select(n => n with
        {
            Start = Math.Max(0, n.Start + ticks),
            Pitch = Math.Clamp(n.Pitch + semitones, MinPitch, MaxPitch),
        }));

    /// <summary>Ende der letzten Note (0 ohne Noten).</summary>
    public static long EndOf(IEnumerable<Note> notes) => notes.Select(n => n.End).DefaultIfEmpty(0).Max();
}
