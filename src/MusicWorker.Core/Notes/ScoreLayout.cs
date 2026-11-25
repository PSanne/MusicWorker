namespace MusicWorker.Core.Notes;

/// <summary>Seitenmaße und Abstände für das Notenblatt (in geräteunabhängigen Pixeln, 96 pro Zoll).</summary>
public sealed record ScoreLayoutOptions
{
    /// <summary>A4 hochkant.</summary>
    public double PageWidth { get; init; } = 794;

    public double PageHeight { get; init; } = 1123;

    public double Margin { get; init; } = 60;

    /// <summary>Abstand zweier Notenlinien.</summary>
    public double StaffSpace { get; init; } = 7;

    /// <summary>Platz für Titel und Tempoangabe auf der ersten Seite.</summary>
    public double TitleHeight { get; init; } = 80;

    /// <summary>Abstand zwischen den Notensystemen einer Stimme (Klaviersystem) bzw. zwischen Stimmen.</summary>
    public double StaffGap { get; init; } = 44;

    /// <summary>Abstand zwischen zwei Notenzeilen.</summary>
    public double SystemGap { get; init; } = 52;

    /// <summary>Platz links für den Stimmnamen.</summary>
    public double NameWidth { get; init; } = 60;

    public double StaffHeight => StaffSpace * 4;
}

/// <summary>Eine Notenzeile: welche Takte sie zeigt und wie breit jeder Takt ist.</summary>
public sealed record LayoutSystem(int FirstMeasure, IReadOnlyList<double> MeasureWidths, double Top, double HeaderWidth, bool ShowTimeSignature)
{
    public int MeasureCount => MeasureWidths.Count;
}

public sealed record LayoutPage(int Number, IReadOnlyList<LayoutSystem> Systems);

/// <summary>Verteilt die Takte einer Partitur auf Notenzeilen und Seiten.</summary>
public static class ScoreLayout
{
    /// <summary>Anzahl Notensysteme (Fünf-Linien-Systeme) in jeder Zeile.</summary>
    public static int StaffCount(Score score) => Math.Max(1, score.Parts.Sum(p => p.Staves.Count));

    /// <summary>Höhe einer Notenzeile (von der obersten bis zur untersten Linie).</summary>
    public static double SystemHeight(Score score, ScoreLayoutOptions options)
    {
        int staves = StaffCount(score);
        return (staves * options.StaffHeight) + ((staves - 1) * options.StaffGap);
    }

    /// <summary>Platz am Zeilenanfang für Schlüssel, Vorzeichen und (in der ersten Zeile) die Taktart.</summary>
    public static double HeaderWidth(Score score, ScoreLayoutOptions options, bool withTimeSignature) =>
        (options.StaffSpace * 5.8) + (Math.Abs(score.Key.Fifths) * options.StaffSpace * 1.1) + (withTimeSignature ? options.StaffSpace * 3 : 0) + options.StaffSpace;

    /// <summary>Mindestbreite eines Takts (alle Notensysteme gemeinsam, damit Gleichzeitiges untereinander steht).</summary>
    public static double MeasureWidth(Score score, int measureIndex, ScoreLayoutOptions options) =>
        EventOffsets(score, measureIndex, options).Width;

    /// <summary>
    /// Waagerechte Lage jedes Einsatzzeitpunkts (in Sechzehnteln ab Taktbeginn) im ungestreckten Takt.
    /// Alle Notensysteme teilen sich diese Positionen.
    /// </summary>
    public static (IReadOnlyDictionary<int, double> Offsets, double Width) EventOffsets(Score score, int measureIndex, ScoreLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(score);
        ArgumentNullException.ThrowIfNull(options);
        double s = options.StaffSpace;
        int unitsPerBar = score.BeatsPerBar * 4;
        var events = score.Parts.SelectMany(p => p.Staves)
            .Where(st => measureIndex < st.Measures.Count)
            .SelectMany(st => st.Measures[measureIndex].Events)
            .ToArray();
        var starts = events.Select(e => e.Start).Append(0).Distinct().OrderBy(x => x).ToList();
        var offsets = new Dictionary<int, double>();
        double x = s * 1.5;
        for (int i = 0; i < starts.Count; i++)
        {
            int start = starts[i];
            int next = i + 1 < starts.Count ? starts[i + 1] : unitsPerBar;
            bool accidental = events.Any(e => e.Start == start && e.Notes.Any(n => n.Accidental is not null));
            if (accidental)
            {
                x += s * 1.4;
            }

            offsets[start] = x;
            x += (s * 2.2) + (s * 1.1 * Math.Sqrt(Math.Max(1, next - start)));
        }

        return (offsets, Math.Max(s * 8, x + s));
    }

    public static IReadOnlyList<LayoutPage> Paginate(Score score, ScoreLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(score);
        options ??= new ScoreLayoutOptions();
        double contentWidth = options.PageWidth - (2 * options.Margin) - options.NameWidth;

        // 1. Takte auf Zeilen verteilen
        var rows = new List<(int First, List<double> Widths, double Header, bool Time)>();
        int m = 0;
        while (m < score.MeasureCount)
        {
            bool time = rows.Count == 0;
            double header = HeaderWidth(score, options, time);
            var widths = new List<double>();
            double used = header;
            while (m < score.MeasureCount)
            {
                double w = MeasureWidth(score, m, options);
                if (widths.Count > 0 && used + w > contentWidth)
                {
                    break;
                }

                widths.Add(w);
                used += w;
                m++;
            }

            // Auf volle Breite strecken (die letzte Zeile nur, wenn sie schon gut gefüllt ist)
            double free = contentWidth - used;
            bool last = m >= score.MeasureCount;
            if (free > 0 && (!last || used / contentWidth > 0.6))
            {
                double total = widths.Sum();
                for (int i = 0; i < widths.Count; i++)
                {
                    widths[i] += free * widths[i] / total;
                }
            }

            rows.Add((m - widths.Count, widths, header, time));
        }

        // 2. Zeilen auf Seiten verteilen
        double systemHeight = SystemHeight(score, options);
        var pages = new List<LayoutPage>();
        var systems = new List<LayoutSystem>();
        double top = options.Margin + options.TitleHeight;
        foreach (var (first, widths, header, time) in rows)
        {
            if (systems.Count > 0 && top + systemHeight > options.PageHeight - options.Margin)
            {
                pages.Add(new LayoutPage(pages.Count + 1, systems));
                systems = [];
                top = options.Margin + options.StaffSpace * 2;
            }

            systems.Add(new LayoutSystem(first, widths, top, header, time));
            top += systemHeight + options.SystemGap;
        }

        pages.Add(new LayoutPage(pages.Count + 1, systems));
        return pages;
    }
}
