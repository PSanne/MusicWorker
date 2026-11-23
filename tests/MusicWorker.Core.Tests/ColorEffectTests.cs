using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Tests;

public class ColorEffectTests
{
    private const int Rate = 44100;

    /// <summary>Amplitude einer bestimmten Frequenz (Goertzel), über die zweite Hälfte des Signals.</summary>
    private static double AmplitudeAt(AudioBuffer buffer, double frequency, int channel = 0)
    {
        int start = buffer.FrameCount / 2;
        int n = buffer.FrameCount - start;
        double re = 0, im = 0;
        for (int i = 0; i < n; i++)
        {
            double angle = 2 * Math.PI * frequency * (start + i) / buffer.SampleRate;
            double v = buffer.GetSample(start + i, channel);
            re += v * Math.Cos(angle);
            im -= v * Math.Sin(angle);
        }

        return 2 * Math.Sqrt(re * re + im * im) / n;
    }

    private static double GainDb(AudioBuffer output, AudioBuffer input, double frequency) =>
        20 * Math.Log10(AmplitudeAt(output, frequency) / AmplitudeAt(input, frequency));

    // ───────────── Filter ─────────────

    [Fact]
    public void Neutral_filter_leaves_the_signal_unchanged()
    {
        var input = Signals.Sine(440, 0.3);

        var output = new FilterEffect(FilterSettings.Off).Apply(input);

        for (int f = 0; f < input.FrameCount; f += 37)
        {
            Assert.Equal(input.GetSample(f, 0), output.GetSample(f, 0), 1e-5f);
        }
    }

    [Fact]
    public void High_pass_removes_low_frequencies_and_keeps_high_ones()
    {
        var effect = new FilterEffect(new FilterSettings(highPassHz: 1000));

        Assert.True(GainDb(effect.Apply(Signals.Sine(100, 1)), Signals.Sine(100, 1), 100) < -60);
        Assert.Equal(0, GainDb(effect.Apply(Signals.Sine(8000, 1)), Signals.Sine(8000, 1), 8000), 1.0);
    }

    [Fact]
    public void Low_pass_removes_high_frequencies_and_keeps_low_ones()
    {
        var effect = new FilterEffect(new FilterSettings(lowPassHz: 1000));

        Assert.True(GainDb(effect.Apply(Signals.Sine(10000, 1)), Signals.Sine(10000, 1), 10000) < -60);
        Assert.Equal(0, GainDb(effect.Apply(Signals.Sine(100, 1)), Signals.Sine(100, 1), 100), 1.0);
    }

    [Fact]
    public void Cutoff_frequency_is_about_minus_3_dB()
    {
        var effect = new FilterEffect(new FilterSettings(highPassHz: 1000));

        Assert.Equal(-3, GainDb(effect.Apply(Signals.Sine(1000, 1)), Signals.Sine(1000, 1), 1000), 0.6);
    }

    [Theory]
    [InlineData(true, 30, 6)]
    [InlineData(true, 8000, 0)]
    [InlineData(false, 15000, 6)]
    [InlineData(false, 100, 0)]
    public void Shelf_filters_boost_their_side(bool low, double frequency, double expectedDb)
    {
        var c = low
            ? BiquadCoefficients.LowShelf(Rate, 200, 6)
            : BiquadCoefficients.HighShelf(Rate, 5000, 6);

        Assert.Equal(expectedDb, c.MagnitudeDb(frequency, Rate), 0.6);
    }

    // ───────────── Verzerrung ─────────────

    [Theory]
    [InlineData(DistortionType.Overdrive)]
    [InlineData(DistortionType.Fuzz)]
    [InlineData(DistortionType.Bitcrusher)]
    public void Zero_mix_leaves_the_signal_unchanged(DistortionType type)
    {
        var input = Signals.Sine(440, 0.2);

        var output = new DistortionEffect(new DistortionSettings([0], type, 80), preventClipping: false).Apply(input);

        Assert.Equal(input.Samples.ToArray(), output.Samples.ToArray());
    }

    [Fact]
    public void Overdrive_creates_harmonics()
    {
        var input = Signals.Sine(440, 1, 0.5f);

        var output = new DistortionEffect(new DistortionSettings([100], DistortionType.Overdrive, 70, 100), false).Apply(input);

        double fundamental = AmplitudeAt(output, 440);
        double third = AmplitudeAt(output, 1320);
        Assert.True(AmplitudeAt(input, 1320) < 1e-4, "Ein reiner Sinus hat keine Obertöne");
        Assert.True(third > 0.1 * fundamental, $"3. Harmonische {third:0.000} vs. Grundton {fundamental:0.000}");
    }

