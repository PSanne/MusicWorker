namespace MusicWorker.Core.Dsp;

/// <summary>Charakter der Verzerrung.</summary>
public enum DistortionType
{
    /// <summary>Warm, wie ein übersteuerter Röhrenverstärker (weiche Sättigung).</summary>
    Overdrive,

    /// <summary>Hart und kratzig (asymmetrisches Abschneiden).</summary>
    Fuzz,

    /// <summary>Lo-Fi, wie alte Computer: weniger Bits und geringere Abtastrate.</summary>
    Bitcrusher,
}

/// <summary>Einstellungen der Verzerrung; die Stärke (Mischung) ist pro Kanal einstellbar.</summary>
public sealed class DistortionSettings
{
    public DistortionSettings(
        IReadOnlyList<double> mixPercent,
        DistortionType type = DistortionType.Overdrive,
        double drivePercent = 50,
        double tonePercent = 70)
    {
        ArgumentNullException.ThrowIfNull(mixPercent);
        if (mixPercent.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Kanal ist erforderlich.", nameof(mixPercent));
        }

        MixPercent = mixPercent.Select(m => Math.Clamp(m, 0, 100)).ToArray();
        Type = type;
        DrivePercent = Math.Clamp(drivePercent, 0, 100);
        TonePercent = Math.Clamp(tonePercent, 0, 100);
    }

    public static DistortionSettings Off { get; } = new([0]);

    public IReadOnlyList<double> MixPercent { get; }

    public DistortionType Type { get; }

    /// <summary>Wie stark verzerrt wird.</summary>
    public double DrivePercent { get; }

    /// <summary>Klangfarbe: wenig = dumpf, viel = hell.</summary>
    public double TonePercent { get; }

    public bool IsNeutral => MixPercent.All(m => m <= 0);

    public double MixFor(int channel) => MixPercent[Math.Min(channel, MixPercent.Count - 1)];
}

/// <summary>Verzerrung (Overdrive, Fuzz, Bitcrusher) mit Klangregler und Stärke pro Kanal.</summary>
public sealed class DistortionProcessor
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly float[] _tone;
    private readonly float[] _dcIn;
    private readonly float[] _dcOut;
    private readonly float[] _held;
    private int _holdCounter;
    private Snapshot _snapshot = null!;

    public DistortionProcessor(int sampleRate, int channels, DistortionSettings settings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _sampleRate = sampleRate;
        _channels = channels;
        _tone = new float[channels];
        _dcIn = new float[channels];
        _dcOut = new float[channels];
        _held = new float[channels];
        Update(settings);
    }

    public void Update(DistortionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        double drive = settings.DrivePercent / 100;
        var mix = new float[_channels];
        for (int c = 0; c < _channels; c++)
        {
            mix[c] = (float)(settings.MixFor(c) / 100);
        }

        double cutoff = 800 * Math.Pow(20000.0 / 800, settings.TonePercent / 100); // 800 Hz … 20 kHz
        Volatile.Write(ref _snapshot, new Snapshot(
            settings.Type,
            (float)Math.Pow(10, drive * 36 / 20),                     // 0 … +36 dB Vorverstärkung
            (float)(1 - Math.Exp(-2 * Math.PI * Math.Min(cutoff, _sampleRate * 0.45) / _sampleRate)),
            (float)Math.Pow(2, 16 - drive * 14),                        // 16 … 2 Bit
            1 + (int)(drive * 15),                                      // 1 … 16-fache Unterabtastung
            mix));
    }

    public void Process(Span<float> samples)
    {
        var s = Volatile.Read(ref _snapshot);
        int frames = samples.Length / _channels;
        for (int frame = 0; frame < frames; frame++)
        {
            bool takeNew = s.Type != DistortionType.Bitcrusher || _holdCounter == 0;
            for (int c = 0; c < _channels; c++)
            {
                int i = frame * _channels + c;
                float dry = samples[i];
                float wet = s.Type switch
                {
                    DistortionType.Overdrive => 0.6f * MathF.Tanh(dry * s.Gain),
                    DistortionType.Fuzz => Fuzz(dry * s.Gain, c),
                    _ => Crush(dry, c, takeNew, s),
                };

                // Klangregler: einfacher Tiefpass nach der Verzerrung
                _tone[c] += s.ToneCoefficient * (wet - _tone[c]);
                wet = _tone[c];
                samples[i] = dry + (wet - dry) * s.Mix[c];
            }

            if (s.Type == DistortionType.Bitcrusher)
            {
                _holdCounter = (_holdCounter + 1) % s.HoldFrames;
            }
        }
    }

    /// <summary>Asymmetrisches hartes Abschneiden, danach Gleichanteil entfernen.</summary>
    private float Fuzz(float x, int channel)
    {
        float clipped = 0.75f * Math.Clamp(x, -0.45f, 0.9f);
        float output = clipped - _dcIn[channel] + 0.995f * _dcOut[channel];
        _dcIn[channel] = clipped;
        _dcOut[channel] = output;
        return output;
    }

    /// <summary>Weniger Bits (grobe Stufen) und Sample-and-Hold (niedrigere Abtastrate).</summary>
    private float Crush(float x, int channel, bool takeNew, Snapshot s)
    {
        if (takeNew)
        {
            float levels = s.Levels / 2;
            _held[channel] = MathF.Round(Math.Clamp(x, -1f, 1f) * levels) / levels;
        }

        return _held[channel];
    }

    private sealed record Snapshot(
        DistortionType Type, float Gain, float ToneCoefficient, float Levels, int HoldFrames, float[] Mix);
}
