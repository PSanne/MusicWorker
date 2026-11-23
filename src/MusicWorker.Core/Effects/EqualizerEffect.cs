using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Effects;

/// <summary>Wendet den 15-Band-Equalizer an – für alle Kanäle gleich oder pro Kanal verschieden.</summary>
public sealed class EqualizerEffect : SampleEffect
{
    private static readonly float ClipLimit = (float)Decibels.ToGain(-1.0);

    public EqualizerEffect(EqualizerSettings settings, bool preventClipping = true)
        : this([settings ?? throw new ArgumentNullException(nameof(settings))], preventClipping)
    {
    }

    /// <summary>Pro-Kanal-EQ: eine Einstellung je Kanal (0 = links, 1 = rechts).</summary>
    public EqualizerEffect(IReadOnlyList<EqualizerSettings> perChannel, bool preventClipping = true)
    {
        ArgumentNullException.ThrowIfNull(perChannel);
        if (perChannel.Count == 0)
        {
            throw new ArgumentException("Mindestens eine Einstellung ist erforderlich.", nameof(perChannel));
        }

        ChannelSettings = perChannel.ToArray();
        PreventClipping = preventClipping;
    }

    /// <summary>Einstellung des ersten Kanals (bzw. die gemeinsame Einstellung).</summary>
    public EqualizerSettings Settings => ChannelSettings[0];

    /// <summary>Einstellungen je Kanal; bei nur einer gilt sie für alle Kanäle.</summary>
    public IReadOnlyList<EqualizerSettings> ChannelSettings { get; }

    /// <summary>Wenn true, wird das Ergebnis bei Übersteuerung auf -1 dBFS heruntergeregelt.</summary>
    public bool PreventClipping { get; }

    public override string Name => ChannelSettings.Count > 1 ? "Equalizer (L/R getrennt)" : "Equalizer";

    protected override void Process(Span<float> samples, int channels, int sampleRate)
    {
        new EqualizerProcessor(sampleRate, channels, ChannelSettings).Process(samples);

        if (!PreventClipping)
        {
            return;
        }

        float peak = 0;
        foreach (float sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        if (peak > ClipLimit)
        {
            float factor = ClipLimit / peak;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] *= factor;
            }
        }
    }
}
