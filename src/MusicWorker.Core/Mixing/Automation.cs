using System.Globalization;

namespace MusicWorker.Core.Mixing;

/// <summary>Was eine Hüllkurve (Automation) einer Spur steuert.</summary>
public enum AutomationTarget
{
    /// <summary>Lautstärke in dB – wirkt zusätzlich zum Fader (0 dB = Fader-Stellung).</summary>
    Volume,

    /// <summary>Panorama -1 (links) … +1 (rechts) – ersetzt den Panorama-Regler.</summary>
    Pan,

    /// <summary>Send auf den Hall-Bus 0 … 1 – ersetzt den Send-Regler.</summary>
    SendHall,

    /// <summary>Send auf den Echo-Bus 0 … 1 – ersetzt den Send-Regler.</summary>
    SendEcho,

    /// <summary>Hochpass-Frequenz 20 … 2000 Hz – ersetzt den Hochpass-Regler (Filterfahrt).</summary>
    HighPass,
}

/// <summary>Ein Punkt einer Hüllkurve: Position auf der Zeitleiste (Frames) und Wert.</summary>
public readonly record struct AutomationPoint(long Frame, double Value);

/// <summary>Wertebereich und Darstellung eines Automationsziels.</summary>
public sealed record AutomationRange(double Minimum, double Maximum, double Default, bool Logarithmic, string Name)
{
    /// <summary>Wert → 0 … 1 (für die senkrechte Darstellung).</summary>
    public double Normalize(double value)
    {
        value = Math.Clamp(value, Minimum, Maximum);
        return Logarithmic
            ? Math.Log(value / Minimum) / Math.Log(Maximum / Minimum)
            : (value - Minimum) / (Maximum - Minimum);
    }

    /// <summary>0 … 1 → Wert.</summary>
    public double Denormalize(double normalized)
    {
        normalized = Math.Clamp(normalized, 0, 1);
        return Logarithmic
            ? Minimum * Math.Pow(Maximum / Minimum, normalized)
            : Minimum + normalized * (Maximum - Minimum);
    }
}

/// <summary>Wertebereiche, Namen und Anzeige der Automationsziele.</summary>
public static class Automation
{
    private static readonly AutomationRange VolumeRange = new(-60, 6, 0, false, "Lautstärke");
    private static readonly AutomationRange PanRange = new(-1, 1, 0, false, "Panorama");
    private static readonly AutomationRange HallRange = new(0, 1, 0, false, "Hall-Send");
    private static readonly AutomationRange EchoRange = new(0, 1, 0, false, "Echo-Send");
    private static readonly AutomationRange HighPassRange = new(20, 2000, 20, true, "Hochpass");

    public static IReadOnlyList<AutomationTarget> Targets { get; } = Enum.GetValues<AutomationTarget>();

    public static AutomationRange RangeOf(AutomationTarget target) => target switch
    {
        AutomationTarget.Volume => VolumeRange,
        AutomationTarget.Pan => PanRange,
        AutomationTarget.SendHall => HallRange,
        AutomationTarget.SendEcho => EchoRange,
        _ => HighPassRange,
    };

    /// <summary>Wert lesbar darstellen, z. B. „-6,0 dB“, „L 40“, „35 %“, „120 Hz“.</summary>
    public static string Format(AutomationTarget target, double value)
    {
        var culture = CultureInfo.CurrentCulture;
        return target switch
        {
            AutomationTarget.Volume => value <= MixMath.SilenceDb ? "-∞ dB" : string.Format(culture, "{0:+0.0;-0.0;0.0} dB", value),
            AutomationTarget.Pan => Math.Abs(value) < 0.005 ? "Mitte"
                : string.Format(culture, value < 0 ? "L {0:0}" : "R {0:0}", Math.Abs(value) * 100),
            AutomationTarget.HighPass => value <= 20.5 ? "aus" : string.Format(culture, "{0:0} Hz", value),
            _ => string.Format(culture, "{0:0} %", value * 100),
        };
    }
}

