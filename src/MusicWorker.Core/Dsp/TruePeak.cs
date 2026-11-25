using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Dsp;

/// <summary>
/// Echte Spitzenwerte („True Peak“, dBTP) nach BS.1770 Anhang 2: das Signal wird vierfach überabgetastet,
/// damit auch Spitzen zwischen zwei Samples sichtbar werden. Solche Spitzen können beim Umwandeln in MP3/AAC
/// oder im Abspielgerät zu Verzerrungen führen, obwohl kein Sample über 0 dBFS liegt.
/// </summary>
public static class TruePeak
{
    public const int Factor = 4;
    private const int TapsPerPhase = 16;

    /// <summary>Polyphasen-Interpolationsfilter (gefenstertes Sinc), Phase × Tap.</summary>
    private static readonly double[][] Phases = CreatePhases();

    /// <summary>Höchste echte Spitze aller Kanäle in dBTP.</summary>
    public static double Measure(AudioBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        double peak = 0;
        for (int c = 0; c < buffer.Channels; c++)
        {
            var peaks = Envelope(buffer, c);
            foreach (float p in peaks)
            {
                peak = Math.Max(peak, p);
            }
        }

        return Decibels.FromGain(peak);
    }

    /// <summary>Je Sample die größte überabgetastete Auslenkung rund um dieses Sample (für Begrenzer).</summary>
    public static float[] Envelope(AudioBuffer buffer, int channel)
    {
        int frames = buffer.FrameCount;
        int half = TapsPerPhase / 2;

        // Kanal mit Rand (Nullen) in ein eigenes Feld kopieren – so braucht die innere Schleife keine Bereichsprüfung.
        var x = new float[frames + TapsPerPhase];
        var samples = buffer.Samples;
        int channels = buffer.Channels;
        for (int f = 0; f < frames; f++)
        {
            x[f + half - 1] = samples[f * channels + channel];
        }

        var result = new float[frames];
        var p1 = Phases[1];
        var p2 = Phases[2];
        var p3 = Phases[3];
        for (int f = 0; f < frames; f++)
        {
            // x[f + t] entspricht dem Sample f - half + 1 + t
            double s1 = 0, s2 = 0, s3 = 0;
            for (int t = 0; t < TapsPerPhase; t++)
            {
                double v = x[f + t];
                s1 += p1[t] * v;
                s2 += p2[t] * v;
                s3 += p3[t] * v;
            }

            double peak = Math.Max(Math.Abs(x[f + half - 1]), Math.Max(Math.Abs(s1), Math.Max(Math.Abs(s2), Math.Abs(s3))));
            result[f] = (float)peak;
        }

        return result;
    }

    private static double[][] CreatePhases()
    {
        var phases = new double[Factor][];
        int half = TapsPerPhase / 2;
        for (int phase = 0; phase < Factor; phase++)
        {
            phases[phase] = new double[TapsPerPhase];
            double fraction = phase / (double)Factor;
            for (int t = 0; t < TapsPerPhase; t++)
            {
                // Abstand zwischen Interpolationsstelle (f + fraction) und Sample (f - half + 1 + t)
                double x = fraction - (t - half + 1);
                double sinc = Math.Abs(x) < 1e-12 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);
                double w = (x + half) / (2.0 * half); // 0 … 1 über die Fensterbreite
                double window = 0.42 - 0.5 * Math.Cos(2 * Math.PI * w) + 0.08 * Math.Cos(4 * Math.PI * w); // Blackman
                phases[phase][t] = sinc * window;
            }
        }

        return phases;
    }
}
