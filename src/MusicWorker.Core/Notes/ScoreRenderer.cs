using System.Globalization;
using System.Text;

namespace MusicWorker.Core.Notes;

/// <summary>Ausrichtung eines Textes zu seinem x-Wert.</summary>
public enum TextAnchor
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Zeichenfläche für das Notenblatt. Alles ist schwarz auf weiß; Koordinaten in geräteunabhängigen Pixeln.
/// Pfade verwenden absolute Befehle (M, L, C, Q, Z) mit „x,y“-Paaren – das verstehen WPF und SVG gleichermaßen.
/// </summary>
public interface IScoreCanvas
{
    void Line(double x1, double y1, double x2, double y2, double thickness);

    void Rect(double x, double y, double width, double height);

    void Ellipse(double cx, double cy, double rx, double ry, double rotationDegrees, bool filled, double strokeThickness);

    void Path(string data, bool filled, double strokeThickness);

    /// <summary>Text mit der Grundlinie bei <paramref name="baseline"/>.</summary>
    void Text(string text, double x, double baseline, double size, bool bold, bool italic, TextAnchor anchor);
}

/// <summary>Zeichnet Seiten einer <see cref="Score"/> mit dem Ergebnis von <see cref="ScoreLayout.Paginate"/>.</summary>
public sealed class ScoreRenderer
{
    // Vorzeichen-Positionen im Violinschlüssel (0 = unterste Linie)
    private static readonly int[] SharpPositions = [8, 5, 9, 6, 3, 7, 4];
    private static readonly int[] FlatPositions = [4, 7, 3, 6, 2, 5, 1];

    private readonly Score _score;
    private readonly ScoreLayoutOptions _o;
    private readonly double _s;
    private readonly int _unitsPerBar;
    private readonly int _pageCount;

    public ScoreRenderer(Score score, ScoreLayoutOptions options, int pageCount)
    {
        _score = score ?? throw new ArgumentNullException(nameof(score));
        _o = options ?? throw new ArgumentNullException(nameof(options));
        _s = options.StaffSpace;
        _unitsPerBar = score.BeatsPerBar * 4;
        _pageCount = pageCount;
    }

    public void RenderPage(LayoutPage page, IScoreCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(canvas);
        if (page.Number == 1)
        {
            canvas.Text(_score.Title, _o.PageWidth / 2, _o.Margin + 26, 22, bold: true, italic: false, TextAnchor.Center);
            canvas.Text(string.Create(CultureInfo.InvariantCulture, $"♩ = {Math.Round(_score.Bpm)}"), _o.Margin + _o.NameWidth, _o.Margin + _o.TitleHeight - 22, 12, bold: true, italic: false, TextAnchor.Left);
        }

        if (_pageCount > 1)
        {
            canvas.Text($"Seite {page.Number} von {_pageCount}", _o.PageWidth / 2, _o.PageHeight - (_o.Margin / 2), 9, bold: false, italic: false, TextAnchor.Center);
        }

        foreach (var system in page.Systems)
        {
            RenderSystem(system, canvas);
        }
    }

    private double StaffTop(LayoutSystem system, int staffIndex) =>
        system.Top + (staffIndex * (_o.StaffHeight + _o.StaffGap));

    private double Y(double staffTop, double position) => staffTop + ((8 - position) * _s / 2);