    [Fact]
    public void Fuzz_stays_within_full_scale_and_has_no_dc_offset()
    {
        var input = Signals.Sine(220, 1, 0.9f);

        var output = new DistortionEffect(new DistortionSettings([100], DistortionType.Fuzz, 100, 100), false).Apply(input);

        Assert.True(Signals.Peak(output) <= 1.0f);
        double mean = output.Samples.ToArray().Skip(Rate / 2).Average(v => (double)v);
        Assert.True(Math.Abs(mean) < 0.02, $"Gleichanteil {mean}");
    }

    [Fact]
    public void Bitcrusher_reduces_the_number_of_levels()
    {
        var input = Signals.Sine(220, 0.5, 0.9f);

        var output = new DistortionEffect(new DistortionSettings([100], DistortionType.Bitcrusher, 100, 100), false).Apply(input);

        int distinct = output.Samples.ToArray().Select(v => MathF.Round(v, 4)).Distinct().Count();
        Assert.InRange(distinct, 2, 60); // statt tausender Stufen nur wenige (Ton-Tiefpass glättet etwas)
    }

    [Fact]
    public void Distortion_mix_works_per_channel()
    {
        var input = Signals.Sine(440, 0.3, 0.5f, Rate, channels: 2);

        var output = new DistortionEffect(new DistortionSettings([100, 0], drivePercent: 80), false).Apply(input);

        Assert.Equal(input.GetChannel(1).Samples.ToArray(), output.GetChannel(1).Samples.ToArray());
        Assert.NotEqual(input.GetChannel(0).Samples.ToArray(), output.GetChannel(0).Samples.ToArray());
    }

    // ───────────── Chorus & Flanger ─────────────

    [Fact]
    public void Chorus_with_zero_mix_is_neutral_and_with_mix_keeps_the_level()
    {
        var input = Signals.Sine(440, 1, 0.5f, Rate, channels: 2);

        var neutral = new ChorusEffect(new ModulationSettings([0])).Apply(input);
        var chorus = new ChorusEffect(new ModulationSettings([100], 1.5, 80)).Apply(input);

        Assert.Equal(input.Samples.ToArray(), neutral.Samples.ToArray());
        Assert.NotEqual(input.Samples.ToArray(), chorus.Samples.ToArray());
        double ratioDb = 20 * Math.Log10(Signals.Rms(chorus, 0.3) / Signals.Rms(input, 0.3));
        Assert.InRange(ratioDb, -6, 1);
    }

    [Fact]
    public void Chorus_channels_are_modulated_differently_for_width()
    {
        var input = Signals.Sine(440, 1, 0.5f, Rate, channels: 2);

        var output = new ChorusEffect(new ModulationSettings([100], 1, 100)).Apply(input);

        Assert.NotEqual(output.GetChannel(0).Samples.ToArray(), output.GetChannel(1).Samples.ToArray());
    }

    [Fact]
    public void Flanger_adds_a_short_delayed_copy()
    {
        var impulse = new float[Rate / 2];
        impulse[0] = 1f;

        var output = new FlangerEffect(new ModulationSettings([100], 0.1, 50, 0), preventClipping: false)
            .Apply(new AudioBuffer(impulse, Rate, 1));

        // Direkter Anteil halb so laut, dazu ein Echo innerhalb der ersten ~5 ms
        Assert.Equal(0.5f, output.GetSample(0, 0), 1e-5f);
        double echo = Enumerable.Range(2, (int)(0.0052 * Rate)).Max(f => Math.Abs(output.GetSample(f, 0)));
        Assert.True(echo > 0.2, $"Echo {echo}");
    }

    [Fact]
    public void Flanger_feedback_creates_repeats_and_stays_stable()
    {
        var random = new Random(5);
        var noise = Enumerable.Range(0, Rate * 3).Select(_ => (float)(random.NextDouble() - 0.5)).ToArray();

        var output = new FlangerEffect(new ModulationSettings([100], 2, 100, 90), preventClipping: false)
            .Apply(new AudioBuffer(noise, Rate, 1));

        Assert.All(output.Samples.ToArray(), v => Assert.True(float.IsFinite(v) && Math.Abs(v) < 10));
    }

    [Fact]
    public void Modulation_mix_works_per_channel()
    {
        var input = Signals.Sine(440, 0.5, 0.5f, Rate, channels: 2);

        var output = new FlangerEffect(new ModulationSettings([0, 100], 1, 80, 50), preventClipping: false).Apply(input);

        Assert.Equal(input.GetChannel(0).Samples.ToArray(), output.GetChannel(0).Samples.ToArray());
    }
}
