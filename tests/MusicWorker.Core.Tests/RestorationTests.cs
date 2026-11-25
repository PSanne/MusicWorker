using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Tests;

public class RestorationTests
{
    private const int Rate = 44100;

    private static float[] Music(int frames, int seed = 1)
    {
        var random = new Random(seed);
        var samples = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            double t = f / (double)Rate;
            samples[f] = (float)(0.3 * Math.Sin(2 * Math.PI * 220 * t) + 0.15 * Math.Sin(2 * Math.PI * 330 * t + 1) + 0.1 * Math.Sin(2 * Math.PI * 880 * t) + 0.002 * (random.NextDouble() - 0.5));
        }

        return samples;
    }

    [Fact]
    public void Clicks_are_found_and_repaired()
    {
        var clean = Music(Rate * 2);
        var damaged = (float[])clean.Clone();
        int[] clicks = [10000, 23456, 40000, 61234, 77777];
        foreach (int at in clicks)
        {
            damaged[at] += 0.6f;
            damaged[at + 1] -= 0.4f;
            damaged[at + 2] += 0.2f;
        }

        var effect = new DeclickEffect(sensitivity: 5);
        var repaired = effect.Apply(new AudioBuffer(damaged, Rate, 1));

        Assert.InRange(effect.RepairedClicks, clicks.Length, clicks.Length + 2);
        foreach (int at in clicks)
        {
            for (int i = at - 2; i < at + 5; i++)
            {
                Assert.True(Math.Abs(repaired.Samples[i] - clean[i]) < 0.05, $"Stelle {i}: {repaired.Samples[i]:0.000} statt {clean[i]:0.000}");
            }
        }
    }

    [Fact]
    public void Clean_music_stays_untouched()
    {
        var clean = Music(Rate);
        var effect = new DeclickEffect(sensitivity: 5);

        var output = effect.Apply(new AudioBuffer(clean, Rate, 1));

        Assert.Equal(0, effect.RepairedClicks);
        Assert.Equal(clean, output.Samples.ToArray());
    }

    [Fact]
    public void Spectral_repair_removes_a_ringing_tone_but_keeps_the_rest()
    {
        // Leises Rauschen + Musik bei 220 Hz; dazwischen 0,3 s „Telefon“ bei 3 kHz.
        var samples = Music(Rate * 2);
        int from = Rate * 3 / 4, to = from + Rate * 3 / 10;
        for (int f = from; f < to; f++)
        {
            samples[f] += (float)(0.4 * Math.Sin(2 * Math.PI * 3000 * f / Rate));
        }

        var input = new AudioBuffer(samples, Rate, 1);
        var output = new SpectralRepairEffect(from, to, 2500, 3500).Apply(input);

        double Band(AudioBuffer b, double hz, int start, int length)
        {
            double re = 0, im = 0;
            for (int i = start; i < start + length; i++)
            {
                re += b.Samples[i] * Math.Cos(2 * Math.PI * hz * i / Rate);
                im += b.Samples[i] * Math.Sin(2 * Math.PI * hz * i / Rate);
            }

            return Math.Sqrt(re * re + im * im);
        }

        int mid = from + Rate / 20;
        double toneBefore = Band(input, 3000, mid, Rate / 10);
        double toneAfter = Band(output, 3000, mid, Rate / 10);
        Assert.True(20 * Math.Log10(toneAfter / toneBefore) < -20, $"Ton nur um {20 * Math.Log10(toneAfter / toneBefore):0.0} dB leiser");

        double musicBefore = Band(input, 220, mid, Rate / 10);
        double musicAfter = Band(output, 220, mid, Rate / 10);
        Assert.InRange(musicAfter / musicBefore, 0.97, 1.03);

        // Weit weg von der Stelle bleibt alles gleich.
        for (int i = 1000; i < 5000; i++)
        {
            Assert.True(Math.Abs(output.Samples[i] - input.Samples[i]) < 1e-3);
        }
    }

    [Fact]
    public void Attenuate_mode_lowers_by_the_chosen_amount()
    {
        var samples = Music(Rate * 2);
        var input = new AudioBuffer(samples, Rate, 1);

        var output = new SpectralRepairEffect(Rate / 2, Rate, 0, Rate / 2.0, SpectralRepairMode.Attenuate, 12).Apply(input);

        double Rms(AudioBuffer b, int start, int length) => Math.Sqrt(Enumerable.Range(start, length).Average(i => (double)b.Samples[i] * b.Samples[i]));
        double ratio = Rms(output, Rate * 6 / 10, Rate / 4) / Rms(input, Rate * 6 / 10, Rate / 4);
        Assert.InRange(20 * Math.Log10(ratio), -12.5, -11.5);
    }
}