    private void RenderSystem(LayoutSystem system, IScoreCanvas canvas)
    {
        double left = _o.Margin + _o.NameWidth;
        double right = left + system.HeaderWidth + system.MeasureWidths.Sum();
        var staves = _score.Parts.SelectMany((p, pi) => p.Staves.Select((st, si) => (Part: pi, Index: si, Staff: st))).ToArray();
        double lastBottom = StaffTop(system, staves.Length - 1) + _o.StaffHeight;

        // Linien, Stimmnamen, Klammer
        for (int k = 0; k < staves.Length; k++)
        {
            double top = StaffTop(system, k);
            for (int line = 0; line < 5; line++)
            {
                canvas.Line(left, top + (line * _s), right, top + (line * _s), 0.8);
            }
        }

        int staffIndex = 0;
        foreach (var part in _score.Parts)
        {
            double top = StaffTop(system, staffIndex);
            double bottom = StaffTop(system, staffIndex + part.Staves.Count - 1) + _o.StaffHeight;
            canvas.Text(part.Name, left - (_s * (part.Staves.Count > 1 ? 3.2 : 1)), ((top + bottom) / 2) + 3.5, 9.5, bold: false, italic: false, TextAnchor.Right);
            if (part.Staves.Count > 1)
            {
                Brace(canvas, left - (_s * 1.2), top, bottom);
            }

            staffIndex += part.Staves.Count;
        }

        canvas.Line(left, StaffTop(system, 0), left, lastBottom, 1.2);
        if (system.FirstMeasure > 0)
        {
            canvas.Text((system.FirstMeasure + 1).ToString(CultureInfo.InvariantCulture), left, StaffTop(system, 0) - (_s * 1.5), 8, bold: false, italic: true, TextAnchor.Left);
        }

        // Schlüssel, Vorzeichen, Taktart
        for (int k = 0; k < staves.Length; k++)
        {
            double top = StaffTop(system, k);
            var clef = staves[k].Staff.Clef;
            double x = left + (_s * 0.6);
            if (clef == Clef.Treble)
            {
                TrebleClef(canvas, x + (_s * 1.3), Y(top, 2));
            }
            else
            {
                BassClef(canvas, x + (_s * 0.4), Y(top, 6));
            }

            x = left + (_s * 5.2);
            var positions = _score.Key.Fifths >= 0 ? SharpPositions : FlatPositions;
            for (int i = 0; i < Math.Abs(_score.Key.Fifths); i++)
            {
                double y = Y(top, positions[i] - (clef == Clef.Bass ? 2 : 0));
                Accidental(canvas, x + (i * _s * 1.1) + (_s * 0.5), y, Math.Sign(_score.Key.Fifths));
            }

            if (system.ShowTimeSignature)
            {
                double tx = left + system.HeaderWidth - (_s * 2.3);
                double size = _s * 2.9;
                canvas.Text(_score.BeatsPerBar.ToString(CultureInfo.InvariantCulture), tx, Y(top, 4) - 0.5, size, bold: true, italic: false, TextAnchor.Center);
                canvas.Text("4", tx, Y(top, 0) - 0.5, size, bold: true, italic: false, TextAnchor.Center);
            }
        }

        // Takte
        double measureLeft = left + system.HeaderWidth;
        var ties = new Dictionary<int, List<(int Pitch, double X, double Y, bool Above)>>();
        for (int i = 0; i < system.MeasureCount; i++)
        {
            int m = system.FirstMeasure + i;
            double width = system.MeasureWidths[i];
            var (offsets, natural) = ScoreLayout.EventOffsets(_score, m, _o);
            double scale = width / natural;
            for (int k = 0; k < staves.Length; k++)
            {
                if (!ties.TryGetValue(k, out var pending))
                {
                    pending = [];
                    ties[k] = pending;
                }

                RenderMeasure(canvas, staves[k].Staff, m, StaffTop(system, k), measureLeft, width, offsets, scale, pending);
            }

            measureLeft += width;
            bool last = m == _score.MeasureCount - 1;
            staffIndex = 0;
            foreach (var part in _score.Parts)
            {
                double top = StaffTop(system, staffIndex);
                double bottom = StaffTop(system, staffIndex + part.Staves.Count - 1) + _o.StaffHeight;
                if (last)
                {
                    canvas.Line(measureLeft - (_s * 0.9), top, measureLeft - (_s * 0.9), bottom, 0.9);
                    canvas.Rect(measureLeft - (_s * 0.5), top, _s * 0.5, bottom - top);
                }
                else
                {
                    canvas.Line(measureLeft, top, measureLeft, bottom, 0.9);
                }

                staffIndex += part.Staves.Count;
            }
        }

        // Haltebögen, die über das Zeilenende gehen
        foreach (var (k, pending) in ties)
        {
            foreach (var t in pending)
            {
                Tie(canvas, t.X, right - (_s * 0.3), t.Y, t.Above);
            }
        }
    }

