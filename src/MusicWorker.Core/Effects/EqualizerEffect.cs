using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Effects;

/// <summary>Wendet den 15-Band-Equalizer an.</summary>
public sealed class EqualizerEffect : SampleEffect
{
    private static readonly float ClipLimit = (float)Decibels.ToGain(-1.0);

    public EqualizerEffect(EqualizerSettings settings, bool preventClipping = true)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        PreventClipping = preventClipping;
    }

    public EqualizerSettings Settings { get; }

    /// <summary>Wenn true, wird das Ergebnis bei Übersteuerung auf -1 dBFS heruntergeregelt.</summary>
    public bool PreventClipping { get; }

    public override string Name => "Equalizer";

    protected override void Process(Span<float> samples, int channels, int sampleRate)
    {
        new EqualizerProcessor(sampleRate, channels, Settings).Process(samples);

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
