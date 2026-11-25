using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Dsp;

/// <summary>Ergebnis einer Lautheitsmessung nach ITU-R BS.1770 / EBU R128.</summary>
/// <param name="IntegratedLufs">Gesamtlautheit (LUFS); <see cref="double.NegativeInfinity"/> bei Stille oder zu kurzem Audio.</param>
/// <param name="LoudnessRange">Lautheitsumfang (LU): wie stark laute und leise Stellen auseinanderliegen.</param>
/// <param name="TruePeakDb">Echte Spitze (dBTP) – auch zwischen den Samples, wie sie nach dem Umwandeln entsteht.</param>
/// <param name="SamplePeakDb">Höchster Samplewert (dBFS).</param>
/// <param name="MaxMomentaryLufs">Lauteste Stelle über 0,4 s.</param>
/// <param name="MaxShortTermLufs">Lauteste Stelle über 3 s.</param>
public sealed record LoudnessResult(
    double IntegratedLufs, double LoudnessRange, double TruePeakDb, double SamplePeakDb,
    double MaxMomentaryLufs, double MaxShortTermLufs)
{
    public bool HasLoudness => !double.IsNegativeInfinity(IntegratedLufs);
}

/// <summary>Messung der empfundenen Lautheit (LUFS) – so wie Spotify, YouTube und Rundfunk messen.</summary>
public static class Loudness
{
    /// <summary>Verbreitete Zielwerte.</summary>
    public static IReadOnlyList<(string Name, double Lufs)> Targets { get; } =
    [
        ("Spotify, YouTube, Tidal (-14 LUFS)", -14),
        ("Apple Music (-16 LUFS)", -16),
        ("Podcast, Hörbuch (-16 LUFS)", -16),
        ("Lauter Pop/Club-Mix (-9 LUFS)", -9),
        ("Rundfunk EBU R128 (-23 LUFS)", -23),
    ];

    /// <summary>Misst Lautheit, Lautheitsumfang und Spitzen des ganzen Puffers.</summary>
    public static LoudnessResult Measure(AudioBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var meter = new LoudnessMeter(buffer.SampleRate, buffer.Channels, keepHistory: true);
        var samples = buffer.Samples;
        int block = buffer.SampleRate * buffer.Channels; // 1 s am Stück
        for (int offset = 0; offset < samples.Length; offset += block)
        {
            meter.Process(samples.Slice(offset, Math.Min(block, samples.Length - offset)));
        }

        return new LoudnessResult(
            meter.Integrated, meter.LoudnessRange, TruePeak.Measure(buffer), Decibels.FromGain(SamplePeak(buffer)),
            meter.MaxMomentary, meter.MaxShortTerm);
    }

    public static float SamplePeak(AudioBuffer buffer)
    {
        float peak = 0;
        foreach (float v in buffer.Samples)
        {
            peak = Math.Max(peak, Math.Abs(v));
        }

        return peak;
    }

    /// <summary>Lautheit eines Blocks aus der gewichteten mittleren Energie (BS.1770: -0,691 + 10·log10).</summary>
    internal static double ToLufs(double energy) => energy <= 0 ? double.NegativeInfinity : -0.691 + 10 * Math.Log10(energy);

    internal static double FromLufs(double lufs) => Math.Pow(10, (lufs + 0.691) / 10);
}

/// <summary>
/// Lautheitsmesser nach ITU-R BS.1770-4 / EBU R128: K-Gewichtung, Blöcke von 400 ms (alle 100 ms),
/// Momentan- (0,4 s), Kurzzeit- (3 s) und integrierte Lautheit mit absolutem (-70 LUFS) und
/// relativem Gate (-10 LU) sowie Lautheitsumfang (LRA, EBU Tech 3342). Auch für die Live-Anzeige geeignet.
/// </summary>
public sealed class LoudnessMeter
{
    private const int SubBlocksPerMomentary = 4;   // 4 × 100 ms
    private const int SubBlocksPerShortTerm = 30;  // 30 × 100 ms

