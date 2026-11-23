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
