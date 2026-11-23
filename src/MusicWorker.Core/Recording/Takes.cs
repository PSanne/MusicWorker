using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Recording;

/// <summary>Ein Abschnitt der Zusammenstellung (Comp): von <see cref="Start"/> bis <see cref="End"/> (Frames, relativ zum Clip) klingt Take <see cref="Take"/>.</summary>
public readonly record struct CompSegment(long Start, long End, int Take)
{
    public long Length => End - Start;
}

/// <summary>
/// Takes und Comping: Mehrere Aufnahmen (Takes) derselben Stelle liegen übereinander; die Zusammenstellung
/// (Comp) bestimmt, welcher Take wo zu hören ist. An den Schnittstellen wird kurz übergeblendet.
/// Alle Takes einer Gruppe beginnen an derselben Stelle (kürzere werden mit Stille aufgefüllt).
/// </summary>
public static class TakeTools
{
    /// <summary>Überblendung an Comp-Grenzen: 10 ms – kurz genug für präzise Schnitte, lang genug gegen Knackser.</summary>
    public const double CrossfadeSeconds = 0.01;

    /// <summary>Gesamtlänge der Gruppe (längster Take).</summary>
    public static long LengthOf(IReadOnlyList<AudioBuffer> takes) => takes.Select(t => (long)t.FrameCount).DefaultIfEmpty(0).Max();

    /// <summary>Comp, der überall denselben Take spielt („Clear-Comp“).</summary>
    public static IReadOnlyList<CompSegment> ClearComp(long length, int take) => length <= 0 ? [] : [new CompSegment(0, length, take)];

    /// <summary>Legt fest, dass im Bereich [start, end) Take <paramref name="take"/> klingt; der Rest bleibt, wie er war.</summary>
    public static IReadOnlyList<CompSegment> Assign(IReadOnlyList<CompSegment> comp, long start, long end, int take, long length)
    {
        start = Math.Clamp(start, 0, length);
        end = Math.Clamp(end, 0, length);
        if (end <= start)
        {
            return Normalize(comp, length);
        }

        var result = new List<CompSegment>();
        foreach (var segment in Normalize(comp, length))
        {
            if (segment.End <= start || segment.Start >= end)
            {
                result.Add(segment);
                continue;
            }

            if (segment.Start < start)
            {
                result.Add(segment with { End = start });
            }

            if (segment.End > end)
            {
                result.Add(segment with { Start = end });
            }
        }

        result.Add(new CompSegment(start, end, take));
        return Normalize(result, length);
    }

    /// <summary>Sortiert, schließt Lücken (mit dem vorigen Take bzw. Take 0) und verbindet gleiche Nachbarn.</summary>
    public static IReadOnlyList<CompSegment> Normalize(IEnumerable<CompSegment> comp, long length)
    {
        var sorted = comp.Where(s => s.End > s.Start).OrderBy(s => s.Start).ToList();
        var result = new List<CompSegment>();
        long position = 0;
        foreach (var segment in sorted)
        {
            long start = Math.Max(segment.Start, position);
            long end = Math.Min(segment.End, length);
            if (end <= start)
            {
                continue;
            }

            if (start > position)
            {
                Append(result, new CompSegment(position, start, result.Count > 0 ? result[^1].Take : segment.Take));
            }

            Append(result, new CompSegment(start, end, segment.Take));
            position = end;
        }

        if (position < length)
        {
            Append(result, new CompSegment(position, length, result.Count > 0 ? result[^1].Take : 0));
        }

        return result;
    }

    /// <summary>
    /// Fügt einen neuen Take hinzu, der bei <paramref name="takeStart"/> beginnt (Frames auf der Zeitleiste).
    /// Alle Takes werden auf einen gemeinsamen Anfang gebracht; der neue Take klingt dort, wo er Audio enthält.
    /// </summary>
    public static (IReadOnlyList<AudioBuffer> Takes, long Start, IReadOnlyList<CompSegment> Comp) AddTake(
        IReadOnlyList<AudioBuffer> takes, long groupStart, IReadOnlyList<CompSegment> comp, AudioBuffer take, long takeStart)
    {
        long start = takes.Count == 0 ? takeStart : Math.Min(groupStart, takeStart);
        long shiftOld = groupStart - start;
        long shiftNew = takeStart - start;
        long length = Math.Max(takes.Count == 0 ? 0 : shiftOld + LengthOf(takes), shiftNew + take.FrameCount);
        int channels = Math.Max(take.Channels, takes.Select(t => t.Channels).DefaultIfEmpty(1).Max());

        var aligned = takes.Select(t => Pad(t, shiftOld, length, channels)).ToList();
        aligned.Add(Pad(take, shiftNew, length, channels));

        var shifted = comp.Select(s => new CompSegment(s.Start + shiftOld, s.End + shiftOld, s.Take)).ToList();
        if (shiftOld > 0 && takes.Count > 0)
        {
            shifted.Insert(0, new CompSegment(0, shiftOld, aligned.Count - 1)); // vor den alten Takes gibt es nur den neuen
        }

        var newComp = takes.Count == 0
            ? ClearComp(length, 0)
            : Assign(Normalize(shifted, length), shiftNew, shiftNew + take.FrameCount, aligned.Count - 1, length);
        return (aligned, start, newComp);
    }

