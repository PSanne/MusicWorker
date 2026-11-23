using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Dsp;

/// <summary>Ändert die Wiedergabegeschwindigkeit durch Umrechnen der Samples (Tonhöhe und Länge ändern sich).</summary>
public static class Resampler
{
    // Butterworth-Tiefpass 8. Ordnung als vier Biquads mit diesen Güten.
    private static readonly double[] ButterworthQ = [0.5098, 0.6013, 0.9000, 2.5629];

    /// <summary>
    /// Liest das Audio mit <paramref name="speed"/>-facher Geschwindigkeit
    /// (2 = doppelt so schnell → eine Oktave höher und halb so lang).
    /// </summary>
    public static AudioBuffer ChangeSpeed(AudioBuffer input, double speed)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(speed, 0);
        if (Math.Abs(speed - 1) < 1e-9 || input.IsEmpty)
        {
            return input;
        }

        int channels = input.Channels;
        int inputFrames = input.FrameCount;
        var source = speed > 1 ? AntiAlias(input, speed) : input.Samples.ToArray();

        int outputFrames = Math.Max(1, (int)Math.Round(inputFrames / speed));
        var output = new float[outputFrames * channels];

        for (int frame = 0; frame < outputFrames; frame++)
        {
            double position = frame * speed;
            int index = (int)position;
            float t = (float)(position - index);
            for (int c = 0; c < channels; c++)
            {
                float y0 = Sample(source, index - 1, c, channels, inputFrames);
                float y1 = Sample(source, index, c, channels, inputFrames);
                float y2 = Sample(source, index + 1, c, channels, inputFrames);
                float y3 = Sample(source, index + 2, c, channels, inputFrames);
                output[frame * channels + c] = Hermite(y0, y1, y2, y3, t);
            }
        }

        return new AudioBuffer(output, input.SampleRate, channels);
    }

    /// <summary>
    /// Filtert Frequenzen heraus, die nach dem Beschleunigen über der halben Abtastrate lägen
    /// und sonst als störende Verzerrungen (Aliasing) hörbar würden.
    /// </summary>
    private static float[] AntiAlias(AudioBuffer input, double speed)
    {
        var samples = input.Samples.ToArray();
        int channels = input.Channels;
        double cutoff = input.SampleRate * 0.45 / speed;

        for (int c = 0; c < channels; c++)
        {
            var filters = ButterworthQ
                .Select(q => new BiquadFilter(BiquadCoefficients.LowPass(input.SampleRate, cutoff, q)))
                .ToArray();
            for (int i = c; i < samples.Length; i += channels)
            {
                double x = samples[i];
                foreach (var filter in filters)
                {
                    x = filter.Process(x);
                }

                samples[i] = (float)x;
            }
        }

        return samples;
    }

    private static float Sample(float[] samples, int frame, int channel, int channels, int frames) =>
        samples[Math.Clamp(frame, 0, frames - 1) * channels + channel];

    /// <summary>Kubische Hermite-Interpolation (weicher als lineare Interpolation).</summary>
    private static float Hermite(float y0, float y1, float y2, float y3, float t)
    {
        float c1 = 0.5f * (y2 - y0);
        float c2 = y0 - 2.5f * y1 + 2f * y2 - 0.5f * y3;
        float c3 = 0.5f * (y3 - y0) + 1.5f * (y1 - y2);
        return ((c3 * t + c2) * t + c1) * t + y1;
    }
}
