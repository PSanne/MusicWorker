using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Tests;

public class StudioEffectTests
{
    private const int Rate = 44100;

    private static float[] Sine(double hz, double amplitude, double seconds, int channels = 2)
    {
        int frames = (int)(seconds * Rate);
        var samples = new float[frames * channels];
        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < channels; c++)
            {
                samples[f * channels + c] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * f / Rate));
            }
        }

        return samples;
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (float v in samples)
        {
            sum += v * v;
        }

        return Math.Sqrt(sum / Math.Max(1, samples.Length));
    }

    [Fact]
    public void Gate_silences_noise_in_pauses_and_keeps_speech()
    {
        // 0,5 s leises Rauschen (-60 dB), 0,5 s Ton (-12 dB), 0,5 s Rauschen
        var random = new Random(1);
        var samples = new float[(int)(1.5 * Rate) * 2];
        for (int f = 0; f < samples.Length / 2; f++)
        {
            float v = f >= Rate / 2 && f < Rate ? (float)(0.25 * Math.Sin(2 * Math.PI * 300 * f / Rate)) : (float)(0.001 * (random.NextDouble() - 0.5) * 2);
            samples[2 * f] = samples[2 * f + 1] = v;
        }

        var before = (float[])samples.Clone();
        new GateProcessor(Rate, 2, new GateSettings(-40, 40)).Process(samples);

        double noiseRatio = Rms(samples.AsSpan(Rate * 2 + Rate / 2, Rate / 2)) / Rms(before.AsSpan(Rate * 2 + Rate / 2, Rate / 2));
        double toneRatio = Rms(samples.AsSpan(Rate + Rate / 4, Rate / 2)) / Rms(before.AsSpan(Rate + Rate / 4, Rate / 2));
        Assert.True(noiseRatio < 0.05, $"Rauschen nur auf {noiseRatio:0.000}");
        Assert.InRange(toneRatio, 0.98, 1.001);
    }

    [Fact]
    public void Tremolo_modulates_at_its_rate_and_pans()
    {
        var samples = Sine(1000, 0.5, 1);
        new TremoloProcessor(Rate, 2, new TremoloSettings(4, 100, StereoPan: true)).Process(samples);

        // Bei 4 Hz: nach 1/8 s (halbe Periode) ist links laut und rechts leise.
        int f = Rate / 8;
        double left = Rms(Enumerable.Range(f - 100, 200).Select(i => samples[2 * i]).ToArray());
        double right = Rms(Enumerable.Range(f - 100, 200).Select(i => samples[2 * i + 1]).ToArray());
        Assert.True(left < 0.05 && right > 0.3, $"L {left:0.000}, R {right:0.000}");
    }

    [Fact]
    public void Phaser_changes_the_sound_but_stays_bounded()
    {
        var random = new Random(2);
        var samples = Enumerable.Range(0, Rate * 2).Select(_ => (float)((random.NextDouble() - 0.5) * 0.5)).ToArray();
        var before = (float[])samples.Clone();

        new PhaserProcessor(Rate, 2, new PhaserSettings(1, 80, 50, 100)).Process(samples);

        double difference = Rms(samples.Zip(before, (a, b) => a - b).ToArray());
        Assert.True(difference > 0.02, $"kaum verändert ({difference:0.000})");
        Assert.All(samples, v => Assert.InRange(v, -1.5f, 1.5f));
        Assert.DoesNotContain(samples, float.IsNaN);
    }

    [Fact]
    public void Widener_zero_is_mono_and_wide_increases_side()
    {
        var samples = new float[Rate * 2];
        for (int f = 0; f < Rate; f++)
        {
            samples[2 * f] = (float)(0.5 * Math.Sin(2 * Math.PI * 200 * f / Rate));
            samples[2 * f + 1] = (float)(0.2 * Math.Sin(2 * Math.PI * 300 * f / Rate));
        }

        var mono = (float[])samples.Clone();
        new WidenerProcessor(2, new WidenerSettings(0)).Process(mono);
        Assert.All(Enumerable.Range(0, Rate), f => Assert.Equal(mono[2 * f], mono[2 * f + 1]));

        var wide = (float[])samples.Clone();
        new WidenerProcessor(2, new WidenerSettings(200)).Process(wide);
        double Side(float[] s) => Rms(Enumerable.Range(0, Rate).Select(f => (s[2 * f] - s[2 * f + 1]) * 0.5f).ToArray());
        double Mid(float[] s) => Rms(Enumerable.Range(0, Rate).Select(f => (s[2 * f] + s[2 * f + 1]) * 0.5f).ToArray());
        Assert.True(Side(wide) / Mid(wide) > 1.8 * Side(samples) / Mid(samples));
    }

    [Fact]
    public void Saturation_adds_harmonics_and_keeps_the_level()
    {
        var samples = Sine(200, 0.7, 1, channels: 1);
        var before = (float[])samples.Clone();
        new SaturationProcessor(Rate, 1, new SaturationSettings(12, 0, 100)).Process(samples);

        double Harmonic(float[] s, double hz)
        {
            double re = 0, im = 0;
            for (int i = Rate / 4; i < Rate; i++)
            {
                re += s[i] * Math.Cos(2 * Math.PI * hz * i / Rate);
                im += s[i] * Math.Sin(2 * Math.PI * hz * i / Rate);
            }

            return Math.Sqrt(re * re + im * im);
        }

        Assert.True(Harmonic(samples, 600) > 20 * Harmonic(before, 600) + 1, "keine dritte Harmonische");
        Assert.True(Harmonic(samples, 400) > 20 * Harmonic(before, 400) + 1, "keine zweite Harmonische (Asymmetrie)");
        Assert.All(samples, v => Assert.InRange(v, -1.2f, 1.2f));
    }

    [Fact]
    public void Neutral_settings_change_nothing()
    {
        var samples = Sine(440, 0.5, 0.2);
        var before = (float[])samples.Clone();

        new GateProcessor(Rate, 2, GateSettings.Off).Process(samples);
        new TremoloProcessor(Rate, 2, TremoloSettings.Off).Process(samples);
        new PhaserProcessor(Rate, 2, PhaserSettings.Off).Process(samples);
        new WidenerProcessor(2, WidenerSettings.Off).Process(samples);
        new SaturationProcessor(Rate, 2, SaturationSettings.Off).Process(samples);

        Assert.Equal(before, samples);
    }

    [Fact]
    public void Effects_keep_length_and_format()
    {
        var input = new AudioBuffer(Sine(440, 0.5, 0.5), Rate, 2);
        IAudioEffect[] effects =
        [
            new GateEffect(new GateSettings()), new TremoloEffect(new TremoloSettings()), new PhaserEffect(new PhaserSettings()),
            new WidenerEffect(new WidenerSettings(150)), new SaturationEffect(new SaturationSettings()),
        ];

        foreach (var effect in effects)
        {
            var output = effect.Apply(input);
            Assert.Equal(input.FrameCount, output.FrameCount);
            Assert.Equal(2, output.Channels);
        }
    }
}