    private void RenderMeasure(
        IScoreCanvas canvas, NotationStaff staff, int measure, double top, double left, double width,
        IReadOnlyDictionary<int, double> offsets, double scale, List<(int Pitch, double X, double Y, bool Above)> pendingTies)
    {
        if (measure >= staff.Measures.Count)
        {
            return;
        }

        var events = staff.Measures[measure].Events;
        var beamGroups = Notation.BeamGroups(staff.Measures[measure]);
        var beamed = new Dictionary<int, IReadOnlyList<int>>();
        foreach (var group in beamGroups)
        {
            foreach (int index in group)
            {
                beamed[index] = group;
            }
        }

        // Halsrichtung je Balkengruppe gemeinsam
        var groupDown = beamGroups.ToDictionary(g => g, g => g.SelectMany(i => events[i].Notes).Average(n => Position(n, staff.Clef)) >= 4);
        var stemEnds = new Dictionary<int, (double X, double Y)>();
        var groupBeamY = beamGroups.ToDictionary(g => g, g =>
        {
            bool down = groupDown[g];
            var ys = g.SelectMany(i => events[i].Notes).Select(n => Y(top, Position(n, staff.Clef))).ToArray();
            return down ? ys.Max() + (_s * 3.5) : ys.Min() - (_s * 3.5);
        });

        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            double x = left + (offsets.TryGetValue(e.Start, out double o) ? o * scale : _s * 2);
            if (e.IsRest)
            {
                if (e.FullMeasure)
                {
                    x = left + (width / 2);
                }

                Rest(canvas, x, top, e.Value, e.Dotted);
                continue;
            }

            var positions = e.Notes.Select(n => Position(n, staff.Clef)).ToArray();
            bool down = beamed.TryGetValue(i, out var bg) ? groupDown[bg] : positions.Average() >= 4;
            double rx = _s * 0.62;
            double ry = _s * 0.46;

            // Hilfslinien
            double ledgerHalf = rx * 1.7;
            for (int p = -2; p >= positions.Min(); p -= 2)
            {
                canvas.Line(x - ledgerHalf, Y(top, p), x + ledgerHalf, Y(top, p), 0.9);
            }

            for (int p = 10; p <= positions.Max(); p += 2)
            {
                canvas.Line(x - ledgerHalf, Y(top, p), x + ledgerHalf, Y(top, p), 0.9);
            }

            // Notenköpfe (Sekunden versetzt), Versetzungszeichen, Punkte
            var order = Enumerable.Range(0, e.Notes.Count).OrderBy(k => positions[k]).ToArray();
            int previous = int.MinValue;
            bool previousShifted = false;
            int accidentalColumn = 0;
            foreach (int k in order)
            {
                var note = e.Notes[k];
                int p = positions[k];
                bool shift = p - previous == 1 && !previousShifted;
                double nx = shift ? x + ((down ? -2 : 2) * rx * 0.95) : x;
                double ny = Y(top, p);
                bool filled = e.Value >= 4;
                canvas.Ellipse(nx, ny, rx, ry, -20, filled, filled ? 0 : 1.3);
                if (note.Accidental is { } acc)
                {
                    Accidental(canvas, x - rx - (_s * 0.9) - (accidentalColumn * _s * 1.1), ny, acc);
                    accidentalColumn = (accidentalColumn + 1) % 2;
                }

                if (e.Dotted)
                {
                    double dy = p % 2 == 0 ? ny - (_s / 2) : ny;
                    canvas.Ellipse(x + rx + (_s * 0.7), dy, _s * 0.18, _s * 0.18, 0, filled: true, 0);
                }

                // Haltebögen
                int existing = pendingTies.FindIndex(t => t.Pitch == note.Pitch);
                if (existing >= 0)
                {
                    var t = pendingTies[existing];
                    Tie(canvas, t.X, nx - rx, t.Y, t.Above);
                    pendingTies.RemoveAt(existing);
                }

                if (note.TieToNext)
                {
                    pendingTies.Add((note.Pitch, nx + rx, ny, down));
                }

                previous = p;
                previousShifted = shift;
            }

            // Hals, Fähnchen
            if (e.Value >= 2)
            {
                double stemX = down ? x - (rx * 0.92) : x + (rx * 0.92);
                double fromY = Y(top, down ? positions.Max() : positions.Min());
                double toY;
                if (beamed.TryGetValue(i, out var group))
                {
                    toY = groupBeamY[group];
                }
                else
                {
                    double extreme = Y(top, down ? positions.Min() : positions.Max());
                    toY = down ? extreme + (_s * 3.5) : extreme - (_s * 3.5);
                    toY = down ? Math.Max(toY, Y(top, 4)) : Math.Min(toY, Y(top, 4)); // weit entfernte Noten: Hals bis zur Mitte
                }

                canvas.Line(stemX, fromY, stemX, toY, 1.1);
                stemEnds[i] = (stemX, toY);
                if (!beamed.ContainsKey(i) && e.Value >= 8)
                {
                    Flags(canvas, stemX, toY, down, e.Value == 16 ? 2 : 1);
                }
            }
        }

