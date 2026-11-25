using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Effects;

/// <summary>Was beim Anpassen der Lautheit passiert ist (für die Meldung an den Nutzer).</summary>
public sealed record LoudnessReport(double BeforeLufs, double AfterLufs, double GainDb, double LimitedDb, double TruePeakDb);

/// <summary>
/// Bringt Audio auf eine Ziel-Lautheit in LUFS (z. B. -14 für Spotify/YouTube). Würden die Spitzen dabei über die
/// Obergrenze (z. B. -1 dBTP) steigen, greift ein vorausschauender Begrenzer, der nur diese Spitzen sanft absenkt –
/// so wird das Ziel erreicht, ohne dass es verzerrt.
/// </summary>
public static class LoudnessNormalizer
{
    public static (AudioBuffer Audio, LoudnessReport Report) Normalize(AudioBuffer input, double targetLufs, double ceilingDbtp = -1)
    {
        ArgumentNullException.ThrowIfNull(input);
        var before = Loudness.Measure(input);
        if (!before.HasLoudness)
        {
            throw new InvalidOperationException("Das Audio ist zu leise oder zu kurz (mindestens 0,4 Sekunden), um die Lautheit zu messen.");
        }

        // Die Spitzen-Hüllkurve wird nur einmal berechnet – mit einer Verstärkung skaliert sie einfach mit.
        var envelope = PeakEnvelope(input);
        double ceiling = Decibels.ToGain(ceilingDbtp) * 0.995; // kleine Reserve für die Interpolation

        double gainDb = targetLufs - before.IntegratedLufs;
        var (output, limited) = Render(input, envelope, gainDb, ceiling);
        double reached = Loudness.Measure(output).IntegratedLufs;

        // Der Begrenzer nimmt etwas Lautheit weg: mit dem Sekantenverfahren nachregeln (höchstens 8 Schritte).
        double previousGain = gainDb;
        double previousReached = reached;
        gainDb += targetLufs - reached;
        for (int pass = 0; pass < 8 && limited > 0 && Math.Abs(targetLufs - reached) >= 0.05; pass++)
        {
            (output, limited) = Render(input, envelope, gainDb, ceiling);
            reached = Loudness.Measure(output).IntegratedLufs;
            double slope = Math.Abs(reached - previousReached) > 1e-6 ? (gainDb - previousGain) / (reached - previousReached) : 1;
            previousGain = gainDb;
            previousReached = reached;
            double maxGain = targetLufs - before.IntegratedLufs + 30; // nie mehr als 30 dB zusätzlich begrenzen
            gainDb = Math.Min(gainDb + Math.Clamp(slope, 1, 20) * (targetLufs - reached), maxGain);
        }

        gainDb = previousGain;
        var after = Loudness.Measure(output);
        return (output, new LoudnessReport(before.IntegratedLufs, after.IntegratedLufs, gainDb, limited, after.TruePeakDb));
    }

    /// <summary>Verstärkt um <paramref name="gainDb"/> und begrenzt die Spitzen; liefert die stärkste Absenkung in dB.</summary>
    private static (AudioBuffer Audio, double LimitedDb) Render(AudioBuffer input, float[] envelope, double gainDb, double ceiling)
    {
        double gain = Decibels.ToGain(gainDb);
        var curve = LimiterCurve(envelope, gain, ceiling, input.SampleRate, out double minGain);
        int channels = input.Channels;
        var samples = input.Samples.ToArray();
        for (int f = 0; f < input.FrameCount; f++)
        {
            float g = (float)(gain * (curve is null ? 1 : curve[f]));
            for (int c = 0; c < channels; c++)
            {
                samples[f * channels + c] *= g;
            }
        }

        return (new AudioBuffer(samples, input.SampleRate, channels), curve is null ? 0 : -Decibels.FromGain(minGain));
    }

    /// <summary>Größte echte Spitze aller Kanäle je Sample.</summary>
    private static float[] PeakEnvelope(AudioBuffer audio)
    {
        var envelope = new float[audio.FrameCount];
        for (int c = 0; c < audio.Channels; c++)
        {
            var peaks = TruePeak.Envelope(audio, c);
            for (int f = 0; f < envelope.Length; f++)
            {
                envelope[f] = Math.Max(envelope[f], peaks[f]);
            }
        }

        return envelope;
    }

    /// <summary>
    /// Verstärkungskurve eines vorausschauenden Spitzenbegrenzers (5 ms Vorlauf, 150 ms Rückkehr);
    /// null, wenn keine Spitze die Obergrenze überschreitet.
    /// </summary>
    private static double[]? LimiterCurve(float[] envelope, double gain, double ceiling, int sampleRate, out double minGain)
    {
        int frames = envelope.Length;
        minGain = 1;
        double[]? curve = null;
        for (int f = 0; f < frames; f++)
        {
            double peak = envelope[f] * gain;
            if (peak > ceiling)
            {
                curve ??= Enumerable.Repeat(1.0, frames).ToArray();
                curve[f] = ceiling / peak;
            }
        }

        if (curve is null)
        {
            return null;
        }

        int lookahead = Math.Max(1, sampleRate / 200);           // 5 ms
        double release = Math.Exp(-1.0 / (0.150 * sampleRate));  // 150 ms

        // 1. Absenkung schon vor der Spitze beginnen: linear über den Vorlauf einblenden (Rückwärtslauf).
        for (int f = frames - 2; f >= 0; f--)
        {
            curve[f] = Math.Min(curve[f], curve[f + 1] + (1.0 / lookahead));
        }

        // 2. Nach der Spitze langsam zurückkehren (Vorwärtslauf).
        for (int f = 1; f < frames; f++)
        {
            curve[f] = Math.Min(curve[f], 1 - (1 - curve[f - 1]) * release);
            minGain = Math.Min(minGain, curve[f]);
        }

        minGain = Math.Min(minGain, curve[0]);
        return curve;
    }

    /// <summary>Begrenzt die echten Spitzen auf <paramref name="ceilingDbtp"/>; liefert die stärkste Absenkung in dB (0 = nichts zu tun).</summary>
    public static double Limit(ref AudioBuffer audio, double ceilingDbtp)
    {
        var (limited, reduction) = Render(audio, PeakEnvelope(audio), 0, Decibels.ToGain(ceilingDbtp) * 0.995);
        audio = limited;
        return reduction;
    }
}

/// <summary>„Auf Lautheit normalisieren“ als rückgängig machbarer Effekt.</summary>
public sealed class LoudnessNormalizeEffect(double targetLufs, double ceilingDbtp = -1) : IAudioEffect
{
    public double TargetLufs { get; } = Math.Clamp(targetLufs, -40, 0);

    public double CeilingDbtp { get; } = Math.Clamp(ceilingDbtp, -6, 0);

    public string Name => $"Lautheit auf {TargetLufs:0.#} LUFS";

    /// <summary>Bericht des letzten Durchlaufs.</summary>
    public LoudnessReport? Report { get; private set; }

    public AudioBuffer Apply(AudioBuffer input)
    {
        var (audio, report) = LoudnessNormalizer.Normalize(input, TargetLufs, CeilingDbtp);
        Report = report;
        return audio;
    }
}
