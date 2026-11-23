using System.Globalization;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Effects;

/// <summary>Hochpass/Tiefpass als Effekt (für „In Audio übernehmen“ und Export).</summary>
public sealed class FilterEffect(FilterSettings settings) : SampleEffect
{
    public FilterSettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

    public override string Name => (Settings.HighPassActive, Settings.LowPassActive) switch
    {
        (true, true) => string.Format(CultureInfo.CurrentCulture, "Filter ({0:0} Hz – {1:0} Hz)", Settings.HighPassHz, Settings.LowPassHz),
        (true, false) => string.Format(CultureInfo.CurrentCulture, "Hochpass {0:0} Hz", Settings.HighPassHz),
        _ => string.Format(CultureInfo.CurrentCulture, "Tiefpass {0:0} Hz", Settings.LowPassHz),
    };

    protected override void Process(Span<float> samples, int channels, int sampleRate) =>
        new FilterProcessor(sampleRate, channels, Settings).Process(samples);
}

/// <summary>Verzerrung als Effekt.</summary>
public sealed class DistortionEffect(DistortionSettings settings, bool preventClipping = true) : SampleEffect
{
    private static readonly float ClipLimit = (float)Decibels.ToGain(-1.0);

    public DistortionSettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

    public override string Name => Settings.Type switch
    {
        DistortionType.Fuzz => "Verzerrung (Fuzz)",
        DistortionType.Bitcrusher => "Verzerrung (Bitcrusher)",
        _ => "Verzerrung (Overdrive)",
    };

    protected override void Process(Span<float> samples, int channels, int sampleRate)
    {
        new DistortionProcessor(sampleRate, channels, Settings).Process(samples);
        if (preventClipping)
        {
            Limit(samples, ClipLimit);
        }
    }

    internal static void Limit(Span<float> samples, float limit)
    {
        float peak = 0;
        foreach (float sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        if (peak > limit)
        {
            float factor = limit / peak;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] *= factor;
            }
        }
    }
}

/// <summary>Chorus als Effekt.</summary>
public sealed class ChorusEffect(ModulationSettings settings, int channelOffset = 0) : SampleEffect
{
    public ModulationSettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

    public override string Name => "Chorus";

    protected override void Process(Span<float> samples, int channels, int sampleRate) =>
        new ChorusProcessor(sampleRate, channels, Settings, channelOffset).Process(samples);
}

/// <summary>Flanger als Effekt.</summary>
public sealed class FlangerEffect(ModulationSettings settings, int channelOffset = 0, bool preventClipping = true) : SampleEffect
{
    private static readonly float ClipLimit = (float)Decibels.ToGain(-1.0);

    public ModulationSettings Settings { get; } = settings ?? throw new ArgumentNullException(nameof(settings));

    public override string Name => "Flanger";

    protected override void Process(Span<float> samples, int channels, int sampleRate)
    {
        new FlangerProcessor(sampleRate, channels, Settings, channelOffset).Process(samples);
        if (preventClipping)
        {
            DistortionEffect.Limit(samples, ClipLimit);
        }
    }
}
