namespace MusicWorker.Core.Dsp;

/// <summary>Hochpass und Tiefpass (jeweils 24 dB/Oktave, Butterworth). 20 Hz bzw. 20 kHz = aus.</summary>
public sealed class FilterSettings
{
    public const double MinHz = 20;
    public const double MaxHz = 20000;

    public FilterSettings(double highPassHz = MinHz, double lowPassHz = MaxHz)
    {
        HighPassHz = Math.Clamp(highPassHz, MinHz, MaxHz);
        LowPassHz = Math.Clamp(lowPassHz, MinHz, MaxHz);
    }

    public static FilterSettings Off { get; } = new();

    /// <summary>Alles unterhalb dieser Frequenz wird abgeschnitten (20 Hz = aus).</summary>
    public double HighPassHz { get; }

    /// <summary>Alles oberhalb dieser Frequenz wird abgeschnitten (20 kHz = aus).</summary>
    public double LowPassHz { get; }

    public bool HighPassActive => HighPassHz > MinHz + 0.5;

    public bool LowPassActive => LowPassHz < MaxHz - 0.5;

    public bool IsNeutral => !HighPassActive && !LowPassActive;
}

/// <summary>
/// Hoch- und Tiefpass mit je zwei Biquads in Reihe (Butterworth 4. Ordnung, 24 dB/Oktave).
/// Einstellungen lassen sich während der Wiedergabe thread-sicher ändern.
/// </summary>
public sealed class FilterProcessor
{
    // Güten für einen Butterworth-Filter 4. Ordnung aus zwei Biquads
    private static readonly double[] ButterworthQ = [0.5412, 1.3066];

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly double[] _state; // je Kanal 4 Biquads × 2 Zustände
    private Snapshot _snapshot = null!;

    public FilterProcessor(int sampleRate, int channels, FilterSettings settings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _sampleRate = sampleRate;
        _channels = channels;
        _state = new double[channels * 4 * 2];
        Update(settings);
    }

    public void Update(FilterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var coefficients = new BiquadCoefficients[4];
        for (int i = 0; i < 2; i++)
        {
            coefficients[i] = settings.HighPassActive
                ? BiquadCoefficients.HighPass(_sampleRate, settings.HighPassHz, ButterworthQ[i])
                : BiquadCoefficients.Identity;
            coefficients[2 + i] = settings.LowPassActive && settings.LowPassHz < _sampleRate * 0.49
                ? BiquadCoefficients.LowPass(_sampleRate, settings.LowPassHz, ButterworthQ[i])
                : BiquadCoefficients.Identity;
        }

        Volatile.Write(ref _snapshot, new Snapshot(coefficients));
    }

    public void Process(Span<float> samples)
    {
        var coefficients = Volatile.Read(ref _snapshot).Coefficients;
        var state = _state;
        for (int i = 0; i < samples.Length; i++)
        {
            int s = (i % _channels) * 8;
            double x = samples[i] + 1e-20;
            for (int f = 0; f < 4; f++, s += 2)
            {
                ref readonly var c = ref coefficients[f];
                double y = c.B0 * x + state[s];
                state[s] = c.B1 * x - c.A1 * y + state[s + 1];
                state[s + 1] = c.B2 * x - c.A2 * y;
                x = y;
            }

            samples[i] = (float)x;
        }
    }

    private sealed record Snapshot(BiquadCoefficients[] Coefficients);
}
