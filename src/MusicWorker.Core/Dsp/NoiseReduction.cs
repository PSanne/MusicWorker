using MusicWorker.Core.Audio;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Dsp;

/// <summary>Kurzzeit-Fourier-Transformation (Hann-Fenster, 75 % Überlappung) mit Rücktransformation.</summary>
public static class Stft
{
    public const int Size = 2048;
    public const int Hop = Size / 4;

    public static readonly double[] Window = Enumerable.Range(0, Size).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Size)).ToArray();

    /// <summary>Zerlegt einen Kanal in Spektren, verändert sie mit <paramref name="modify"/> und setzt wieder zusammen.</summary>
    public static float[] Process(ReadOnlySpan<float> mono, Action<int, double[], double[]> modify)
    {
        int length = mono.Length;
        var output = new double[length + Size * 2];
        var input = new double[length + Size * 2];
        for (int i = 0; i < length; i++)
        {
            input[i + Size] = mono[i]; // vorne und hinten Platz, damit auch die Ränder voll überlappt werden
        }

        var real = new double[Size];
        var imaginary = new double[Size];
        int frame = 0;
        for (int start = 0; start + Size <= input.Length; start += Hop, frame++)
        {
            for (int i = 0; i < Size; i++)
            {
                real[i] = input[start + i] * Window[i];
                imaginary[i] = 0;
            }

            Fft.Transform(real, imaginary);
            modify(frame, real, imaginary);

            // Rücktransformation über die konjugierte Hin-Transformation
            for (int i = 0; i < Size; i++)
            {
                imaginary[i] = -imaginary[i];
            }

            Fft.Transform(real, imaginary);
            for (int i = 0; i < Size; i++)
            {
                output[start + i] += real[i] / Size * Window[i] / 1.5; // Hann² bei 75 % Überlappung summiert sich zu 1,5
            }
        }

        var result = new float[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = (float)output[i + Size];
        }

        return result;
    }

    /// <summary>Betragsspektren aller Fenster eines Kanals (für Rauschprofil und Spektrogramm).</summary>
    public static IEnumerable<double[]> Magnitudes(ReadOnlySpan<float> mono)
    {
        var frames = new List<double[]>();
        var real = new double[Size];
        var imaginary = new double[Size];
        for (int start = 0; start + Size <= mono.Length; start += Hop)
        {
            for (int i = 0; i < Size; i++)
            {
                real[i] = mono[start + i] * Window[i];
                imaginary[i] = 0;
            }

            Fft.Transform(real, imaginary);
            var magnitude = new double[Size / 2 + 1];
            for (int k = 0; k <= Size / 2; k++)
            {
                magnitude[k] = Math.Sqrt(real[k] * real[k] + imaginary[k] * imaginary[k]);
            }

            frames.Add(magnitude);
        }

        return frames;
    }
}

/// <summary>Rauschprofil: mittleres Betragsspektrum einer Stelle, an der nur Rauschen zu hören ist.</summary>
public sealed class NoiseProfile
{
    private NoiseProfile(int sampleRate, double[] magnitudes)
    {
        SampleRate = sampleRate;
        Magnitudes = magnitudes;
    }

    public int SampleRate { get; }

    /// <summary>Mittlerer Betrag je Frequenzbin.</summary>
    public IReadOnlyList<double> Magnitudes { get; }

    /// <summary>Lernt das Rauschen aus einem Ausschnitt (mindestens etwa 0,1 s; alle Kanäle gemischt).</summary>
    public static NoiseProfile Learn(AudioBuffer noise)
    {
        ArgumentNullException.ThrowIfNull(noise);
        if (noise.FrameCount < Stft.Size)
        {
            throw new ArgumentException("Für ein Rauschprofil wird mindestens etwa 0,05 Sekunden Audio benötigt.", nameof(noise));
        }

        var mono = Mono(noise);
        var frames = Stft.Magnitudes(mono).ToArray();
        var average = new double[Stft.Size / 2 + 1];
        foreach (var frame in frames)
        {
            for (int k = 0; k < average.Length; k++)
            {
                average[k] += frame[k] / frames.Length;
            }
        }

        return new NoiseProfile(noise.SampleRate, average);
    }

    internal static float[] Mono(AudioBuffer buffer)
    {
        var result = new float[buffer.FrameCount];
        var samples = buffer.Samples;
        for (int f = 0; f < buffer.FrameCount; f++)
        {
            float sum = 0;
            for (int c = 0; c < buffer.Channels; c++)
            {
                sum += samples[f * buffer.Channels + c];
            }

            result[f] = sum / buffer.Channels;
        }

        return result;
    }
}

/// <summary>
/// Spektrale Rauschentfernung: Frequenzanteile, die kaum lauter sind als das gelernte Rauschen, werden um
/// <see cref="ReductionDb"/> abgesenkt. Die Absenkung wird über Zeit und Frequenz geglättet, damit kein
/// „Zwitschern“ (Musical Noise) entsteht.
/// </summary>
public sealed class NoiseReductionEffect(NoiseProfile profile, double reductionDb = 15, double sensitivity = 1.5) : IAudioEffect
{
    public double ReductionDb { get; } = Math.Clamp(reductionDb, 0, 40);

    /// <summary>Ab dem Wievielfachen des Rauschpegels ein Anteil als Nutzsignal gilt.</summary>
    public double Sensitivity { get; } = Math.Clamp(sensitivity, 1, 6);

    public string Name => "Rauschen entfernen";

    public AudioBuffer Apply(AudioBuffer input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var noise = profile.Magnitudes;
        int bins = Stft.Size / 2 + 1;
        double floor = Decibels.ToGain(-ReductionDb);
        int channels = input.Channels;
        var output = new float[input.Samples.Length];
        for (int c = 0; c < channels; c++)
        {
            var mono = new float[input.FrameCount];
            for (int f = 0; f < mono.Length; f++)
            {
                mono[f] = input.GetSample(f, c);
            }

            var previous = Enumerable.Repeat(1.0, bins).ToArray();
            var gains = new double[bins];
            var processed = Stft.Process(mono, (_, real, imaginary) =>
            {
                for (int k = 0; k < bins; k++)
                {
                    double magnitude = Math.Sqrt(real[k] * real[k] + imaginary[k] * imaginary[k]) + 1e-12;
                    double threshold = noise[k] * Sensitivity;
                    double ratio = threshold / magnitude;
                    double gain = Math.Max(floor, Math.Sqrt(Math.Max(0, 1 - ratio * ratio)));
                    // schnell öffnen, langsam schließen
                    gains[k] = gain > previous[k] ? gain : previous[k] * 0.6 + gain * 0.4;
                }

                for (int k = 0; k < bins; k++)
                {
                    // über Nachbarfrequenzen glätten
                    double smoothed = (gains[Math.Max(0, k - 1)] + 2 * gains[k] + gains[Math.Min(bins - 1, k + 1)]) / 4;
                    previous[k] = gains[k];
                    real[k] *= smoothed;
                    imaginary[k] *= smoothed;
                    if (k > 0 && k < Stft.Size / 2)
                    {
                        real[Stft.Size - k] *= smoothed; // gespiegelte Hälfte des Spektrums
                        imaginary[Stft.Size - k] *= smoothed;
                    }
                }
            });

            for (int f = 0; f < processed.Length; f++)
            {
                output[f * channels + c] = processed[f];
            }
        }

        return new AudioBuffer(output, input.SampleRate, channels);
    }
}
