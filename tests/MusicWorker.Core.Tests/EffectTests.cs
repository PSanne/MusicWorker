using MusicWorker.Core.Audio;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Tests;

public class EffectTests
{
    [Fact]
    public void Normalize_scales_peak_to_target()
    {
        var input = new AudioBuffer([0.1f, -0.25f, 0.2f, 0f], 1000, 2);

        var output = new NormalizeEffect(targetPeakDecibels: 0).Apply(input);

        Assert.Equal(1f, output.Samples.ToArray().Max(Math.Abs), precision: 4);
        Assert.Equal(-1f, output.GetSample(0, 1), precision: 4);
    }

    [Fact]
    public void Normalize_leaves_silence_untouched()
    {
        var output = new NormalizeEffect().Apply(AudioBuffer.CreateSilence(1000, 1, 10));

        Assert.All(output.Samples.ToArray(), s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Fade_in_goes_from_silence_to_full_level()
    {
        var input = new AudioBuffer([1f, 1f, 1f, 1f, 1f], 1000, 1);

        var output = new FadeEffect(FadeDirection.In).Apply(input);

        Assert.Equal(new[] { 0f, 0.25f, 0.5f, 0.75f, 1f }, output.Samples.ToArray());
    }

    [Fact]
    public void Fade_out_applies_the_same_gain_to_every_channel()
    {
        var input = new AudioBuffer([1f, 0.5f, 1f, 0.5f, 1f, 0.5f], 1000, 2);

        var output = new FadeEffect(FadeDirection.Out).Apply(input);

        Assert.Equal(new[] { 1f, 0.5f, 0.5f, 0.25f, 0f, 0f }, output.Samples.ToArray());
    }

    [Fact]
    public void Effects_do_not_change_their_input()
    {
        var input = new AudioBuffer([0.5f, 0.5f], 1000, 1);

        _ = new GainEffect(6).Apply(input);

        Assert.Equal(0.5f, input.GetSample(0, 0));
    }
}
