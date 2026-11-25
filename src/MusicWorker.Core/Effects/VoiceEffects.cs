using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Effects;

/// <summary>
/// De-Esser: macht scharfe Zischlaute („s“, „sch“, „z“) leiser, ohne den Rest der Stimme zu verändern.
/// Erkannt wird ein Zischlaut, wenn die Höhen ab <see cref="FrequencyHz"/> im Verhältnis zum ganzen Signal
/// zu stark werden – so funktioniert er bei leisen und lauten Aufnahmen gleich. Abgesenkt wird, indem zum
/// tiefpassgefilterten Signal übergeblendet wird; ohne Zischlaut bleibt das Signal exakt unverändert.
/// </summary>
public sealed class DeEsserEffect(double frequencyHz = 5500, double maxReductionDb = 10, double sensitivity = 5) : IAudioEffect
{
    /// <summary>Ab dieser Frequenz wird nach Zischlauten gesucht und abgesenkt (3 – 10 kHz).</summary>
    public double FrequencyHz { get; } = Math.Clamp(frequencyHz, 2000, 12000);

    /// <summary>Höchstens so viele dB werden abgesenkt.</summary>
    public double MaxReductionDb { get; } = Math.Clamp(maxReductionDb, 1, 30);

    /// <summary>1 = nur sehr scharfe Zischlaute … 10 = schon leichte.</summary>
    public double Sensitivity { get; } = Math.Clamp(sensitivity, 1, 10);

    public string Name => $"De-Esser ({FrequencyHz / 1000:0.#} kHz, bis {MaxReductionDb:0} dB)";

    /// <summary>Wie stark (in dB) die Höhen höchstens unter dem Gesamtsignal liegen dürfen.</summary>
    private double ThresholdDb => -2 - (2 * (Sensitivity - 1)); // 1 → -2 dB, 5 → -10 dB, 10 → -20 dB

    public AudioBuffer Apply(AudioBuffer input)
    {
        ArgumentNullException.ThrowIfNull(input);
        int channels = input.Channels;
        int rate = input.SampleRate;
        var output = input.Samples.ToArray();
        double frequency = Math.Min(FrequencyHz, rate * 0.45);
        double attack = Math.Exp(-1 / (0.0005 * rate));
        double release = Math.Exp(-1 / (0.06 * rate));
        double gate = Math.Pow(10, -60 / 20.0);
        double ratio = 8;

        // Die Erkennung nutzt alle Kanäle gemeinsam, damit links und rechts gleich behandelt werden.
        var filters = Enumerable.Range(0, channels).Select(_ => new BiquadFilter(BiquadCoefficients.HighPass(rate, frequency, 0.707))).ToArray();
        // Steiler Tiefpass (4. Ordnung) für das „entschärfte“ Signal
        var lows = Enumerable.Range(0, channels).Select(_ => (
            A: new BiquadFilter(BiquadCoefficients.LowPass(rate, frequency * 0.9, 0.541)),
            B: new BiquadFilter(BiquadCoefficients.LowPass(rate, frequency * 0.9, 1.307)))).ToArray();
        double highEnv = 0;
        double fullEnv = 0;
        var high = new double[channels];
        var low = new double[channels];
        for (int f = 0; f < input.FrameCount; f++)
        {
            double highPeak = 0;
            double fullPeak = 0;
            for (int c = 0; c < channels; c++)
            {
                double x = output[(f * channels) + c];
                high[c] = filters[c].Process(x);
                low[c] = lows[c].B.Process(lows[c].A.Process(x));
                highPeak = Math.Max(highPeak, Math.Abs(high[c]));
                fullPeak = Math.Max(fullPeak, Math.Abs(x));
            }

            highEnv = highPeak > highEnv ? (attack * highEnv) + ((1 - attack) * highPeak) : (release * highEnv) + ((1 - release) * highPeak);
            fullEnv = fullPeak > fullEnv ? (attack * fullEnv) + ((1 - attack) * fullPeak) : (release * fullEnv) + ((1 - release) * fullPeak);
            if (fullEnv < gate || highEnv <= 0)
            {
                continue;
            }

            double over = (20 * Math.Log10(highEnv / fullEnv)) - ThresholdDb;
            if (over <= 0)
            {
                continue;
            }

            double reductionDb = Math.Min(MaxReductionDb, over * (1 - (1 / ratio)));
            double g = Math.Pow(10, -reductionDb / 20);
            for (int c = 0; c < channels; c++)
            {
                output[(f * channels) + c] = (float)((g * output[(f * channels) + c]) + ((1 - g) * low[c]));
            }
        }

        return new AudioBuffer(output, rate, channels);
    }
}

