using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Dsp;

/// <summary>Erkannte Tonhöhe an einer Stelle: Frequenz (0 = keine/stimmlos) und Sicherheit 0 … 1.</summary>
public readonly record struct PitchFrame(double Frequency, double Confidence)
{
    public bool IsVoiced => Frequency > 0 && Confidence >= PitchDetector.VoicedConfidence;

    /// <summary>Tonhöhe als MIDI-Notennummer mit Nachkommastellen (69 = A4).</summary>
    public double Midi => Frequency > 0 ? 69 + 12 * Math.Log2(Frequency / 440) : 0;
}

/// <summary>Tonhöhenverlauf: ein <see cref="PitchFrame"/> alle <see cref="HopFrames"/> Frames.</summary>
public sealed record PitchTrack(int HopFrames, IReadOnlyList<PitchFrame> Frames)
{
    /// <summary>Mittlere Tonhöhe (Median der sicheren Werte) als MIDI-Nummer, oder null.</summary>
    public double? MedianMidi()
    {
        var voiced = Frames.Where(f => f.IsVoiced).Select(f => f.Midi).OrderBy(m => m).ToArray();
        return voiced.Length == 0 ? null : voiced[voiced.Length / 2];
    }
}

/// <summary>
/// Tonhöhenerkennung nach dem YIN-Verfahren (de Cheveigné &amp; Kawahara 2002) für einstimmige Signale
/// (Gesang, Soloinstrument). Zur Beschleunigung wird auf etwa 11 kHz heruntergerechnet – für Grundtöne bis
/// 1 kHz reicht das.
/// </summary>
public static class PitchDetector
{
    /// <summary>Ab dieser Sicherheit gilt eine Stelle als „gesungen/gespielt“ (sonst Atmen, Konsonant, Stille).</summary>
    public const double VoicedConfidence = 0.75;

    private const double Threshold = 0.15;
    private const int TargetRate = 11025;

    /// <summary>Tonhöhenverlauf eines Puffers (Kanäle werden gemischt).</summary>
    public static PitchTrack Track(AudioBuffer buffer, double minHz = 70, double maxHz = 1000, double hopSeconds = 0.006)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        int factor = Math.Max(1, buffer.SampleRate / TargetRate);
        int rate = buffer.SampleRate / factor;
        var mono = Downsample(buffer, factor);
        int maxLag = (int)Math.Ceiling(rate / minHz);
        int minLag = Math.Max(2, (int)Math.Floor(rate / maxHz));
        int window = maxLag * 2;
        int hop = Math.Max(1, (int)(hopSeconds * rate));
        var frames = new List<PitchFrame>();
        var difference = new double[maxLag + 1];
        for (int start = -window / 2; start < mono.Length - window / 2; start += hop)
        {
            frames.Add(Analyze(mono, start, window, minLag, maxLag, rate, difference));
        }

        return new PitchTrack(hop * factor, frames);
    }

    /// <summary>Tonhöhe eines kurzen Ausschnitts (Mono, beliebige Abtastrate).</summary>
    public static PitchFrame Detect(ReadOnlySpan<float> mono, int sampleRate, double minHz = 70, double maxHz = 1000)
    {
        int maxLag = (int)Math.Ceiling(sampleRate / minHz);
        int minLag = Math.Max(2, (int)Math.Floor(sampleRate / maxHz));
        int window = Math.Min(mono.Length - maxLag - 1, maxLag * 2);
        if (window < maxLag)
        {
            return default;
        }

        return Analyze(mono.ToArray(), 0, window, minLag, maxLag, sampleRate, new double[maxLag + 1]);
    }

    private static PitchFrame Analyze(float[] x, int start, int window, int minLag, int maxLag, int rate, double[] d)
    {
        // Pegel prüfen: sehr leise Stellen sind stimmlos
        double energy = 0;
        for (int j = 0; j < window; j++)
        {
            double v = Get(x, start + j);
            energy += v * v;
        }

        if (energy / window < 1e-6)
        {
            return default; // unter etwa -60 dBFS
        }

        // Differenzfunktion
        for (int lag = 1; lag <= maxLag; lag++)
        {
            double sum = 0;
            for (int j = 0; j < window; j++)
            {
                double delta = Get(x, start + j) - Get(x, start + j + lag);
                sum += delta * delta;
            }

            d[lag] = sum;
        }

        // Kumulativ normierte Differenz
        d[0] = 1;
        double running = 0;
        for (int lag = 1; lag <= maxLag; lag++)
        {
            running += d[lag];
            d[lag] = running > 0 ? d[lag] * lag / running : 1;
        }

        int best = -1;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            if (d[lag] < Threshold)
            {
                while (lag + 1 <= maxLag && d[lag + 1] < d[lag])
                {
                    lag++;
                }

                best = lag;
                break;
            }
        }

        if (best < 0)
        {
            best = minLag;
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                if (d[lag] < d[best])
                {
                    best = lag;
                }
            }
        }

        // Parabolische Interpolation für Bruchteile eines Samples
        double refined = best;
        if (best > minLag && best < maxLag)
        {
            double a = d[best - 1];
            double b = d[best];
            double c = d[best + 1];
            double denominator = a - 2 * b + c;
            if (Math.Abs(denominator) > 1e-12)
            {
                refined = best + 0.5 * (a - c) / denominator;
            }
        }

        return new PitchFrame(rate / refined, Math.Clamp(1 - d[best], 0, 1));
    }

    private static double Get(float[] x, int index) => index >= 0 && index < x.Length ? x[index] : 0;

    /// <summary>Mono-Mischung, gemittelt über <paramref name="factor"/> Samples (einfacher Tiefpass) und ausgedünnt.</summary>
    private static float[] Downsample(AudioBuffer buffer, int factor)
    {
        int frames = buffer.FrameCount / factor;
        var result = new float[frames];
        int channels = buffer.Channels;
        var samples = buffer.Samples;
        for (int i = 0; i < frames; i++)
        {
            double sum = 0;
            for (int k = 0; k < factor; k++)
            {
                int frame = i * factor + k;
                for (int c = 0; c < channels; c++)
                {
                    sum += samples[frame * channels + c];
                }
            }

            result[i] = (float)(sum / (factor * channels));
        }

        return result;
    }
}

