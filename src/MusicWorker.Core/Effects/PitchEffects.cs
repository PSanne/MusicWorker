using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Notes;

namespace MusicWorker.Core.Effects;

/// <summary>Tonleitern für Autotune.</summary>
public enum ScaleKind
{
    Chromatic,
    Major,
    Minor,
    MajorPentatonic,
    MinorPentatonic,
}

/// <summary>Tonleitern: welche Halbtöne (relativ zum Grundton) erlaubt sind.</summary>
public static class Scales
{
    public static IReadOnlyList<ScaleKind> All { get; } = Enum.GetValues<ScaleKind>();

    public static IReadOnlyList<string> KeyNames { get; } = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "H"];

    public static string NameOf(ScaleKind scale) => scale switch
    {
        ScaleKind.Chromatic => "Chromatisch (alle Halbtöne)",
        ScaleKind.Major => "Dur",
        ScaleKind.Minor => "Moll",
        ScaleKind.MajorPentatonic => "Dur-Pentatonik",
        _ => "Moll-Pentatonik",
    };

    public static IReadOnlyList<int> Steps(ScaleKind scale) => scale switch
    {
        ScaleKind.Chromatic => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
        ScaleKind.Major => [0, 2, 4, 5, 7, 9, 11],
        ScaleKind.Minor => [0, 2, 3, 5, 7, 8, 10],
        ScaleKind.MajorPentatonic => [0, 2, 4, 7, 9],
        _ => [0, 3, 5, 7, 10],
    };

    /// <summary>Nächster Ton der Tonleiter zu einer (gebrochenen) MIDI-Tonhöhe.</summary>
    public static double Nearest(double midi, int key, ScaleKind scale)
    {
        var steps = Steps(scale);
        double best = midi;
        double distance = double.MaxValue;
        int octave = (int)Math.Floor((midi - key) / 12);
        for (int o = octave - 1; o <= octave + 1; o++)
        {
            foreach (int step in steps)
            {
                double candidate = key + o * 12 + step;
                double d = Math.Abs(candidate - midi);
                if (d < distance)
                {
                    distance = d;
                    best = candidate;
                }
            }
        }

        return best;
    }

    /// <summary>„A4 +12 ct“ – Tonname und Abweichung in Cent.</summary>
    public static string Describe(double midi)
    {
        int nearest = (int)Math.Round(midi);
        int cents = (int)Math.Round((midi - nearest) * 100);
        return cents == 0 ? Note.PitchName(nearest) : $"{Note.PitchName(nearest)} {cents:+0;-0} ct";
    }
}

/// <summary>
/// Autotune: erkennt die gesungene Tonhöhe und zieht sie auf den nächsten Ton der gewählten Tonleiter.
/// <see cref="SpeedMs"/> bestimmt, wie schnell korrigiert wird (0 = sofort, der bekannte „Roboter-Effekt“;
/// 50 – 150 ms klingt natürlich), <see cref="StrengthPercent"/> wie weit.
/// </summary>
public sealed class AutotuneEffect(int key, ScaleKind scale, double speedMs, double strengthPercent) : IAudioEffect
{
    public int Key { get; } = Math.Clamp(key, 0, 11);

    public ScaleKind Scale { get; } = scale;

    public double SpeedMs { get; } = Math.Clamp(speedMs, 0, 1000);

    public double StrengthPercent { get; } = Math.Clamp(strengthPercent, 0, 100);

    public string Name => "Autotune";

    public AudioBuffer Apply(AudioBuffer input)
    {
        var track = PitchDetector.Track(input);
        var semitones = Corrections(track, input.SampleRate);
        return VariablePitchShifter.Apply(input, frame =>
        {
            double position = frame / (double)track.HopFrames;
            int i = Math.Clamp((int)position, 0, semitones.Length - 1);
            int j = Math.Min(i + 1, semitones.Length - 1);
            double s = semitones[i] + (semitones[j] - semitones[i]) * (position - i);
            return Math.Pow(2, s / 12);
        });
    }

    /// <summary>Korrektur in Halbtönen je Analyseschritt (geglättet).</summary>
    public double[] Corrections(PitchTrack track, int sampleRate)
    {
        var result = new double[Math.Max(1, track.Frames.Count)];
        double hopSeconds = track.HopFrames / (double)sampleRate;
        double smoothing = SpeedMs <= 0 ? 0 : Math.Exp(-hopSeconds / (SpeedMs / 1000));
        double current = 0;
        for (int i = 0; i < track.Frames.Count; i++)
        {
            var frame = track.Frames[i];
            double target = 0;
            if (frame.IsVoiced)
            {
                double midi = frame.Midi;
                target = (Scales.Nearest(midi, Key, Scale) - midi) * StrengthPercent / 100;
            }

            current = target + (current - target) * smoothing;
            result[i] = current;
        }

        return result;
    }
}

/// <summary>Tonhöhe um einen festen Wert ändern (z. B. eine zu tief gesungene Stelle um +30 Cent korrigieren); die Länge bleibt gleich.</summary>
public sealed class PitchShiftEffect(double semitones) : IAudioEffect
{
    public double Semitones { get; } = Math.Clamp(semitones, -24, 24);

    public string Name => $"Tonhöhe {Semitones:+0.##;-0.##} Halbtöne";