        // Balken
        foreach (var group in beamGroups)
        {
            bool down = groupDown[group];
            double thickness = _s * 0.5;
            var first = stemEnds[group[0]];
            var last = stemEnds[group[^1]];
            BeamBar(canvas, first.X, last.X, first.Y, thickness, down);
            for (int g = 0; g < group.Count; g++)
            {
                if (events[group[g]].Value < 16)
                {
                    continue;
                }

                var here = stemEnds[group[g]];
                double y = down ? here.Y - (_s * 0.8) : here.Y + (_s * 0.8);
                bool nextSixteenth = g + 1 < group.Count && events[group[g + 1]].Value >= 16;
                bool previousSixteenth = g > 0 && events[group[g - 1]].Value >= 16;
                if (nextSixteenth)
                {
                    BeamBar(canvas, here.X, stemEnds[group[g + 1]].X, y, thickness, down);
                }
                else if (!previousSixteenth)
                {
                    double stub = g > 0 ? here.X - _s : here.X + _s;
                    BeamBar(canvas, Math.Min(here.X, stub), Math.Max(here.X, stub), y, thickness, down);
                }
            }
        }
    }

    private static int Position(NotationNote note, Clef clef) => Notation.StaffPosition(note.Spelling, clef);

    private void BeamBar(IScoreCanvas canvas, double x1, double x2, double y, double thickness, bool down)
    {
        double top = down ? y - thickness : y;
        canvas.Rect(Math.Min(x1, x2) - 0.5, top, Math.Abs(x2 - x1) + 1, thickness);
    }

    private void Tie(IScoreCanvas canvas, double x1, double x2, double y, bool above)
    {
        if (x2 - x1 < _s)
        {
            x2 = x1 + _s;
        }

        double dir = above ? -1 : 1;
        double y0 = y + (dir * _s * 0.7);
        double bulge = dir * _s * Math.Min(1.4, 0.6 + ((x2 - x1) / (_s * 20)));
        canvas.Path(
            string.Create(CultureInfo.InvariantCulture,
                $"M {x1:0.##},{y0:0.##} C {x1 + ((x2 - x1) * 0.25):0.##},{y0 + bulge:0.##} {x2 - ((x2 - x1) * 0.25):0.##},{y0 + bulge:0.##} {x2:0.##},{y0:0.##} " +
                $"C {x2 - ((x2 - x1) * 0.25):0.##},{y0 + (bulge * 0.7):0.##} {x1 + ((x2 - x1) * 0.25):0.##},{y0 + (bulge * 0.7):0.##} {x1:0.##},{y0:0.##} Z"),
            filled: true,
            0.4);
    }

    private void Flags(IScoreCanvas canvas, double x, double y, bool down, int count)
    {
        double d = down ? -1 : 1;
        for (int f = 0; f < count; f++)
        {
            double y0 = y + (d * f * _s * 0.85);
            canvas.Path(
                string.Create(CultureInfo.InvariantCulture,
                    $"M {x:0.##},{y0:0.##} C {x + (_s * 0.15):0.##},{y0 + (d * _s * 1.1):0.##} {x + (_s * 1.4):0.##},{y0 + (d * _s * 1.4):0.##} {x + (_s * 0.95):0.##},{y0 + (d * _s * 2.9):0.##} " +
                    $"C {x + (_s * 1.1):0.##},{y0 + (d * _s * 1.8):0.##} {x + (_s * 0.4):0.##},{y0 + (d * _s * 1.4):0.##} {x:0.##},{y0 + (d * _s * 1.0):0.##} Z"),
                filled: true,
                0.5);
        }
    }

    private void Rest(IScoreCanvas canvas, double x, double top, int value, bool dotted)
    {
        double s = _s;
        switch (value)
        {
            case 1:
                canvas.Rect(x - (s * 0.6), Y(top, 6), s * 1.2, s * 0.5);
                break;
            case 2:
                canvas.Rect(x - (s * 0.6), Y(top, 4) - (s * 0.5), s * 1.2, s * 0.5);
                break;
            case 4:
                double cy = Y(top, 4);
                canvas.Path(
                    string.Create(CultureInfo.InvariantCulture,
                        $"M {x - (s * 0.25):0.##},{cy - (s * 1.5):0.##} L {x + (s * 0.45):0.##},{cy - (s * 0.7):0.##} L {x - (s * 0.1):0.##},{cy - (s * 0.05):0.##} " +
                        $"L {x + (s * 0.45):0.##},{cy + (s * 0.65):0.##} Q {x - (s * 0.45):0.##},{cy + (s * 0.35):0.##} {x + (s * 0.05):0.##},{cy + (s * 1.45):0.##}"),
                    filled: false,
                    s * 0.3);
                break;
            default:
                int hooks = value >= 16 ? 2 : 1;
                double baseY = Y(top, 4);
                double topY = baseY - (s * 0.9);
                canvas.Line(x + (s * 0.45), topY, x - (s * 0.25), baseY + (s * (hooks == 2 ? 1.9 : 1.2)), 1.2);
                for (int h = 0; h < hooks; h++)
                {
                    double hy = topY + (h * s);
                    double hx = x + (s * 0.45) - (h * s * 0.4);
                    canvas.Ellipse(hx - (s * 0.75), hy - (s * 0.05), s * 0.24, s * 0.24, 0, filled: true, 0);
                    canvas.Path(
                        string.Create(CultureInfo.InvariantCulture,
                            $"M {hx - (s * 0.75):0.##},{hy + (s * 0.15):0.##} Q {hx - (s * 0.25):0.##},{hy + (s * 0.35):0.##} {hx:0.##},{hy:0.##}"),
                        filled: false,
                        1.1);
                }

                break;
        }

        if (dotted)
        {
            canvas.Ellipse(x + (s * 1.1), Y(top, 5), s * 0.18, s * 0.18, 0, filled: true, 0);
        }
    }

    /// <summary>Kreuz (+1), B (−1) oder Auflösungszeichen (0), mittig bei (x, y).</summary>
    private void Accidental(IScoreCanvas canvas, double x, double y, int kind)
    {
        double s = _s;
        switch (kind)
        {
            case > 0:
                canvas.Line(x - (s * 0.22), y - (s * 1.3), x - (s * 0.22), y + (s * 1.4), 0.9);
                canvas.Line(x + (s * 0.22), y - (s * 1.4), x + (s * 0.22), y + (s * 1.3), 0.9);
                canvas.Line(x - (s * 0.55), y - (s * 0.25), x + (s * 0.55), y - (s * 0.55), s * 0.28);
                canvas.Line(x - (s * 0.55), y + (s * 0.55), x + (s * 0.55), y + (s * 0.25), s * 0.28);
                break;
            case < 0:
                canvas.Line(x - (s * 0.35), y - (s * 1.9), x - (s * 0.35), y + (s * 0.5), 1);
                canvas.Path(
                    string.Create(CultureInfo.InvariantCulture,
                        $"M {x - (s * 0.35):0.##},{y - (s * 0.05):0.##} C {x + (s * 0.2):0.##},{y - (s * 0.75):0.##} {x + (s * 0.9):0.##},{y - (s * 0.2):0.##} {x - (s * 0.35):0.##},{y + (s * 0.5):0.##}"),
                    filled: false,
                    1.3);
                break;
            default:
                canvas.Line(x - (s * 0.3), y - (s * 1.4), x - (s * 0.3), y + (s * 0.6), 0.9);
                canvas.Line(x + (s * 0.3), y - (s * 0.6), x + (s * 0.3), y + (s * 1.4), 0.9);
                canvas.Line(x - (s * 0.3), y - (s * 0.2), x + (s * 0.3), y - (s * 0.45), s * 0.28);
                canvas.Line(x - (s * 0.3), y + (s * 0.45), x + (s * 0.3), y + (s * 0.2), s * 0.28);
                break;
        }
    }

    /// <summary>Violinschlüssel; (x, y) = Mitte der Spirale auf der G-Linie.</summary>
    private void TrebleClef(IScoreCanvas canvas, double x, double y)
    {
        double s = _s;
        string P(double dx, double dy) => string.Create(CultureInfo.InvariantCulture, $"{x + (dx * s):0.##},{y + (dy * s):0.##}");
        string path =
            $"M {P(0.15, -0.35)} C {P(-0.35, -0.45)} {P(-0.45, 0.35)} {P(0.05, 0.45)} " +
            $"C {P(0.75, 0.55)} {P(1.05, -0.35)} {P(0.55, -0.9)} " +
            $"C {P(0.1, -1.4)} {P(-0.85, -0.9)} {P(-0.9, 0.0)} " +
            $"C {P(-0.95, 0.9)} {P(0.0, 1.35)} {P(0.8, 0.95)} " +
            $"C {P(1.3, 0.6)} {P(0.9, -1.4)} {P(0.4, -2.5)} " +
            $"C {P(0.1, -3.2)} {P(0.2, -4.3)} {P(0.6, -4.6)} " +
            $"C {P(0.95, -4.8)} {P(0.9, -3.8)} {P(0.3, -3.0)} " +
            $"C {P(-0.2, -2.3)} {P(0.0, -1.0)} {P(0.15, 0.5)} " +
            $"L {P(0.35, 2.4)} " +
            $"C {P(0.45, 3.1)} {P(-0.3, 3.3)} {P(-0.45, 2.7)}";
        canvas.Path(path, filled: false, s * 0.24);
        canvas.Ellipse(x - (s * 0.3), y + (s * 2.65), s * 0.3, s * 0.3, 0, filled: true, 0);
    }

    /// <summary>Bassschlüssel; (x, y) = Kopf auf der F-Linie.</summary>
    private void BassClef(IScoreCanvas canvas, double x, double y)
    {
        double s = _s;
        string P(double dx, double dy) => string.Create(CultureInfo.InvariantCulture, $"{x + (dx * s):0.##},{y + (dy * s):0.##}");
        canvas.Ellipse(x + (s * 0.35), y, s * 0.38, s * 0.38, 0, filled: true, 0);
        canvas.Path(
            $"M {P(0.1, -0.2)} C {P(0.3, -1.3)} {P(2.4, -1.4)} {P(2.4, 0.2)} C {P(2.4, 1.6)} {P(1.2, 2.6)} {P(0.0, 3.1)} " +
            $"C {P(1.6, 2.2)} {P(2.0, 1.4)} {P(2.0, 0.2)} C {P(2.0, -1.0)} {P(0.8, -1.0)} {P(0.1, -0.2)} Z",
            filled: true,
            0.6);
        canvas.Ellipse(x + (s * 3.0), y - (s * 0.5), s * 0.2, s * 0.2, 0, filled: true, 0);
        canvas.Ellipse(x + (s * 3.0), y + (s * 0.5), s * 0.2, s * 0.2, 0, filled: true, 0);
    }

    /// <summary>Geschweifte Klammer links vor einem Klaviersystem.</summary>
    private void Brace(IScoreCanvas canvas, double x, double top, double bottom)
    {
        double mid = (top + bottom) / 2;
        double w = _s * 1.1;
        string path = string.Create(CultureInfo.InvariantCulture,
            $"M {x:0.##},{top:0.##} C {x - w:0.##},{top + ((mid - top) * 0.3):0.##} {x + (w * 0.4):0.##},{mid - ((mid - top) * 0.3):0.##} {x - w:0.##},{mid:0.##} " +
            $"C {x + (w * 0.4):0.##},{mid + ((bottom - mid) * 0.3):0.##} {x - w:0.##},{bottom - ((bottom - mid) * 0.3):0.##} {x:0.##},{bottom:0.##} " +
            $"C {x - (w * 0.55):0.##},{bottom - ((bottom - mid) * 0.3):0.##} {x + (w * 0.8):0.##},{mid + ((bottom - mid) * 0.3):0.##} {x - w:0.##},{mid:0.##} " +
            $"C {x + (w * 0.8):0.##},{mid - ((mid - top) * 0.3):0.##} {x - (w * 0.55):0.##},{top + ((mid - top) * 0.3):0.##} {x:0.##},{top:0.##} Z");
        canvas.Path(path, filled: true, 0.4);
    }
}