/// <summary>
/// Stufenlos steuerbarer Pitch-Shifter (zwei überblendete Lesezeiger in einer Verzögerungsleitung).
/// Das Verhältnis darf sich von Sample zu Sample ändern – Grundlage für Autotune und Tonhöhenverläufe.
/// Die Klangfarbe (Formanten) wird mit verschoben; für Korrekturen bis etwa ±3 Halbtöne klingt das natürlich.
/// </summary>
public sealed class VariablePitchShifter
{
    private readonly float[] _line;
    private readonly int _window;
    private int _write;
    private double _phase;

    /// <param name="sampleRate">Abtastrate.</param>
    /// <param name="windowSeconds">Fensterlänge: länger = weniger Rauigkeit, aber mehr „Echo“.</param>
    public VariablePitchShifter(int sampleRate, double windowSeconds = 0.03)
    {
        _window = Math.Max(16, (int)(windowSeconds * sampleRate));
        _line = new float[_window * 2 + 8];
    }

    /// <summary>Mittlere Verzögerung (Frames), um die das Ergebnis später kommt.</summary>
    public int Latency => _window / 2;

    /// <summary>Verarbeitet ein Sample mit dem Tonhöhenverhältnis <paramref name="ratio"/> (2 = Oktave höher).</summary>
    public float Process(float input, double ratio)
    {
        _line[_write] = input;
        _phase += (1 - ratio) / _window;
        _phase -= Math.Floor(_phase);
        double second = _phase + 0.5;
        second -= Math.Floor(second);
        float a = Read(_phase * _window);
        float b = Read(second * _window);
        double gainA = Math.Sin(Math.PI * _phase);
        double gainB = Math.Sin(Math.PI * second);
        _write = (_write + 1) % _line.Length;
        return (float)(a * gainA * gainA + b * gainB * gainB);
    }

    /// <summary>
    /// Verschiebt einen ganzen Puffer; <paramref name="ratioAt"/> liefert das Verhältnis je Frame.
    /// Die Verzögerung wird ausgeglichen, die Länge bleibt gleich.
    /// </summary>
    public static AudioBuffer Apply(AudioBuffer input, Func<long, double> ratioAt, double windowSeconds = 0.03)
    {
        ArgumentNullException.ThrowIfNull(input);
        int channels = input.Channels;
        int frames = input.FrameCount;
        var source = input.Samples;
        var output = new float[frames * channels];
        var ratios = new double[frames];
        for (long f = 0; f < frames; f++)
        {
            ratios[f] = Math.Clamp(ratioAt(f), 0.25, 4);
        }

        for (int c = 0; c < channels; c++)
        {
            var shifter = new VariablePitchShifter(input.SampleRate, windowSeconds);
            int latency = shifter.Latency;
            for (int f = 0; f < frames + latency; f++)
            {
                float x = f < frames ? source[f * channels + c] : 0;
                float y = shifter.Process(x, ratios[Math.Min(f, frames - 1)]);
                int target = f - latency;
                if (target >= 0)
                {
                    output[target * channels + c] = y;
                }
            }
        }

        return new AudioBuffer(output, input.SampleRate, channels);
    }

    private float Read(double delay)
    {
        double position = _write - 1 - delay;
        while (position < 0)
        {
            position += _line.Length;
        }

        int i = (int)position;
        double frac = position - i;
        float s0 = _line[i % _line.Length];
        float s1 = _line[(i + 1) % _line.Length];
        return (float)(s0 + (s1 - s0) * frac);
    }
}
