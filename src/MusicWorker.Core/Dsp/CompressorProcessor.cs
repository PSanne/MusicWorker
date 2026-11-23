namespace MusicWorker.Core.Dsp;

/// <summary>
/// Kompressor mit Hüllkurvenfolger (Attack/Release), weichem Knie (6 dB) und Aufholverstärkung.
/// Gekoppelt regelt er alle Kanäle mit derselben Verstärkung (lautester Kanal bestimmt).
/// Einstellungen lassen sich während der Wiedergabe thread-sicher ändern.
/// </summary>
public sealed class CompressorProcessor
{
    private const double KneeDb = 6;

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly double[] _envelope;
    private readonly float[] _gainReductionDb;
    private Snapshot _snapshot = null!;

    public CompressorProcessor(int sampleRate, int channels, CompressorSettings settings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _sampleRate = sampleRate;
        _channels = channels;
        _envelope = new double[channels];
        _gainReductionDb = new float[channels];
        Update(settings);
    }

    /// <summary>Aktuelle Pegelreduktion je Kanal in dB (positiv), z. B. für eine Anzeige.</summary>
    public float GetGainReductionDb(int channel) =>
        channel >= 0 && channel < _channels ? Volatile.Read(ref _gainReductionDb[channel]) : 0;

    public void Update(CompressorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var mix = new float[_channels];
        for (int c = 0; c < _channels; c++)
        {
            mix[c] = (float)(settings.MixFor(c) / 100);
        }

        Volatile.Write(ref _snapshot, new Snapshot(
            settings.ThresholdDb,
            1 - 1 / settings.Ratio,
            Math.Exp(-1 / (settings.AttackMs / 1000 * _sampleRate)),
            Math.Exp(-1 / (settings.ReleaseMs / 1000 * _sampleRate)),
            settings.MakeupDb,
            settings.Linked,
            mix));
    }

    /// <summary>Verarbeitet verschachtelte Samples (L, R, L, R, …) an Ort und Stelle.</summary>
    public void Process(Span<float> samples)
    {
        var s = Volatile.Read(ref _snapshot);
        int frames = samples.Length / _channels;
        var reduction = new double[_channels];

        for (int frame = 0; frame < frames; frame++)
        {
            int offset = frame * _channels;

            // 1. Hüllkurve je Kanal (Spitzenwert-Folger mit Attack/Release)
            for (int c = 0; c < _channels; c++)
            {
                double level = Math.Abs(samples[offset + c]);
                double coefficient = level > _envelope[c] ? s.Attack : s.Release;
                _envelope[c] = coefficient * _envelope[c] + (1 - coefficient) * level;
            }

            // 2. Pegelreduktion berechnen (gekoppelt: lautester Kanal für alle)
            if (s.Linked)
            {
                double max = 0;
                for (int c = 0; c < _channels; c++)
                {
                    max = Math.Max(max, _envelope[c]);
                }

                double r = GainReduction(max, s);
                Array.Fill(reduction, r);
            }
            else
            {
                for (int c = 0; c < _channels; c++)
                {
                    reduction[c] = GainReduction(_envelope[c], s);
                }
            }

            // 3. Anwenden und mit dem Original mischen
            for (int c = 0; c < _channels; c++)
            {
                float dry = samples[offset + c];
                float wet = dry * (float)Math.Pow(10, (s.MakeupDb - reduction[c]) / 20);
                samples[offset + c] = dry + (wet - dry) * s.Mix[c];
            }
        }

        for (int c = 0; c < _channels; c++)
        {
            Volatile.Write(ref _gainReductionDb[c], (float)reduction[c]);
        }
    }

    /// <summary>Pegelreduktion in dB (≥ 0) für einen Hüllkurvenwert, mit weichem Knie.</summary>
    private static double GainReduction(double envelope, Snapshot s)
    {
        if (envelope <= 1e-9)
        {
            return 0;
        }

        double over = 20 * Math.Log10(envelope) - s.ThresholdDb;
        if (over <= -KneeDb / 2)
        {
            return 0;
        }

        if (over < KneeDb / 2)
        {
            double x = over + KneeDb / 2;
            return s.Slope * x * x / (2 * KneeDb);
        }

        return s.Slope * over;
    }

    private sealed record Snapshot(
        double ThresholdDb, double Slope, double Attack, double Release, double MakeupDb, bool Linked, float[] Mix);
}
