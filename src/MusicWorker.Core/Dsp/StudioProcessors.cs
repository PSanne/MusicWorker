using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Dsp;

// Fünf weitere Effekte: Noise-Gate, Tremolo, Phaser, Stereo-Verbreiterung, Bandsättigung.
// Alle arbeiten blockweise auf verschachtelten Samples (L, R, L, R …) und lassen sich während der
// Wiedergabe mit Update() ändern (die neuen Werte werden atomar übernommen).

/// <summary>Noise-Gate: schaltet Pausen stumm (bzw. leiser), damit Rauschen und Brummen zwischen Sätzen verschwinden.</summary>
/// <param name="ThresholdDb">Unter dieser Schwelle gilt das Signal als Pause.</param>
/// <param name="ReductionDb">Wie stark Pausen abgesenkt werden (0 = aus, 80 = praktisch stumm).</param>
/// <param name="HoldMs">So lange bleibt das Gate nach dem letzten lauten Moment offen (verhindert Abhacken).</param>
/// <param name="ReleaseMs">So sanft schließt es danach.</param>
public sealed record GateSettings(double ThresholdDb = -50, double ReductionDb = 40, double HoldMs = 60, double ReleaseMs = 150)
{
    public static GateSettings Off { get; } = new(ReductionDb: 0);

    public bool IsNeutral => ReductionDb <= 0.05;
}

public sealed class GateProcessor
{
    private readonly int _channels;
    private readonly int _sampleRate;
    private GateSettings _settings;
    private double _envelope;
    private double _gain = 1;
    private int _holdLeft;

    public GateProcessor(int sampleRate, int channels, GateSettings settings)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _settings = settings;
    }

    public void Update(GateSettings settings) => Volatile.Write(ref _settings, settings);

    public void Process(Span<float> samples)
    {
        var settings = Volatile.Read(ref _settings);
        if (settings.IsNeutral)
        {
            return;
        }

        double open = Decibels.ToGain(settings.ThresholdDb);
        double close = open * 0.7; // etwa 3 dB Hysterese gegen Flattern
        double floor = Decibels.ToGain(-Math.Clamp(settings.ReductionDb, 0, 90));
        double envelopeRelease = Math.Exp(-1.0 / (0.010 * _sampleRate));
        double attack = 1 - Math.Exp(-1.0 / (0.001 * _sampleRate));
        double release = 1 - Math.Exp(-4.0 / (Math.Max(1, settings.ReleaseMs) / 1000 * _sampleRate)); // nach „Release“ fast ganz zu
        int hold = (int)(settings.HoldMs / 1000 * _sampleRate);
        int frames = samples.Length / _channels;
        for (int f = 0; f < frames; f++)
        {
            double peak = 0;
            for (int c = 0; c < _channels; c++)
            {
                peak = Math.Max(peak, Math.Abs(samples[f * _channels + c]));
            }

            _envelope = peak > _envelope ? peak : _envelope * envelopeRelease;
            if (_envelope >= open)
            {
                _holdLeft = hold;
            }
            else if (_envelope < close && _holdLeft > 0)
            {
                _holdLeft--;
            }

            double target = _envelope >= open || _holdLeft > 0 ? 1 : floor;
            _gain += (target - _gain) * (target > _gain ? attack : release);
            float g = (float)_gain;
            for (int c = 0; c < _channels; c++)
            {
                samples[f * _channels + c] *= g;
            }
        }
    }
}

/// <summary>Tremolo: Lautstärke schwankt regelmäßig – als Pendel auch zwischen links und rechts.</summary>
public sealed record TremoloSettings(double RateHz = 5, double DepthPercent = 50, bool StereoPan = false)
{
    public static TremoloSettings Off { get; } = new(DepthPercent: 0);

    public bool IsNeutral => DepthPercent <= 0.05;
}

public sealed class TremoloProcessor(int sampleRate, int channels, TremoloSettings settings)
{
    private TremoloSettings _settings = settings;
    private double _phase;

    public void Update(TremoloSettings value) => Volatile.Write(ref _settings, value);

    public void Process(Span<float> samples)
    {
        var s = Volatile.Read(ref _settings);
        if (s.IsNeutral)
        {
            return;
        }

        double depth = Math.Clamp(s.DepthPercent, 0, 100) / 100;
        double step = 2 * Math.PI * Math.Clamp(s.RateHz, 0.1, 20) / sampleRate;
        int frames = samples.Length / channels;
        for (int f = 0; f < frames; f++)
        {
            double lfo = 0.5 - 0.5 * Math.Cos(_phase); // 0 … 1
            for (int c = 0; c < channels; c++)
            {
                double value = s.StereoPan && channels > 1 && c % 2 == 1 ? 1 - lfo : lfo;
                samples[f * channels + c] *= (float)(1 - depth * value);
            }

            _phase += step;
            if (_phase > 2 * Math.PI)
            {
                _phase -= 2 * Math.PI;
            }
        }
    }
}

/// <summary>Phaser: wandernde Auslöschungen im Klangspektrum – der typische „schwebende“ Klang der 70er.</summary>
public sealed record PhaserSettings(double RateHz = 0.5, double DepthPercent = 70, double FeedbackPercent = 40, double MixPercent = 50)
{
    public static PhaserSettings Off { get; } = new(MixPercent: 0);

    public bool IsNeutral => MixPercent <= 0.05;
}

public sealed class PhaserProcessor
{
    private const int Stages = 6;
    private const double MinHz = 200;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly double[][] _state;
    private readonly double[] _last;
    private PhaserSettings _settings;
    private double _phase;

