using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Tests;

public class ReverbTests
{
    private const int Rate = 44100;

    /// <summary>Ein einzelner „Klick“ am Anfang, danach Stille – damit hört (misst) man nur den Hall.</summary>
    private static AudioBuffer Impulse(double seconds = 3, int channels = 2)
    {
        var samples = new float[(int)(seconds * Rate) * channels];
        for (int c = 0; c < channels; c++)
        {
            samples[c] = 0.8f;
        }

        return new AudioBuffer(samples, Rate, channels);
    }

    /// <summary>Energie eines Kanals im Zeitfenster [from, to) Sekunden.</summary>
    private static double Energy(AudioBuffer buffer, int channel, double from, double to)
    {
        double sum = 0;
        for (int f = (int)(from * Rate); f < Math.Min(buffer.FrameCount, (int)(to * Rate)); f++)
        {
            double v = buffer.GetSample(f, channel);
            sum += v * v;
        }

        return sum;
    }

    [Fact]
    public void Zero_amount_leaves_the_signal_unchanged()
    {
        var input = Signals.Sine(440, 0.5, channels: 2);

        var output = new ReverbEffect(new ReverbSettings([0, 0])).Apply(input);

        Assert.Equal(input.Samples.ToArray(), output.Samples.ToArray());
    }

    [Fact]
    public void Reverb_adds_a_decaying_tail_after_an_impulse()
    {
        var output = new ReverbEffect(new ReverbSettings([50]), preventClipping: false).Apply(Impulse());

        double early = Energy(output, 0, 0.05, 0.5);
        double late = Energy(output, 0, 1.5, 2.0);
        Assert.True(early > 1e-4, $"früh: {early}");
        Assert.True(late < early, $"spät {late} ≥ früh {early}");
    }

    [Fact]
    public void Each_channel_gets_its_own_amount()
    {
        var output = new ReverbEffect(new ReverbSettings([60, 0]), preventClipping: false).Apply(Impulse());

        Assert.True(Energy(output, 0, 0.05, 1) > 1e-4);
        Assert.Equal(0, Energy(output, 1, 0.05, 1)); // rechts: kein Hall, also nach dem Klick Stille
    }

    [Fact]
    public void Larger_room_rings_longer()
    {
        var small = new ReverbEffect(new ReverbSettings([50], roomSizePercent: 10), preventClipping: false).Apply(Impulse());
        var large = new ReverbEffect(new ReverbSettings([50], roomSizePercent: 95), preventClipping: false).Apply(Impulse());

        Assert.True(Energy(large, 0, 1.0, 2.5) > 10 * Energy(small, 0, 1.0, 2.5));
    }

    [Fact]
    public void More_damping_makes_the_tail_darker()
    {
        // Hohe Töne klingen bei starker Dämpfung schneller ab.
        var input = Signals.Sine(8000, 0.3, 0.5f, Rate, channels: 1);
        var padded = input.Insert(input.FrameCount, AudioBuffer.CreateSilence(Rate, 1, Rate * 2));
        var bright = new ReverbEffect(new ReverbSettings([60], 70, dampingPercent: 0), preventClipping: false).Apply(padded);
        var dark = new ReverbEffect(new ReverbSettings([60], 70, dampingPercent: 100), preventClipping: false).Apply(padded);

        Assert.True(Energy(bright, 0, 0.8, 1.8) > 2 * Energy(dark, 0, 0.8, 1.8));
    }

    [Fact]
    public void Tail_extends_the_length()
    {
        var input = Signals.Sine(440, 1, channels: 2);

        var output = new ReverbEffect(new ReverbSettings([40]), tailSeconds: 2).Apply(input);

        Assert.Equal(input.FrameCount + 2 * Rate, output.FrameCount);
        Assert.True(Energy(output, 0, 1.0, 1.5) > 1e-4, "Hall klingt nach dem Ende aus");
    }

    [Fact]
    public void Stays_stable_with_maximum_room_and_no_damping()
    {
        var random = new Random(7);
        var noise = new float[Rate * 5];
        for (int i = 0; i < noise.Length; i++)
        {
            noise[i] = (float)(random.NextDouble() - 0.5);
        }

        var output = new ReverbEffect(new ReverbSettings([100], 100, 0), preventClipping: false)
            .Apply(new AudioBuffer(noise, Rate, 1));

        Assert.All(output.Samples.ToArray(), s => Assert.True(float.IsFinite(s) && Math.Abs(s) < 10));
    }

    [Fact]
    public void Prevent_clipping_limits_the_peak()
    {
        var loud = Signals.Sine(200, 1, 0.95f, Rate, channels: 2);

        var output = new ReverbEffect(new ReverbSettings([100], 90)).Apply(loud);

        Assert.True(Signals.Peak(output) <= 0.8913f + 1e-4f);
    }

    [Fact]
    public void Live_update_changes_the_amount()
    {
        var processor = new ReverbProcessor(Rate, 1, ReverbSettings.Off);
        var block = Impulse(1, channels: 1).Samples.ToArray();

        processor.Update(new ReverbSettings([80]));
        processor.Process(block);

        Assert.True(Energy(new AudioBuffer(block, Rate, 1), 0, 0.05, 0.5) > 1e-4);
    }

    [Fact]
    public void Right_only_processing_matches_the_right_channel_of_stereo()
    {
        var settings = new ReverbSettings([0, 50]);
        var stereo = new ReverbEffect(settings, preventClipping: false).Apply(Impulse());
        var rightOnly = new ReverbEffect(new ReverbSettings([50]), preventClipping: false, channelOffset: 1)
            .Apply(Impulse().GetChannel(1));

        for (int f = 0; f < 20000; f += 97)
        {
            Assert.Equal(stereo.GetSample(f, 1), rightOnly.GetSample(f, 0), 1e-6f);
        }
    }

    [Fact]
    public void Name_shows_amounts()
    {
        Assert.Equal("Hall 40 %", new ReverbEffect(new ReverbSettings([40, 40])).Name);
        Assert.Equal("Hall (L 40 %, R 10 %)", new ReverbEffect(new ReverbSettings([40, 10])).Name);
    }
}
