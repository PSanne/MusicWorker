using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Tests;

/// <summary>Testsignale und Messhilfen.</summary>
internal static class Signals
{
    public static AudioBuffer Sine(double frequency, double seconds, float amplitude = 0.5f,
        int sampleRate = 44100, int channels = 1)
    {
        int frames = (int)(seconds * sampleRate);
        var samples = new float[frames * channels];
        for (int f = 0; f < frames; f++)
        {
            float value = amplitude * (float)Math.Sin(2 * Math.PI * frequency * f / sampleRate);
            for (int c = 0; c < channels; c++)
            {
                samples[f * channels + c] = value;
            }
        }

        return new AudioBuffer(samples, sampleRate, channels);
    }

    /// <summary>Effektivwert (RMS) von Kanal 0, ohne den Anfang (Einschwingen der Filter).</summary>
    public static double Rms(AudioBuffer buffer, double skipSeconds = 0.1)
    {
        int start = (int)(skipSeconds * buffer.SampleRate);
        double sum = 0;
        int count = 0;
        for (int f = start; f < buffer.FrameCount; f++, count++)
        {
            double v = buffer.GetSample(f, 0);
            sum += v * v;
        }

        return Math.Sqrt(sum / count);
    }

    /// <summary>Schätzt die Grundfrequenz über die Nulldurchgänge im mittleren Teil.</summary>
    public static double EstimateFrequency(AudioBuffer buffer)
    {
        int start = buffer.FrameCount / 4;
        int end = buffer.FrameCount * 3 / 4;
        int crossings = 0;
        for (int f = start + 1; f < end; f++)
        {
            if (buffer.GetSample(f - 1, 0) < 0 && buffer.GetSample(f, 0) >= 0)
            {
                crossings++;
            }
        }

        return crossings / ((end - start) / (double)buffer.SampleRate);
    }

    public static float Peak(AudioBuffer buffer) => buffer.Samples.ToArray().Max(Math.Abs);
}
