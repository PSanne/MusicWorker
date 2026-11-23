using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Tests;

public class EqualizerTests
{
    private static EqualizerSettings WithBand(double frequency, double gainDb)
    {
        var gains = new double[EqualizerSettings.BandFrequencies.Count];
        gains[EqualizerSettings.BandFrequencies.ToList().IndexOf(frequency)] = gainDb;
        return new EqualizerSettings(gains);
    }

    [Fact]
    public void Has_fifteen_bands_from_25_Hz_to_16_kHz()
    {
        Assert.Equal(15, EqualizerSettings.BandFrequencies.Count);
        Assert.Equal(25, EqualizerSettings.BandFrequencies[0]);
        Assert.Equal(16000, EqualizerSettings.BandFrequencies[^1]);
    }

    [Fact]
    public void Neutral_settings_leave_the_signal_unchanged()
    {
        var input = Signals.Sine(440, 0.5);

        var output = new EqualizerEffect(EqualizerSettings.Neutral).Apply(input);

        for (int f = 0; f < input.FrameCount; f++)
        {
            Assert.Equal(input.GetSample(f, 0), output.GetSample(f, 0), 1e-5f);
        }
    }

    [Theory]
    [InlineData(12, 3.98)]
    [InlineData(-12, 0.251)]
    [InlineData(6, 1.995)]
    public void Band_boosts_or_cuts_its_own_frequency(double gainDb, double expectedRatio)
    {
        var input = Signals.Sine(1000, 1, amplitude: 0.1f);

        var output = new EqualizerEffect(WithBand(1000, gainDb), preventClipping: false).Apply(input);

        Assert.Equal(expectedRatio, Signals.Rms(output) / Signals.Rms(input), 0.05 * expectedRatio);
    }

    [Fact]
    public void Band_does_not_affect_distant_frequencies()
    {
        var input = Signals.Sine(100, 1, amplitude: 0.1f);

        var output = new EqualizerEffect(WithBand(10000, 12), preventClipping: false).Apply(input);

        Assert.Equal(1.0, Signals.Rms(output) / Signals.Rms(input), 0.03);
    }

    [Fact]
    public void Preamp_scales_the_whole_signal()
    {
        var input = Signals.Sine(440, 1, amplitude: 0.1f);
        var settings = new EqualizerSettings(new double[15], preampDb: -6.0206);

        var output = new EqualizerEffect(settings).Apply(input);

        Assert.Equal(0.5, Signals.Rms(output) / Signals.Rms(input), 0.01);
    }

    [Fact]
    public void Prevent_clipping_keeps_peak_below_minus_one_dB()
    {
        var input = Signals.Sine(1000, 1, amplitude: 0.9f);

        var output = new EqualizerEffect(WithBand(1000, 12), preventClipping: true).Apply(input);

        Assert.True(Signals.Peak(output) <= 0.8913f + 1e-4f, $"Spitze {Signals.Peak(output)}");
    }

    [Fact]
    public void Gains_are_limited_to_plus_minus_12_dB()
    {
        var gains = Enumerable.Repeat(40.0, 15).ToArray();

        var settings = new EqualizerSettings(gains, preampDb: -40);

        Assert.All(settings.GainsDb, g => Assert.Equal(12, g));
        Assert.Equal(-12, settings.PreampDb);
    }

    [Fact]
    public void Live_update_changes_processing_without_reset()
    {
        var processor = new EqualizerProcessor(44100, 1, EqualizerSettings.Neutral);
        var block = Signals.Sine(1000, 1, amplitude: 0.1f).Samples.ToArray();

        processor.Update(WithBand(1000, 12));
        processor.Process(block);

        var output = new Audio.AudioBuffer(block, 44100, 1);
        Assert.Equal(3.98, Signals.Rms(output) / (0.1 / Math.Sqrt(2)), 0.2);
    }

    [Fact]
    public void All_presets_have_fifteen_values_within_range()
    {
        Assert.NotEmpty(EqualizerPresets.All);
        Assert.All(EqualizerPresets.All, p =>
        {
            Assert.Equal(15, p.GainsDb.Count);
            Assert.All(p.GainsDb, g => Assert.InRange(g, -12, 12));
        });
    }

    [Fact]
    public void Bands_above_nyquist_are_bypassed()
    {
        var c = BiquadCoefficients.PeakingEq(sampleRate: 22050, frequency: 16000, q: 2, gainDb: 12);

        Assert.Equal(BiquadCoefficients.Identity, c);
    }
}