/// <summary>Zeichnet in eine SVG-Datei (für Vorschau, Tests und den SVG-Export).</summary>
public sealed class SvgScoreCanvas : IScoreCanvas
{
    private readonly StringBuilder _svg = new();
    private readonly double _width;
    private readonly double _height;

    public SvgScoreCanvas(double width, double height)
    {
        _width = width;
        _height = height;
    }

    public int ElementCount { get; private set; }

    public void Line(double x1, double y1, double x2, double y2, double thickness) =>
        Add($"<line x1=\"{F(x1)}\" y1=\"{F(y1)}\" x2=\"{F(x2)}\" y2=\"{F(y2)}\" stroke=\"#000\" stroke-width=\"{F(thickness)}\" stroke-linecap=\"butt\"/>");

    public void Rect(double x, double y, double width, double height) =>
        Add($"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(width)}\" height=\"{F(height)}\" fill=\"#000\"/>");

    public void Ellipse(double cx, double cy, double rx, double ry, double rotationDegrees, bool filled, double strokeThickness) =>
        Add($"<ellipse cx=\"{F(cx)}\" cy=\"{F(cy)}\" rx=\"{F(rx)}\" ry=\"{F(ry)}\" transform=\"rotate({F(rotationDegrees)} {F(cx)} {F(cy)})\" " +
            (filled ? "fill=\"#000\"/>" : $"fill=\"none\" stroke=\"#000\" stroke-width=\"{F(strokeThickness)}\"/>"));

