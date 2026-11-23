using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Effects;

/// <summary>
/// Verstärkt (oder senkt) so, dass der lauteste Ausschlag genau den Zielpegel erreicht.
/// Standard: -1 dBFS, damit beim Export etwas Reserve gegen Übersteuerung bleibt.
/// </summary>
public sealed class NormalizeEffect : SampleEffect
{
    public NormalizeEffect(double targetPeakDecibels = -1.0)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(targetPeakDecibels, 0.0);
        TargetPeakDecibels = targetPeakDecibels;
    }

    public double TargetPeakDecibels { get; }

    public override string Name => "Normalisieren";

    protected override void Process(Span<float> samples, int channels, int sampleRate)
    {
        float peak = 0f;
        foreach (float sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        if (peak <= 0f)
        {
            return; // reine Stille – nichts zu tun
        }

        float factor = (float)(Decibels.ToGain(TargetPeakDecibels) / peak);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= factor;
        }
    }
}
