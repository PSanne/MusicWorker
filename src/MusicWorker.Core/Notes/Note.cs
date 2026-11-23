namespace MusicWorker.Core.Notes;

/// <summary>
/// Eine Note: Tonhöhe (MIDI 0 … 127, 60 = C4), Beginn und Länge in Ticks (<see cref="MusicalTime.TicksPerQuarter"/>
/// pro Viertelnote), Anschlagstärke 1 … 127 und Stimme (für die Stimmaufteilung).
/// </summary>
public readonly record struct Note(int Pitch, long Start, long Length, int Velocity = 100, int Voice = 0)
{
    public long End => Start + Length;

    /// <summary>Name der Tonhöhe, z. B. „C4“, „F#3“.</summary>
    public static string PitchName(int pitch)
    {
        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        return $"{names[((pitch % 12) + 12) % 12]}{(pitch / 12) - 1}";
    }

    /// <summary>Frequenz in Hz (A4 = MIDI 69 = 440 Hz).</summary>
    public static double Frequency(double pitch) => 440 * Math.Pow(2, (pitch - 69) / 12);
}

/// <summary>Umrechnung zwischen musikalischer Zeit (Ticks) und Audio-Zeit (Frames).</summary>
public static class MusicalTime
{
    /// <summary>Auflösung: Ticks pro Viertelnote (wie in vielen MIDI-Dateien).</summary>
    public const int TicksPerQuarter = 480;

    public static double FramesPerTick(double bpm, int sampleRate) => 60.0 / Math.Max(1, bpm) / TicksPerQuarter * sampleRate;

    public static long TicksToFrames(long ticks, double bpm, int sampleRate) => (long)Math.Round(ticks * FramesPerTick(bpm, sampleRate));

    public static long FramesToTicks(long frames, double bpm, int sampleRate) => (long)Math.Round(frames / FramesPerTick(bpm, sampleRate));

    /// <summary>Ticks eines Takts (Taktart x/4).</summary>
    public static long TicksPerBar(int beatsPerBar) => (long)Math.Max(1, beatsPerBar) * TicksPerQuarter;

    /// <summary>Notenwert in Ticks: 4 = Viertel, 8 = Achtel, 16 = Sechzehntel …; triplet = Triole.</summary>
    public static long GridTicks(int division, bool triplet = false)
    {
        long ticks = TicksPerQuarter * 4L / Math.Max(1, division);
        return triplet ? ticks * 2 / 3 : ticks;
    }
}
