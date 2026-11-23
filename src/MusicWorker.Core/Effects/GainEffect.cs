using System.Globalization;
using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Effects;

/// <summary>Ändert die Lautstärke um einen festen Wert in Dezibel.</summary>
public sealed class GainEffect : SampleEffect
{
    public GainEffect(double decibels)
    {
        Decibels = decibels;
    }

    public double Decibels { get; }

    public override string Name =>
        string.Format(CultureInfo.CurrentCulture, "Lautstärke {0:+0.0;-0.0;0.0} dB", Decibels);

    protected override void Process(Span<float> samples, int channels, int sampleRate)
    {
        float gain = (float)Audio.Decibels.ToGain(Decibels);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= gain;
        }
    }
}