/// <summary>
/// Hüllkurve einer Spur: Punkte (nach Position sortiert), dazwischen wird linear übergeblendet
/// (beim Hochpass logarithmisch, damit die Filterfahrt gleichmäßig klingt). Vor dem ersten und nach
/// dem letzten Punkt gilt dessen Wert. Unveränderlich – Bearbeitungen liefern eine neue Hüllkurve.
/// </summary>
public sealed record AutomationLane(AutomationTarget Target, IReadOnlyList<AutomationPoint> Points)
{
    public AutomationRange Range => Automation.RangeOf(Target);

    public bool IsEmpty => Points.Count == 0;

    /// <summary>Wert an einer Stelle der Zeitleiste.</summary>
    public double ValueAt(double frame)
    {
        var points = Points;
        if (points.Count == 0)
        {
            return Range.Default;
        }

        if (frame <= points[0].Frame)
        {
            return points[0].Value;
        }

        if (frame >= points[^1].Frame)
        {
            return points[^1].Value;
        }

        // Binäre Suche nach dem Abschnitt [lo, lo + 1]
        int lo = 0;
        int hi = points.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (points[mid].Frame <= frame)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        var a = points[lo];
        var b = points[hi];
        double t = b.Frame == a.Frame ? 1 : (frame - a.Frame) / (b.Frame - a.Frame);
        if (Range.Logarithmic && a.Value > 0 && b.Value > 0)
        {
            return a.Value * Math.Pow(b.Value / a.Value, t);
        }

        return a.Value + (b.Value - a.Value) * t;
    }

    /// <summary>Fügt einen Punkt ein (an der richtigen Stelle) und liefert dessen Index.</summary>
    public (AutomationLane Lane, int Index) WithPoint(long frame, double value)
    {
        var point = new AutomationPoint(Math.Max(0, frame), Clamp(value));
        var list = Points.ToList();
        int index = list.FindIndex(p => p.Frame > point.Frame);
        if (index < 0)
        {
            index = list.Count;
        }

        list.Insert(index, point);
        return (this with { Points = list }, index);
    }

    /// <summary>Verschiebt einen Punkt – höchstens bis zu seinen Nachbarn, damit die Reihenfolge erhalten bleibt.</summary>
    public AutomationLane WithMovedPoint(int index, long frame, double value)
    {
        if (index < 0 || index >= Points.Count)
        {
            return this;
        }

        long min = index > 0 ? Points[index - 1].Frame : 0;
        long max = index < Points.Count - 1 ? Points[index + 1].Frame : long.MaxValue;
        var list = Points.ToList();
        list[index] = new AutomationPoint(Math.Clamp(frame, min, max), Clamp(value));
        return this with { Points = list };
    }

    public AutomationLane WithoutPoint(int index)
    {
        if (index < 0 || index >= Points.Count)
        {
            return this;
        }

        var list = Points.ToList();
        list.RemoveAt(index);
        return this with { Points = list };
    }

    /// <summary>Werte auf dem Raster: alle <paramref name="step"/> Frames ab <paramref name="start"/> (für die Misch-Engine).</summary>
    internal void Evaluate(long start, int step, Span<float> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = (float)ValueAt(start + (long)i * step);
        }
    }

    private double Clamp(double value) => Math.Clamp(value, Range.Minimum, Range.Maximum);
}

/// <summary>
/// Auto-Absenkung (Ducking): Diese Spur wird leiser, sobald die Quellspur (z. B. Sprache) über die
/// Schwelle kommt, und danach langsam wieder lauter.
/// </summary>
/// <param name="SourceTrack">Index der Quellspur, die die Absenkung auslöst.</param>
/// <param name="AmountDb">Wie stark abgesenkt wird (dB).</param>
/// <param name="ThresholdDb">Ab diesem Pegel der Quellspur wird abgesenkt (dBFS).</param>
/// <param name="AttackMs">Wie schnell abgesenkt wird.</param>
/// <param name="ReleaseMs">Wie langsam die Lautstärke danach zurückkehrt.</param>
public sealed record DuckingSettings(
    int SourceTrack,
    double AmountDb = 12,
    double ThresholdDb = -40,
    double AttackMs = 40,
    double ReleaseMs = 600);
