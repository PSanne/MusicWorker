using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Mixing;

/// <summary>
/// Verarbeitet die Insert-Effekte eines Stereo-Kanalzugs: Hochpass → Tiefen → Mitten → Höhen →
/// Kompressor. Der Kompressor-Regler (0 … 100 %) steuert Schwelle (0 … -30 dB), Verhältnis (3:1)
/// und eine automatische Aufholverstärkung.
/// </summary>
public sealed class InsertChain
{
    private const double LowShelfHz = 120;
    private const double MidHz = 1000;
    private const double MidQ = 0.9;
    private const double HighShelfHz = 8000;

    private readonly int _sampleRate;
    private readonly double[] _state = new double[2 * 4 * 2]; // 2 Kanäle × 4 Filter × 2 Zustände
    private readonly CompressorProcessor _compressor;
    private BiquadCoefficients[] _coefficients = [];
    private bool _compress;
    private TrackInserts? _applied;

    public InsertChain(int sampleRate, TrackInserts inserts)
    {
        _sampleRate = sampleRate;
        _compressor = new CompressorProcessor(sampleRate, 2, CompressorSettings.Off);
        Update(inserts);
    }

    /// <summary>Übernimmt neue Einstellungen (wird im Audio-Thread vor dem Verarbeiten aufgerufen).</summary>
    public void Update(TrackInserts inserts)
    {
        if (ReferenceEquals(inserts, _applied) || inserts == _applied)
        {
            return;
        }

        _applied = inserts;
        _coefficients =
        [
            inserts.HighPassHz > 20.5 ? BiquadCoefficients.HighPass(_sampleRate, inserts.HighPassHz, 0.7071) : BiquadCoefficients.Identity,
            BiquadCoefficients.LowShelf(_sampleRate, LowShelfHz, inserts.LowDb),
            BiquadCoefficients.PeakingEq(_sampleRate, MidHz, MidQ, inserts.MidDb),
            BiquadCoefficients.HighShelf(_sampleRate, HighShelfHz, inserts.HighDb),
        ];

        double amount = Math.Clamp(inserts.CompressionPercent, 0, 100) / 100;
        _compress = amount > 0;
        _compressor.Update(new CompressorSettings(
            [100], thresholdDb: -30 * amount, ratio: 3, attackMs: 10, releaseMs: 120, makeupDb: 12 * amount));
    }

    public void Process(Span<float> stereo)
    {
        var coefficients = _coefficients;
        for (int i = 0; i < stereo.Length; i++)
        {
            int s = (i & 1) * 8;
            double x = stereo[i] + 1e-20;
            for (int f = 0; f < 4; f++, s += 2)
            {
                ref readonly var c = ref coefficients[f];
                double y = c.B0 * x + _state[s];
                _state[s] = c.B1 * x - c.A1 * y + _state[s + 1];
                _state[s + 1] = c.B2 * x - c.A2 * y;
                x = y;
            }

            stereo[i] = (float)x;
        }

        if (_compress)
        {
            _compressor.Process(stereo);
        }
    }
}

/// <summary>Begrenzer für die Summe: regelt sehr schnell ab -1 dBFS herunter und kappt den Rest.</summary>
public sealed class MasterLimiter
{
    private readonly CompressorProcessor _compressor;

    public MasterLimiter(int sampleRate)
    {
        _compressor = new CompressorProcessor(sampleRate, 2,
            new CompressorSettings([100], thresholdDb: -1, ratio: 20, attackMs: 0.5, releaseMs: 80));
    }

    public void Process(Span<float> stereo)
    {
        _compressor.Process(stereo);
        for (int i = 0; i < stereo.Length; i++)
        {
            stereo[i] = Math.Clamp(stereo[i], -0.995f, 0.995f);
        }
    }
}