    private readonly int _channels;
    private readonly double[] _weights;
    private readonly KWeighting[] _filters;
    private readonly int _subBlockFrames;
    private readonly double[] _subBlocks = new double[SubBlocksPerShortTerm]; // Ring: gewichtete Energie je 100 ms
    private readonly List<double> _momentaryBlocks = [];
    private readonly List<double>? _shortTermBlocks;
    private int _subBlockCount;
    private int _framesInSubBlock;
    private double _subBlockSum;

    public LoudnessMeter(int sampleRate, int channels, bool keepHistory = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _channels = channels;
        _subBlockFrames = Math.Max(1, sampleRate / 10);
        _filters = Enumerable.Range(0, channels).Select(_ => new KWeighting(sampleRate)).ToArray();

        // Kanalgewichte: links/rechts/Mitte 1,0, Surround 1,41, LFE (Kanal 4 bei 5.1) zählt nicht.
        _weights = Enumerable.Range(0, channels).Select(c => channels >= 6 ? c switch { 3 => 0.0, 4 or 5 => 1.41, _ => 1.0 } : 1.0).ToArray();
        _shortTermBlocks = keepHistory ? [] : null;
        MaxMomentary = MaxShortTerm = double.NegativeInfinity;
    }

    /// <summary>Lautheit der letzten 0,4 s (LUFS).</summary>
    public double Momentary { get; private set; } = double.NegativeInfinity;

    /// <summary>Lautheit der letzten 3 s (LUFS).</summary>
    public double ShortTerm { get; private set; } = double.NegativeInfinity;

    public double MaxMomentary { get; private set; }

    public double MaxShortTerm { get; private set; }

    /// <summary>Integrierte (Gesamt-)Lautheit seit Beginn bzw. <see cref="Reset"/>.</summary>
    public double Integrated => GatedLoudness(_momentaryBlocks, relativeGateLu: 10);

    /// <summary>Lautheitsumfang in LU (nur mit <c>keepHistory</c>).</summary>
    public double LoudnessRange
    {
        get
        {
            if (_shortTermBlocks is null)
            {
                return 0;
            }

            double absolute = Loudness.FromLufs(-70);
            var gated = _shortTermBlocks.Where(e => e > absolute).ToArray();
            if (gated.Length == 0)
            {
                return 0;
            }

            double relative = Loudness.FromLufs(Loudness.ToLufs(gated.Average()) - 20);
            var values = gated.Where(e => e > relative).Select(Loudness.ToLufs).OrderBy(v => v).ToArray();
            if (values.Length == 0)
            {
                return 0;
            }

            return Percentile(values, 0.95) - Percentile(values, 0.10);
        }
    }

    public void Reset()
    {
        foreach (var filter in _filters)
        {
            filter.Reset();
        }

        Array.Clear(_subBlocks);
        _momentaryBlocks.Clear();
        _shortTermBlocks?.Clear();
        _subBlockCount = 0;
        _framesInSubBlock = 0;
        _subBlockSum = 0;
        Momentary = ShortTerm = MaxMomentary = MaxShortTerm = double.NegativeInfinity;
    }

    /// <summary>Verarbeitet verschachtelte Samples (L, R, L, R …).</summary>
    public void Process(ReadOnlySpan<float> interleaved)
    {
        int frames = interleaved.Length / _channels;
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int c = 0; c < _channels; c++)
            {
                double y = _filters[c].Process(interleaved[f * _channels + c]);
                sum += _weights[c] * y * y;
            }

