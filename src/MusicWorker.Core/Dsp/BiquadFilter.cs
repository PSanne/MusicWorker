using System.Numerics;

namespace MusicWorker.Core.Dsp;

/// <summary>
/// Koeffizienten eines Biquad-Filters (IIR 2. Ordnung), normiert auf a0 = 1.
/// Formeln nach dem „Audio EQ Cookbook“ von Robert Bristow-Johnson.
/// </summary>
public readonly record struct BiquadCoefficients(double B0, double B1, double B2, double A1, double A2)
{
    /// <summary>Lässt das Signal unverändert durch.</summary>
    public static BiquadCoefficients Identity => new(1, 0, 0, 0, 0);

    /// <summary>Glockenfilter (Peaking EQ): hebt/senkt ein Band um <paramref name="gainDb"/> an.</summary>
    public static BiquadCoefficients PeakingEq(double sampleRate, double frequency, double q, double gainDb)
    {
        if (gainDb == 0 || frequency >= sampleRate * 0.49)
        {
            return Identity; // neutral – oder Band liegt oberhalb der darstellbaren Frequenzen
        }

        double a = Math.Pow(10, gainDb / 40);
        double w0 = 2 * Math.PI * frequency / sampleRate;
        double alpha = Math.Sin(w0) / (2 * q);
        double cos = Math.Cos(w0);

        double a0 = 1 + alpha / a;
        return new BiquadCoefficients(
            (1 + alpha * a) / a0,
            -2 * cos / a0,
            (1 - alpha * a) / a0,
            -2 * cos / a0,
            (1 - alpha / a) / a0);
    }

    /// <summary>Hochpass 2. Ordnung.</summary>
    public static BiquadCoefficients HighPass(double sampleRate, double frequency, double q)
    {
        double w0 = 2 * Math.PI * Math.Min(frequency, sampleRate * 0.49) / sampleRate;
        double alpha = Math.Sin(w0) / (2 * q);
        double cos = Math.Cos(w0);

        double a0 = 1 + alpha;
        return new BiquadCoefficients(
            (1 + cos) / 2 / a0,
            -(1 + cos) / a0,
            (1 + cos) / 2 / a0,
            -2 * cos / a0,
            (1 - alpha) / a0);
    }

    /// <summary>Tiefen-Kuhschwanzfilter (Low Shelf): hebt/senkt alles unterhalb von <paramref name="frequency"/>.</summary>
    public static BiquadCoefficients LowShelf(double sampleRate, double frequency, double gainDb)
    {
        if (gainDb == 0)
        {
            return Identity;
        }

        var (a, cos, twoSqrtAAlpha) = ShelfTerms(sampleRate, frequency, gainDb);
        double a0 = (a + 1) + (a - 1) * cos + twoSqrtAAlpha;
        return new BiquadCoefficients(
            a * ((a + 1) - (a - 1) * cos + twoSqrtAAlpha) / a0,
            2 * a * ((a - 1) - (a + 1) * cos) / a0,
            a * ((a + 1) - (a - 1) * cos - twoSqrtAAlpha) / a0,
            -2 * ((a - 1) + (a + 1) * cos) / a0,
            ((a + 1) + (a - 1) * cos - twoSqrtAAlpha) / a0);
    }

    /// <summary>Höhen-Kuhschwanzfilter (High Shelf): hebt/senkt alles oberhalb von <paramref name="frequency"/>.</summary>
    public static BiquadCoefficients HighShelf(double sampleRate, double frequency, double gainDb)
    {
        if (gainDb == 0)
        {
            return Identity;
        }

        var (a, cos, twoSqrtAAlpha) = ShelfTerms(sampleRate, frequency, gainDb);
        double a0 = (a + 1) - (a - 1) * cos + twoSqrtAAlpha;
        return new BiquadCoefficients(
            a * ((a + 1) + (a - 1) * cos + twoSqrtAAlpha) / a0,
            -2 * a * ((a - 1) + (a + 1) * cos) / a0,
            a * ((a + 1) + (a - 1) * cos - twoSqrtAAlpha) / a0,
            2 * ((a - 1) - (a + 1) * cos) / a0,
            ((a + 1) - (a - 1) * cos - twoSqrtAAlpha) / a0);
    }

    private static (double A, double Cos, double TwoSqrtAAlpha) ShelfTerms(double sampleRate, double frequency, double gainDb)
    {
        double a = Math.Pow(10, gainDb / 40);
        double w0 = 2 * Math.PI * Math.Min(frequency, sampleRate * 0.45) / sampleRate;
        double alpha = Math.Sin(w0) / 2 * Math.Sqrt(2); // Flankensteilheit S = 1
        return (a, Math.Cos(w0), 2 * Math.Sqrt(a) * alpha);
    }

    /// <summary>Betrag des Frequenzgangs in dB bei <paramref name="frequency"/>.</summary>
    public double MagnitudeDb(double frequency, double sampleRate)
    {
        var z1 = Complex.FromPolarCoordinates(1, -2 * Math.PI * frequency / sampleRate); // z^-1
        var z2 = z1 * z1;                                                                 // z^-2
        var response = (B0 + B1 * z1 + B2 * z2) / (1 + A1 * z1 + A2 * z2);
        return 20 * Math.Log10(Math.Max(response.Magnitude, 1e-12));
    }

    /// <summary>Tiefpass 2. Ordnung.</summary>
    public static BiquadCoefficients LowPass(double sampleRate, double frequency, double q)
    {
        double w0 = 2 * Math.PI * Math.Min(frequency, sampleRate * 0.49) / sampleRate;
        double alpha = Math.Sin(w0) / (2 * q);
        double cos = Math.Cos(w0);

        double a0 = 1 + alpha;
        return new BiquadCoefficients(
            (1 - cos) / 2 / a0,
            (1 - cos) / a0,
            (1 - cos) / 2 / a0,
            -2 * cos / a0,
            (1 - alpha) / a0);
    }
}

/// <summary>Ein einzelner Biquad-Filter mit Zustand (Transposed Direct Form II).</summary>
public sealed class BiquadFilter
{
    private double _z1;
    private double _z2;

    public BiquadFilter(BiquadCoefficients coefficients)
    {
        Coefficients = coefficients;
    }

    public BiquadCoefficients Coefficients { get; set; }

    public double Process(double x)
    {
        var c = Coefficients;
        double y = c.B0 * x + _z1;
        _z1 = c.B1 * x - c.A1 * y + _z2;
        _z2 = c.B2 * x - c.A2 * y;
        return y;
    }

    public void Reset() => (_z1, _z2) = (0, 0);
}
