namespace MusicWorker.Core.Dsp;

/// <summary>Einstellungen für Chorus und Flanger (Geschwindigkeit, Tiefe, Rückkopplung, Stärke pro Kanal).</summary>
public sealed class ModulationSettings
{
    public ModulationSettings(
        IReadOnlyList<double> mixPercent,
        double rateHz = 0.8,
        double depthPercent = 50,
        double feedbackPercent = 0)
    {
        ArgumentNullException.ThrowIfNull(mixPercent);
        if (mixPercent.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Kanal ist erforderlich.", nameof(mixPercent));
        }

        MixPercent = mixPercent.Select(m => Math.Clamp(m, 0, 100)).ToArray();
        RateHz = Math.Clamp(rateHz, 0.02, 10);
        DepthPercent = Math.Clamp(depthPercent, 0, 100);
        FeedbackPercent = Math.Clamp(feedbackPercent, 0, 90);
    }

    public static ModulationSettings Off { get; } = new([0]);

    public IReadOnlyList<double> MixPercent { get; }

    /// <summary>Wie schnell die Verzögerung schwankt (Hz).</summary>
    public double RateHz { get; }

    /// <summary>Wie weit die Verzögerung schwankt.</summary>
    public double DepthPercent { get; }

    /// <summary>Rückkopplung (nur Flanger): macht den Effekt schärfer, „metallischer“.</summary>
    public double FeedbackPercent { get; }

    public bool IsNeutral => MixPercent.All(m => m <= 0);

    public double MixFor(int channel) => MixPercent[Math.Min(channel, MixPercent.Count - 1)];
}

/// <summary>
/// Gemeinsame Grundlage für Chorus und Flanger: eine Verzögerungsleitung pro Kanal, deren
/// Verzögerung von einem langsamen Sinus (LFO) moduliert wird. Die Kanäle laufen um 90° versetzt –
/// das macht den Klang breiter.
/// </summary>
public abstract class ModulationProcessor
{
    private readonly float[][] _lines;
    private readonly int _channels;
    private readonly int _channelOffset;
    private int _write;
    private double _phase;
    private Snapshot _snapshot = null!;

    protected ModulationProcessor(int sampleRate, int channels, ModulationSettings settings, int channelOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        SampleRate = sampleRate;
        _channels = channels;
        _channelOffset = channelOffset;
        _lines = Enumerable.Range(0, channels).Select(_ => new float[(int)(0.05 * sampleRate) + 4]).ToArray();
        Update(settings);
    }

    protected int SampleRate { get; }

    public void Update(ModulationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var mix = new float[_channels];
        for (int c = 0; c < _channels; c++)
        {
            mix[c] = (float)(settings.MixFor(c) / 100);
        }

        Volatile.Write(ref _snapshot, new Snapshot(
            2 * Math.PI * settings.RateHz / SampleRate,
            settings.DepthPercent / 100,
            (float)(settings.FeedbackPercent / 100),
            mix));
    }

    public void Process(Span<float> samples)
    {
        var s = Volatile.Read(ref _snapshot);
        int length = _lines[0].Length;
        int frames = samples.Length / _channels;

        for (int frame = 0; frame < frames; frame++)
        {
            for (int c = 0; c < _channels; c++)
            {
                int i = frame * _channels + c;
                float dry = samples[i];
                double phase = _phase + (c + _channelOffset) * Math.PI / 2;
                float wet = ProcessSample(_lines[c], length, dry, phase, s.Depth, s.Feedback);
                samples[i] = dry * (1 - 0.5f * s.Mix[c]) + wet * 0.5f * s.Mix[c];
            }

            _write = _write + 1 == length ? 0 : _write + 1;
            _phase += s.PhaseStep;
            if (_phase > 2 * Math.PI)
            {
                _phase -= 2 * Math.PI;
            }
        }
    }

    /// <summary>Berechnet das Effektsignal eines Samples und schreibt die Verzögerungsleitung.</summary>
    protected abstract float ProcessSample(float[] line, int length, float input, double phase, double depth, float feedback);

    /// <summary>Wert von vor <paramref name="delaySamples"/> Samples (linear interpoliert).</summary>
    protected float Read(float[] line, int length, double delaySamples)
    {
        double position = _write - delaySamples;
        while (position < 0)
        {
            position += length;
        }

        int index = (int)position;
        float t = (float)(position - index);
        int next = index + 1 == length ? 0 : index + 1;
        return line[index] * (1 - t) + line[next] * t;
    }

    protected void Write(float[] line, float value) => line[_write] = Math.Abs(value) < 1e-25f ? 0 : value;

    private sealed record Snapshot(double PhaseStep, double Depth, float Feedback, float[] Mix);
}

/// <summary>Chorus: zwei leicht verzögerte, schwankende Stimmen – klingt voller, wie mehrere Sänger.</summary>
public sealed class ChorusProcessor(int sampleRate, int channels, ModulationSettings settings, int channelOffset = 0)
    : ModulationProcessor(sampleRate, channels, settings, channelOffset)
{
    private const double BaseDelaySeconds = 0.015;
    private const double MaxSwingSeconds = 0.008;

    protected override float ProcessSample(float[] line, int length, float input, double phase, double depth, float feedback)
    {
        double swing = MaxSwingSeconds * depth * SampleRate;
        double baseDelay = BaseDelaySeconds * SampleRate;
        float voice1 = Read(line, length, baseDelay + swing * (0.5 + 0.5 * Math.Sin(phase)));
        float voice2 = Read(line, length, baseDelay + swing * (0.5 + 0.5 * Math.Sin(phase + Math.PI)));
        Write(line, input);
        return 0.5f * (voice1 + voice2);
    }
}

/// <summary>Flanger: sehr kurze, wandernde Verzögerung mit Rückkopplung – der typische „Düsenjet“-Klang.</summary>
public sealed class FlangerProcessor(int sampleRate, int channels, ModulationSettings settings, int channelOffset = 0)
    : ModulationProcessor(sampleRate, channels, settings, channelOffset)
{
    private const double MinDelaySeconds = 0.0001;
    private const double MaxSwingSeconds = 0.005;

    protected override float ProcessSample(float[] line, int length, float input, double phase, double depth, float feedback)
    {
        double delay = (MinDelaySeconds + MaxSwingSeconds * depth * (0.5 + 0.5 * Math.Sin(phase))) * SampleRate;
        float delayed = Read(line, length, Math.Max(1, delay));
        Write(line, input + delayed * feedback);
        return delayed;
    }
}
