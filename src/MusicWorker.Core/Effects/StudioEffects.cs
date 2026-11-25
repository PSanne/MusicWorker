using System.Globalization;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Effects;

/// <summary>Noise-Gate als Effekt (für „Übernehmen“ und Export).</summary>
public sealed class GateEffect(GateSettings settings) : SampleEffect
{
    public override string Name => string.Format(CultureInfo.CurrentCulture, "Noise-Gate ({0:0} dB)", settings.ThresholdDb);

    protected override void Process(Span<float> samples, int channels, int sampleRate) =>
        new GateProcessor(sampleRate, channels, settings).Process(samples);
}

public sealed class TremoloEffect(TremoloSettings settings) : SampleEffect
{
    public override string Name => settings.StereoPan ? "Tremolo (Stereo-Pendel)" : "Tremolo";

    protected override void Process(Span<float> samples, int channels, int sampleRate) =>
        new TremoloProcessor(sampleRate, channels, settings).Process(samples);
}

public sealed class PhaserEffect(PhaserSettings settings) : SampleEffect
{
    public override string Name => "Phaser";

    protected override void Process(Span<float> samples, int channels, int sampleRate) =>
        new PhaserProcessor(sampleRate, channels, settings).Process(samples);
}

public sealed class WidenerEffect(WidenerSettings settings) : SampleEffect
{
    public override string Name => string.Format(CultureInfo.CurrentCulture, "Stereobreite {0:0} %", settings.WidthPercent);

    protected override void Process(Span<float> samples, int channels, int sampleRate) =>
        new WidenerProcessor(channels, settings).Process(samples);
}

public sealed class SaturationEffect(SaturationSettings settings) : SampleEffect
{
    public override string Name => "Bandsättigung";

    protected override void Process(Span<float> samples, int channels, int sampleRate) =>
        new SaturationProcessor(sampleRate, channels, settings).Process(samples);
}