    /// <summary>Entfernt einen Take; seine Abschnitte im Comp übernimmt der nächstneuere (bzw. letzte) Take.</summary>
    public static (IReadOnlyList<AudioBuffer> Takes, IReadOnlyList<CompSegment> Comp) RemoveTake(
        IReadOnlyList<AudioBuffer> takes, IReadOnlyList<CompSegment> comp, int index)
    {
        if (index < 0 || index >= takes.Count || takes.Count <= 1)
        {
            return (takes, comp);
        }

        var remaining = takes.Where((_, i) => i != index).ToArray();
        int replacement = Math.Min(index, remaining.Length - 1);
        var newComp = comp.Select(s => s.Take == index
                ? s with { Take = replacement }
                : s with { Take = s.Take > index ? s.Take - 1 : s.Take })
            .ToArray();
        return (remaining, Normalize(newComp, LengthOf(remaining)));
    }

    /// <summary>Setzt die Zusammenstellung zu einem Audio zusammen (mit kurzen Überblendungen an den Grenzen).</summary>
    public static AudioBuffer Compose(IReadOnlyList<AudioBuffer> takes, IReadOnlyList<CompSegment> comp)
    {
        if (takes.Count == 0)
        {
            throw new ArgumentException("Es gibt keinen Take.", nameof(takes));
        }

        int rate = takes[0].SampleRate;
        int channels = takes.Max(t => t.Channels);
        long length = LengthOf(takes);
        var output = new float[length * channels];
        long fade = Math.Max(1, (long)(CrossfadeSeconds * rate));
        var segments = Normalize(comp, length);

        for (int s = 0; s < segments.Count; s++)
        {
            var segment = segments[s];
            var take = takes[Math.Clamp(segment.Take, 0, takes.Count - 1)];
            // Jeder Abschnitt blendet am Anfang ein und läuft am Ende in den nächsten über (gleiche Leistung).
            long from = Math.Max(0, segment.Start - (s > 0 ? fade / 2 : 0));
            long to = Math.Min(length, segment.End + (s < segments.Count - 1 ? fade / 2 : 0));
            for (long frame = from; frame < to; frame++)
            {
                double gain = 1;
                if (s > 0 && frame < segment.Start + fade / 2)
                {
                    gain = Math.Sin(Math.PI / 2 * (frame - from + 0.5) / fade);
                }
                else if (s < segments.Count - 1 && frame >= segment.End - fade / 2)
                {
                    gain = Math.Cos(Math.PI / 2 * (frame - (segment.End - fade / 2) + 0.5) / fade);
                }

                for (int c = 0; c < channels; c++)
                {
                    output[frame * channels + c] += (float)(Sample(take, frame, c) * gain);
                }
            }
        }

        return new AudioBuffer(output, rate, channels);
    }

    private static float Sample(AudioBuffer take, long frame, int channel)
    {
        if (frame >= take.FrameCount)
        {
            return 0;
        }

        return take.GetSample((int)frame, Math.Min(channel, take.Channels - 1));
    }

    /// <summary>Verschiebt um <paramref name="offset"/> Frames (Stille davor) und füllt bis <paramref name="length"/> auf.</summary>
    private static AudioBuffer Pad(AudioBuffer audio, long offset, long length, int channels)
    {
        if (offset == 0 && audio.FrameCount == length && audio.Channels == channels)
        {
            return audio;
        }

        var samples = new float[length * channels];
        for (long f = 0; f < audio.FrameCount && f + offset < length; f++)
        {
            for (int c = 0; c < channels; c++)
            {
                samples[(f + offset) * channels + c] = audio.GetSample((int)f, Math.Min(c, audio.Channels - 1));
            }
        }

        return new AudioBuffer(samples, audio.SampleRate, channels);
    }

    private static void Append(List<CompSegment> list, CompSegment segment)
    {
        if (list.Count > 0 && list[^1].Take == segment.Take && list[^1].End == segment.Start)
        {
            list[^1] = list[^1] with { End = segment.End };
        }
        else
        {
            list.Add(segment);
        }
    }
}