    public AudioBuffer Apply(AudioBuffer input)
    {
        double ratio = Math.Pow(2, Semitones / 12);
        return VariablePitchShifter.Apply(input, _ => ratio);
    }
}

/// <summary>
/// Parametrische Tonhöhe: Die Tonhöhe verläuft über den Bereich von <see cref="FromSemitones"/> nach
/// <see cref="ToSemitones"/> (linear oder als Kurve) – z. B. 0 → -12 für einen „Bandstopp“-Effekt.
/// </summary>
public sealed class PitchRampEffect(double fromSemitones, double toSemitones, double curve = 1) : IAudioEffect
{
    public double FromSemitones { get; } = Math.Clamp(fromSemitones, -24, 24);

    public double ToSemitones { get; } = Math.Clamp(toSemitones, -24, 24);

    /// <summary>1 = gleichmäßig, &gt; 1 = erst langsam, dann schnell, &lt; 1 = umgekehrt.</summary>
    public double Curve { get; } = Math.Clamp(curve, 0.2, 5);

    public string Name => "Tonhöhen-Verlauf";

    public AudioBuffer Apply(AudioBuffer input)
    {
        double length = Math.Max(1, input.FrameCount - 1);
        return VariablePitchShifter.Apply(input, frame =>
        {
            double t = Math.Pow(frame / length, Curve);
            return Math.Pow(2, (FromSemitones + (ToSemitones - FromSemitones) * t) / 12);
        });
    }
}

/// <summary>Stimmeffekte.</summary>
public enum VoicePreset
{
    Robot,
    Helium,
    Giant,
    Telephone,
    Megaphone,
    Underwater,
}

/// <summary>
/// Stimmeffekte als fertige Voreinstellungen: Roboter, Helium, Riese, Telefon, Megafon, Unterwasser.
/// <see cref="MixPercent"/> mischt den Effekt mit dem Original.
/// </summary>
public sealed class VoiceEffect(VoicePreset preset, double mixPercent = 100) : IAudioEffect
{
    public VoicePreset Preset { get; } = preset;

    public double MixPercent { get; } = Math.Clamp(mixPercent, 0, 100);

    public string Name => $"Stimmeffekt: {NameOf(Preset)}";

    public static IReadOnlyList<VoicePreset> All { get; } = Enum.GetValues<VoicePreset>();

    public static string NameOf(VoicePreset preset) => preset switch
    {
        VoicePreset.Robot => "Roboter",
        VoicePreset.Helium => "Helium (hoch)",
        VoicePreset.Giant => "Riese (tief)",
        VoicePreset.Telephone => "Telefon",
        VoicePreset.Megaphone => "Megafon",
        _ => "Unterwasser",
    };

    public AudioBuffer Apply(AudioBuffer input)
    {
        int rate = input.SampleRate;
        int channels = input.Channels;
        var wet = Preset switch
        {
            VoicePreset.Robot => Robot(input),
            VoicePreset.Helium => VariablePitchShifter.Apply(input, _ => Math.Pow(2, 7 / 12.0)),
            VoicePreset.Giant => VariablePitchShifter.Apply(input, _ => Math.Pow(2, -7 / 12.0), 0.04),
            VoicePreset.Underwater => VariablePitchShifter.Apply(input, f => Math.Pow(2, 0.35 * Math.Sin(2 * Math.PI * 3.5 * f / rate) / 12)),
            _ => input,
        };

        var samples = wet.Samples.ToArray();
        switch (Preset)
        {
            case VoicePreset.Telephone:
                new FilterProcessor(rate, channels, new FilterSettings(400, 3200)).Process(samples);
                new DistortionProcessor(rate, channels, new DistortionSettings([60], DistortionType.Overdrive, 25, 60)).Process(samples);
                break;
            case VoicePreset.Megaphone:
                new FilterProcessor(rate, channels, new FilterSettings(600, 4000)).Process(samples);
                new DistortionProcessor(rate, channels, new DistortionSettings([85], DistortionType.Overdrive, 70, 70)).Process(samples);
                break;
            case VoicePreset.Underwater:
                new FilterProcessor(rate, channels, new FilterSettings(FilterSettings.MinHz, 700)).Process(samples);
                break;
            case VoicePreset.Helium:
                new FilterProcessor(rate, channels, new FilterSettings(150, FilterSettings.MaxHz)).Process(samples);
                break;
        }

        float mix = (float)(MixPercent / 100);
        var dry = input.Samples;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = dry[i] * (1 - mix) + samples[i] * mix;
        }

        return new AudioBuffer(samples, rate, channels);
    }

    /// <summary>Roboter: Ringmodulation (metallisch) plus kurzer Kammfilter (blechern).</summary>
    private static AudioBuffer Robot(AudioBuffer input)
    {
        int rate = input.SampleRate;
        int channels = input.Channels;
        var source = input.Samples;
        var output = new float[source.Length];
        int delay = (int)(0.006 * rate);
        for (int c = 0; c < channels; c++)
        {
            var comb = new float[delay];
            int index = 0;
            for (int f = 0; f < input.FrameCount; f++)
            {
                double carrier = Math.Sin(2 * Math.PI * 55 * f / rate);
                float ring = (float)(source[f * channels + c] * carrier);
                float y = ring + comb[index] * 0.55f;
                comb[index] = y;
                index = (index + 1) % delay;
                output[f * channels + c] = y * 0.8f;
            }
        }

        return new AudioBuffer(output, rate, channels);
    }
}