            _subBlockSum += sum;
            if (++_framesInSubBlock == _subBlockFrames)
            {
                CompleteSubBlock();
            }
        }
    }

    private void CompleteSubBlock()
    {
        _subBlocks[_subBlockCount % SubBlocksPerShortTerm] = _subBlockSum / _subBlockFrames;
        _subBlockCount++;
        _subBlockSum = 0;
        _framesInSubBlock = 0;

        if (_subBlockCount >= SubBlocksPerMomentary)
        {
            double energy = Average(SubBlocksPerMomentary);
            _momentaryBlocks.Add(energy);
            Momentary = Loudness.ToLufs(energy);
            MaxMomentary = Math.Max(MaxMomentary, Momentary);
        }

        if (_subBlockCount >= SubBlocksPerShortTerm)
        {
            double energy = Average(SubBlocksPerShortTerm);
            _shortTermBlocks?.Add(energy);
            ShortTerm = Loudness.ToLufs(energy);
            MaxShortTerm = Math.Max(MaxShortTerm, ShortTerm);
        }
        else if (_subBlockCount >= SubBlocksPerMomentary)
        {
            // In den ersten 3 Sekunden zeigt die Kurzzeit-Anzeige das bisher Gemessene.
            ShortTerm = Loudness.ToLufs(Average(_subBlockCount));
        }
    }

    private double Average(int count)
    {
        double sum = 0;
        for (int i = 1; i <= count; i++)
        {
            sum += _subBlocks[(_subBlockCount - i) % SubBlocksPerShortTerm];
        }

        return sum / count;
    }

    private static double GatedLoudness(List<double> blocks, double relativeGateLu)
    {
        double absolute = Loudness.FromLufs(-70);
        double sum = 0;
        int count = 0;
        foreach (double e in blocks)
        {
            if (e > absolute)
            {
                sum += e;
                count++;
            }
        }

        if (count == 0)
        {
            return double.NegativeInfinity;
        }

        double relative = Loudness.FromLufs(Loudness.ToLufs(sum / count) - relativeGateLu);
        sum = 0;
        count = 0;
        foreach (double e in blocks)
        {
            if (e > absolute && e > relative)
            {
                sum += e;
                count++;
            }
        }

        return count == 0 ? double.NegativeInfinity : Loudness.ToLufs(sum / count);
    }

    private static double Percentile(double[] sorted, double p)
    {
        double index = p * (sorted.Length - 1);
        int lower = (int)Math.Floor(index);
        int upper = Math.Min(sorted.Length - 1, lower + 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }
}

/// <summary>
/// K-Gewichtung nach BS.1770: Höhenanhebung (+4 dB, „Kopf-Effekt“) und Hochpass (RLB) – für jede Abtastrate
/// aus den analogen Vorbildern berechnet (bei 48 kHz exakt die Koeffizienten der Norm).
/// </summary>
public sealed class KWeighting
{
    private readonly double _b0, _b1, _b2, _a1, _a2;   // Stufe 1: Höhenanhebung
    private readonly double _c1, _c2;                  // Stufe 2: Hochpass (Zähler 1, -2, 1)
    private double _x1, _x2, _y1, _y2, _z1, _z2;

    public KWeighting(double sampleRate)
    {
        (_b0, _b1, _b2, _a1, _a2) = ShelfCoefficients(sampleRate);
        (_c1, _c2) = HighPassCoefficients(sampleRate);
    }

    public static (double B0, double B1, double B2, double A1, double A2) ShelfCoefficients(double sampleRate)
    {
        const double f0 = 1681.974450955533;
        const double gain = 3.999843853973347;
        const double q = 0.7071752369554196;
        double k = Math.Tan(Math.PI * f0 / sampleRate);
        double vh = Math.Pow(10, gain / 20);
        double vb = Math.Pow(vh, 0.4996667741545416);
        double a0 = 1 + k / q + k * k;
        return (
            (vh + vb * k / q + k * k) / a0,
            2 * (k * k - vh) / a0,
            (vh - vb * k / q + k * k) / a0,
            2 * (k * k - 1) / a0,
            (1 - k / q + k * k) / a0);
    }

    public static (double A1, double A2) HighPassCoefficients(double sampleRate)
    {
        const double f0 = 38.13547087613982;
        const double q = 0.5003270373253953;
        double k = Math.Tan(Math.PI * f0 / sampleRate);
        double a0 = 1 + k / q + k * k;
        return (2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
    }

    public double Process(double x)
    {
        double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        double z = y - 2 * _y1 + _y2 - _c1 * _z1 - _c2 * _z2;
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        _z2 = _z1;
        _z1 = z;
        return z;
    }

    public void Reset() => _x1 = _x2 = _y1 = _y2 = _z1 = _z2 = 0;
}