    public void Path(string data, bool filled, double strokeThickness) =>
        Add($"<path d=\"{data}\" " + (filled
            ? $"fill=\"#000\" stroke=\"#000\" stroke-width=\"{F(strokeThickness)}\"/>"
            : $"fill=\"none\" stroke=\"#000\" stroke-width=\"{F(strokeThickness)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>"));

    public void Text(string text, double x, double baseline, double size, bool bold, bool italic, TextAnchor anchor)
    {
        string a = anchor switch { TextAnchor.Center => "middle", TextAnchor.Right => "end", _ => "start" };
        Add($"<text x=\"{F(x)}\" y=\"{F(baseline)}\" font-family=\"Georgia, 'Times New Roman', serif\" font-size=\"{F(size)}\" text-anchor=\"{a}\"" +
            (bold ? " font-weight=\"bold\"" : string.Empty) + (italic ? " font-style=\"italic\"" : string.Empty) +
            $">{System.Security.SecurityElement.Escape(text)}</text>");
    }

    public override string ToString() =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(_width)}\" height=\"{F(_height)}\" viewBox=\"0 0 {F(_width)} {F(_height)}\">" +
        $"<rect width=\"100%\" height=\"100%\" fill=\"#fff\"/>{_svg}</svg>";

    private void Add(string element)
    {
        _svg.Append(element).Append('\n');
        ElementCount++;
    }

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