    public PhaserProcessor(int sampleRate, int channels, PhaserSettings settings)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _settings = settings;
        _state = Enumerable.Range(0, channels).Select(_ => new double[Stages * 2]).ToArray();
        _last = new double[channels];
    }

    public void Update(PhaserSettings settings) => Volatile.Write(ref _settings, settings);

    public void Process(Span<float> samples)
    {
        var s = Volatile.Read(ref _settings);
        if (s.IsNeutral)
        {
            return;
        }

        double mix = Math.Clamp(s.MixPercent, 0, 100) / 100;
        double feedback = Math.Clamp(s.FeedbackPercent, 0, 90) / 100;
        double octaves = 0.5 + 4 * Math.Clamp(s.DepthPercent, 0, 100) / 100;
        double step = 2 * Math.PI * Math.Clamp(s.RateHz, 0.02, 10) / _sampleRate;
        int frames = samples.Length / _channels;
        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < _channels; c++)
            {
                // Kanäle um 90° versetzt – das macht den Effekt breiter.
                double lfo = 0.5 + 0.5 * Math.Sin(_phase + c * Math.PI / 2);
                double hz = Math.Min(MinHz * Math.Pow(2, octaves * lfo), _sampleRate * 0.45);
                double t = Math.Tan(Math.PI * hz / _sampleRate);
                double a = (t - 1) / (t + 1);
                var state = _state[c];
                double x = samples[f * _channels + c];
                double y = x + _last[c] * feedback;
                for (int stage = 0; stage < Stages; stage++)
                {
                    // Allpass 1. Ordnung: y[n] = a·x[n] + x[n-1] − a·y[n-1]
                    double output = a * y + state[2 * stage] - a * state[2 * stage + 1];
                    state[2 * stage] = y;
                    state[2 * stage + 1] = output;
                    y = output;
                }

                _last[c] = y;
                samples[f * _channels + c] = (float)(x * (1 - mix * 0.5) + y * mix * 0.5);
            }

            _phase += step;
            if (_phase > 2 * Math.PI)
            {
                _phase -= 2 * Math.PI;
            }
        }
    }
}

/// <summary>Stereo-Verbreiterung: 0 % = Mono, 100 % = unverändert, 200 % = doppelt so breit.</summary>
public sealed record WidenerSettings(double WidthPercent = 100)
{
    public static WidenerSettings Off { get; } = new();

    public bool IsNeutral => Math.Abs(WidthPercent - 100) < 0.5;
}

public sealed class WidenerProcessor(int channels, WidenerSettings settings)
{
    private WidenerSettings _settings = settings;

    public void Update(WidenerSettings value) => Volatile.Write(ref _settings, value);

    public void Process(Span<float> samples)
    {
        var s = Volatile.Read(ref _settings);
        if (channels != 2 || s.IsNeutral)
        {
            return;
        }

        double width = Math.Clamp(s.WidthPercent, 0, 200) / 100;
        double makeup = 1 / Math.Max(1, Math.Sqrt((1 + width * width) / 2)); // breiter heißt sonst auch lauter
        for (int i = 0; i + 1 < samples.Length; i += 2)
        {
            double mid = (samples[i] + samples[i + 1]) * 0.5;
            double side = (samples[i] - samples[i + 1]) * 0.5 * width;
            samples[i] = (float)((mid + side) * (width > 1 ? makeup : 1));
            samples[i + 1] = (float)((mid - side) * (width > 1 ? makeup : 1));
        }
    }
}

/// <summary>Bandsättigung: warme, weiche Verzerrung wie bei einem Tonbandgerät, dazu etwas weniger Höhen.</summary>
/// <param name="DriveDb">Wie stark das Signal in die Sättigung gefahren wird.</param>
/// <param name="WarmthPercent">Wie stark die Höhen gedämpft werden (0 = gar nicht).</param>
/// <param name="MixPercent">Anteil des gesättigten Signals.</param>
public sealed record SaturationSettings(double DriveDb = 6, double WarmthPercent = 40, double MixPercent = 100)
{
    public static SaturationSettings Off { get; } = new(MixPercent: 0);

    public bool IsNeutral => MixPercent <= 0.05 || (DriveDb <= 0.05 && WarmthPercent <= 0.05);
}

public sealed class SaturationProcessor(int sampleRate, int channels, SaturationSettings settings)
{
    private const double Bias = 0.15; // leichte Asymmetrie → auch gerade Obertöne, wie beim Band
    private readonly double[] _low = new double[channels];
    private SaturationSettings _settings = settings;

    public void Update(SaturationSettings value) => Volatile.Write(ref _settings, value);

    public void Process(Span<float> samples)
    {
        var s = Volatile.Read(ref _settings);
        if (s.IsNeutral)
        {
            return;
        }

        double drive = Decibels.ToGain(Math.Clamp(s.DriveDb, 0, 24));
        double mix = Math.Clamp(s.MixPercent, 0, 100) / 100;
        double cutoff = 20000 * Math.Pow(5000 / 20000.0, Math.Clamp(s.WarmthPercent, 0, 100) / 100); // 20 kHz … 5 kHz
        double coefficient = 1 - Math.Exp(-2 * Math.PI * Math.Min(cutoff, sampleRate * 0.45) / sampleRate);
        double offset = Math.Tanh(Bias);
        double slope = 1 - offset * offset; // Steigung von tanh an der Stelle Bias
        double norm = 1 / (slope * Math.Sqrt(drive)); // leise Stellen werden um √Drive lauter, Spitzen bleiben unter 0 dB
        for (int f = 0; f < samples.Length / channels; f++)
        {
            for (int c = 0; c < channels; c++)
            {
                int i = f * channels + c;
                double x = samples[i];
                double wet = (Math.Tanh(x * drive + Bias) - offset) * norm;
                _low[c] += (wet - _low[c]) * coefficient;
                samples[i] = (float)(x * (1 - mix) + _low[c] * mix);
            }
        }
    }
}