/// <summary>
/// Tonhöhe ändern und dabei die Klangfarbe (Formanten) erhalten: Eine höher gesungene Stimme klingt dann nicht
/// nach Micky Maus, eine tiefere nicht nach Monster. Dazu wird erst die Tonhöhe wie gewohnt verschoben und danach
/// in jedem kurzen Abschnitt die spektrale Hülle (Klangfarbe) des Originals wiederhergestellt.
/// </summary>
public sealed class FormantPreservingPitchEffect(double semitones, double formantShiftSemitones = 0) : IAudioEffect
{
    /// <summary>Tonhöhe in Halbtönen.</summary>
    public double Semitones { get; } = Math.Clamp(semitones, -24, 24);

    /// <summary>Zusätzliche Verschiebung der Klangfarbe (0 = unverändert, + = heller/kleiner, − = dunkler/größer).</summary>
    public double FormantShiftSemitones { get; } = Math.Clamp(formantShiftSemitones, -12, 12);

    public string Name => FormantShiftSemitones == 0
        ? $"Tonhöhe {Semitones:+0.##;-0.##} Halbtöne (Klangfarbe erhalten)"
        : $"Tonhöhe {Semitones:+0.##;-0.##}, Klangfarbe {FormantShiftSemitones:+0.##;-0.##} Halbtöne";

    public AudioBuffer Apply(AudioBuffer input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var shifted = Math.Abs(Semitones) < 1e-6 ? input : new PitchShiftEffect(Semitones).Apply(input);
        int channels = input.Channels;
        int rate = input.SampleRate;
        // Hülle bis ca. 2,5 ms Quefrenz: fein genug für Formanten, aber gröber als der Abstand der Obertöne
        // (gilt für Stimmen bis etwa 400 Hz Grundton).
        int lifter = Math.Clamp(rate / 400, 16, Stft.Size / 4);
        double pitchFactor = Math.Pow(2, Semitones / 12);
        double formantFactor = Math.Pow(2, FormantShiftSemitones / 12);
        var result = new float[shifted.FrameCount * channels];
        int half = Stft.Size / 2;
        for (int c = 0; c < channels; c++)
        {
            var envelopes = new List<double[]>();
            Stft.Process(Channel(input, c), (_, re, im) => envelopes.Add(Envelope(re, im, lifter)));
            var corrected = Stft.Process(Channel(shifted, c), (frame, re, im) =>
            {
                if (frame >= envelopes.Count)
                {
                    return;
                }

                // Die einfache Tonhöhenverschiebung hat die Hülle um pitchFactor gestreckt – zurück auf die
                // Original-Hülle (bzw. auf die um formantFactor verschobene).
                var envelope = envelopes[frame];
                for (int k = 0; k <= half; k++)
                {
                    double gain = Math.Clamp(At(envelope, k / formantFactor) / Math.Max(At(envelope, k / pitchFactor), 1e-12), 0.05, 20);
                    re[k] *= gain;
                    im[k] *= gain;
                    if (k > 0 && k < half)
                    {
                        re[Stft.Size - k] *= gain;
                        im[Stft.Size - k] *= gain;
                    }
                }
            });

            for (int f = 0; f < corrected.Length && f < shifted.FrameCount; f++)
            {
                result[(f * channels) + c] = corrected[f];
            }
        }

        return new AudioBuffer(result, rate, channels);
    }

    /// <summary>Hülle an einer gebrochenen Bin-Position (linear interpoliert, außerhalb: Randwert).</summary>
    private static double At(double[] envelope, double bin)
    {
        if (bin <= 0)
        {
            return envelope[0];
        }

        int k0 = Math.Min(envelope.Length - 1, (int)bin);
        int k1 = Math.Min(envelope.Length - 1, k0 + 1);
        double t = bin - k0;
        return (envelope[k0] * (1 - t)) + (envelope[k1] * t);
    }

    private static float[] Channel(AudioBuffer buffer, int channel)
    {
        var data = new float[buffer.FrameCount];
        for (int f = 0; f < data.Length; f++)
        {
            data[f] = buffer.GetSample(f, channel);
        }

        return data;
    }

    /// <summary>Spektrale Hülle (geglätteter Betrag) über das Cepstrum: feine Obertonstruktur wird ausgeblendet.</summary>
    private static double[] Envelope(double[] re, double[] im, int lifter)
    {
        int size = Stft.Size;
        var cr = new double[size];
        var ci = new double[size];
        for (int k = 0; k < size; k++)
        {
            cr[k] = Math.Log(Math.Sqrt((re[k] * re[k]) + (im[k] * im[k])) + 1e-9);
        }

        Fft.Transform(cr, ci); // reelles, symmetrisches Spektrum → Cepstrum (bis auf Faktor)
        for (int q = lifter; q <= size - lifter; q++)
        {
            cr[q] = 0;
            ci[q] = 0;
        }

        for (int q = 0; q < size; q++)
        {
            ci[q] = -ci[q];
        }

        Fft.Transform(cr, ci);
        var envelope = new double[(size / 2) + 1];
        for (int k = 0; k < envelope.Length; k++)
        {
            envelope[k] = Math.Exp(cr[k] / size);
        }

        return envelope;
    }
}
