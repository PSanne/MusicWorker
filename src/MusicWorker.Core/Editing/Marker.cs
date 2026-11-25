namespace MusicWorker.Core.Editing;

/// <summary>Ein benannter Zeitpunkt (z. B. „Strophe“, „Refrain“), gemessen in Frames ab Dateianfang bzw. Projektanfang.</summary>
public sealed record Marker(string Name, long Frame);

/// <summary>Hilfsfunktionen für Markerlisten. Listen sind immer nach Position sortiert und werden nie verändert, sondern neu erzeugt.</summary>
public static class MarkerList
{
    public static IReadOnlyList<Marker> Empty { get; } = [];

    /// <summary>Sortiert nach Position (bei gleicher Position bleibt die Reihenfolge erhalten).</summary>
    public static IReadOnlyList<Marker> Sorted(IEnumerable<Marker> markers) =>
        markers.Select((m, i) => (m, i)).OrderBy(x => x.m.Frame).ThenBy(x => x.i).Select(x => x.m).ToArray();

    /// <summary>Vorschlag für den nächsten Namen: „Marker 1“, „Marker 2“ …</summary>
    public static string NextName(IReadOnlyList<Marker> markers)
    {
        int n = 1;
        while (markers.Any(m => m.Name == $"Marker {n}"))
        {
            n++;
        }

        return $"Marker {n}";
    }

    /// <summary>Erster Marker rechts von <paramref name="frame"/> (null = keiner).</summary>
    public static Marker? Next(IReadOnlyList<Marker> markers, long frame) =>
        markers.Where(m => m.Frame > frame).MinBy(m => m.Frame);

    /// <summary>Letzter Marker links von <paramref name="frame"/> (null = keiner).</summary>
    public static Marker? Previous(IReadOnlyList<Marker> markers, long frame) =>
        markers.Where(m => m.Frame < frame).MaxBy(m => m.Frame);

    public static IReadOnlyList<Marker> Add(IReadOnlyList<Marker> markers, Marker marker) => Sorted(markers.Append(marker));

    public static IReadOnlyList<Marker> RemoveAt(IReadOnlyList<Marker> markers, int index) =>
        markers.Where((_, i) => i != index).ToArray();

    public static IReadOnlyList<Marker> ReplaceAt(IReadOnlyList<Marker> markers, int index, Marker marker) =>
        Sorted(markers.Select((m, i) => i == index ? marker : m));

    /// <summary>
    /// Neue Position eines Markers, nachdem der Bereich [start, start + length) durch
    /// <paramref name="replacementLength"/> Frames ersetzt wurde. Dahinter liegende Marker rücken mit,
    /// Marker in einem gelöschten Bereich fallen weg (null), Marker in einem gestreckten oder
    /// gestauchten Bereich (z. B. Tempoänderung) werden anteilig verschoben.
    /// </summary>
    public static long? MapReplace(long frame, long start, long length, long replacementLength)
    {
        if (frame <= start)
        {
            return frame;
        }

        if (frame >= start + length)
        {
            return frame + replacementLength - length;
        }

        if (replacementLength == 0)
        {
            return null;
        }

        return start + (long)Math.Round((frame - start) * (double)replacementLength / length);
    }

    public static IReadOnlyList<Marker> Replace(IReadOnlyList<Marker> markers, long start, long length, long replacementLength)
    {
        if (markers.Count == 0 || length == replacementLength)
        {
            return markers;
        }

        var result = new List<Marker>(markers.Count);
        foreach (var marker in markers)
        {
            if (MapReplace(marker.Frame, start, length, replacementLength) is { } frame)
            {
                result.Add(marker with { Frame = frame });
            }
        }

        return Sorted(result);
    }

    /// <summary>Alle Marker anteilig auf eine neue Gesamtlänge umrechnen (z. B. nach einer Tempoänderung der ganzen Datei).</summary>
    public static IReadOnlyList<Marker> Scale(IReadOnlyList<Marker> markers, long oldLength, long newLength)
    {
        if (markers.Count == 0 || oldLength <= 0 || oldLength == newLength)
        {
            return markers;
        }

        return Sorted(markers.Select(m => m with { Frame = Math.Clamp((long)Math.Round(m.Frame * (double)newLength / oldLength), 0, newLength) }));
    }
}
