namespace MusicWorker.Core.Effects;

public enum FadeDirection
{
    /// <summary>Einblenden: von Stille auf volle Lautstärke.</summary>
    In,

    /// <summary>Ausblenden: von voller Lautstärke auf Stille.</summary>
    Out,
}

/// <summary>Lineares Ein- bzw. Ausblenden über den gesamten Ausschnitt.</summary>
public sealed class FadeEffect : SampleEffect
{
    public FadeEffect(FadeDirection direction)
    {
        Direction = direction;
    }

    public FadeDirection Direction { get; }

    public override string Name => Direction == FadeDirection.In ? "Einblenden" : "Ausblenden";

    protected override void Process(Span<float> samples, int channels, int sampleRate)
    {
        int frames = samples.Length / channels;
        if (frames == 0)
        {
            return;
        }

        double lastFrame = Math.Max(1, frames - 1);
        for (int frame = 0; frame < frames; frame++)
        {
            double t = frame / lastFrame; // 0 … 1
            float gain = (float)(Direction == FadeDirection.In ? t : 1.0 - t);
            int offset = frame * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                samples[offset + channel] *= gain;
            }
        }
    }
}
