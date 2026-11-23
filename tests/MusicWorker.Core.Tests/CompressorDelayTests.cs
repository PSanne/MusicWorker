using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Tests;

public class CompressorTests
{
    private const int Rate = 44100;

    private static double RmsDb(AudioBuffer b, int channel = 0) =>
        20 * Math.Log10(Signals.Rms(b.GetChannel(channel), skipSeconds: 0.3));

    [Fact]
    public void Zero_mix_leaves_the_signal_unchanged()
    {
        var input = Signals.Sine(440, 0.5, 0.8f, Rate, channels: 2);

        var output = new CompressorEffect(new CompressorSettings([0])).Apply(input);

        Assert.Equal(input.Samples.ToArray(), output.Samples.ToArray());
    }

    [Fact]
    public void Loud_signal_is_reduced_by_the_ratio()
    {
        // Spitze 0,5 = -6 dBFS, Schwelle -20 dB → 14 dB darüber; 4:1 → 10,5 dB Reduktion.
        var input = Signals.Sine(1000, 1.5, 0.5f, Rate, channels: 1);
        var settings = new CompressorSettings([100], thresholdDb: -20, ratio: 4, attackMs: 5, releaseMs: 200);

        var output = new CompressorEffect(settings, preventClipping: false).Apply(input);

        Assert.Equal(-10.5, RmsDb(output) - RmsDb(input), 1.5);
    }

    [Fact]
    public void Quiet_signal_below_threshold_is_untouched()
    {
        var input = Signals.Sine(1000, 1, 0.01f, Rate, channels: 1); // -40 dBFS
        var settings = new CompressorSettings([100], thresholdDb: -20, ratio: 8);

        var output = new CompressorEffect(settings, preventClipping: false).Apply(input);

        Assert.Equal(0, RmsDb(output) - RmsDb(input), 0.05);
    }

    [Fact]
    public void Makeup_gain_raises_the_level()
    {
        var input = Signals.Sine(1000, 1, 0.01f, Rate, channels: 1);
        var settings = new CompressorSettings([100], thresholdDb: -20, makeupDb: 6);

        var output = new CompressorEffect(settings, preventClipping: false).Apply(input);

        Assert.Equal(6, RmsDb(output) - RmsDb(input), 0.1);
    }

    [Fact]
    public void Linked_mode_reduces_both_channels_equally()
    {
        // Links laut, rechts leise
        var loud = Signals.Sine(1000, 1, 0.5f, Rate, channels: 1);
        var quiet = Signals.Sine(1000, 1, 0.01f, Rate, channels: 1);
        var samples = new float[loud.FrameCount * 2];
        for (int f = 0; f < loud.FrameCount; f++)
        {
            samples[2 * f] = loud.GetSample(f, 0);
            samples[2 * f + 1] = quiet.GetSample(f, 0);
        }

        var stereo = new AudioBuffer(samples, Rate, 2);

        var linked = new CompressorEffect(new CompressorSettings([100], -20, 4, linked: true), false).Apply(stereo);
        var separate = new CompressorEffect(new CompressorSettings([100], -20, 4, linked: false), false).Apply(stereo);

        Assert.True(RmsDb(linked, 1) - RmsDb(stereo, 1) < -8, "gekoppelt: rechts wird mit links zusammen geregelt");
        Assert.Equal(0, RmsDb(separate, 1) - RmsDb(stereo, 1), 0.05);
    }

    [Fact]
    public void Mix_per_channel_controls_the_amount()
    {
        var input = Signals.Sine(1000, 1, 0.5f, Rate, channels: 2);

        var output = new CompressorEffect(new CompressorSettings([100, 0], -20, 4, linked: false), false).Apply(input);

        Assert.True(RmsDb(output, 0) - RmsDb(input, 0) < -8);
        Assert.Equal(input.GetChannel(1).Samples.ToArray(), output.GetChannel(1).Samples.ToArray());
    }

    [Fact]
    public void Reports_gain_reduction_for_a_meter()
    {
        var processor = new CompressorProcessor(Rate, 1, new CompressorSettings([100], -20, 4));
        var block = Signals.Sine(1000, 0.5, 0.5f, Rate, channels: 1).Samples.ToArray();

        processor.Process(block);

        Assert.InRange(processor.GetGainReductionDb(0), 8, 12);
    }
}

public class DelayTests
{
    private const int Rate = 44100;

    private static AudioBuffer Impulse(int channels, double seconds = 2, int impulseChannel = -1)
    {
        var samples = new float[(int)(seconds * Rate) * channels];
        for (int c = 0; c < channels; c++)
        {
            if (impulseChannel < 0 || c == impulseChannel)
            {
                samples[c] = 1f;
            }
        }

        return new AudioBuffer(samples, Rate, channels);
    }

    [Fact]
    public void Zero_mix_leaves_the_signal_unchanged()
    {
        var input = Signals.Sine(440, 0.5, channels: 2);

        var output = new DelayEffect(new DelaySettings([300], [0])).Apply(input);

        Assert.Equal(input.Samples.ToArray(), output.Samples.ToArray());
    }

    [Fact]
    public void Echoes_arrive_at_the_right_time_with_the_right_level()
    {
        // 100 ms = 4410 Samples, Stärke 50 %, Rückkopplung 40 %, keine Dämpfung
        var settings = new DelaySettings([100], [50], feedbackPercent: 40, dampingPercent: 0);

        var output = new DelayEffect(settings, preventClipping: false).Apply(Impulse(1));

        Assert.Equal(1f, output.GetSample(0, 0), 1e-6f);          // Original
        Assert.Equal(0.5f, output.GetSample(4410, 0), 1e-5f);     // 1. Echo
        Assert.Equal(0.2f, output.GetSample(8820, 0), 1e-5f);     // 2. Echo: 0,5 · 0,4
        Assert.Equal(0.08f, output.GetSample(13230, 0), 1e-5f);   // 3. Echo: 0,5 · 0,4²
        Assert.Equal(0f, output.GetSample(2000, 0));              // dazwischen Stille
    }

    [Fact]
    public void Each_channel_has_its_own_time()
    {
        var settings = new DelaySettings([100, 250], [50], feedbackPercent: 0, dampingPercent: 0);

        var output = new DelayEffect(settings, preventClipping: false).Apply(Impulse(2));

        Assert.Equal(0.5f, output.GetSample(4410, 0), 1e-5f);     // links nach 100 ms
        Assert.Equal(0f, output.GetSample(4410, 1), 1e-6f);
        Assert.Equal(0.5f, output.GetSample(11025, 1), 1e-5f);    // rechts nach 250 ms
    }

    [Fact]
    public void Ping_pong_bounces_between_left_and_right()
    {
        // Klick nur links; Ping-Pong: 1. Echo links, die Rückkopplung landet rechts.
        var settings = new DelaySettings([100, 100], [50], feedbackPercent: 50, dampingPercent: 0, pingPong: true);

        var output = new DelayEffect(settings, preventClipping: false).Apply(Impulse(2, impulseChannel: 0));

        Assert.Equal(0.5f, output.GetSample(4410, 0), 1e-5f);     // links
        Assert.Equal(0f, output.GetSample(4410, 1), 1e-6f);
        Assert.Equal(0.25f, output.GetSample(8820, 1), 1e-5f);    // dann rechts (0,5 · 0,5)
        Assert.Equal(0f, output.GetSample(8820, 0), 1e-6f);
        Assert.Equal(0.125f, output.GetSample(13230, 0), 1e-5f);  // dann wieder links
    }

    [Fact]
    public void Damping_makes_repeats_duller()
    {
        // Hoher Ton: bei starker Dämpfung sind spätere Wiederholungen deutlich leiser.
        var tone = Signals.Sine(8000, 0.05, 0.5f, Rate, channels: 1);
        var input = tone.Insert(tone.FrameCount, AudioBuffer.CreateSilence(Rate, 1, Rate * 2));
        var bright = new DelayEffect(new DelaySettings([200], [50], 60, dampingPercent: 0), 0, false).Apply(input);
        var dull = new DelayEffect(new DelaySettings([200], [50], 60, dampingPercent: 100), 0, false).Apply(input);

        double Energy(AudioBuffer b) => Enumerable.Range((int)(0.75 * Rate), Rate / 4)
            .Sum(f => (double)b.GetSample(f, 0) * b.GetSample(f, 0));

        Assert.True(Energy(bright) > 4 * Energy(dull));
    }

    [Fact]
    public void Tail_extends_the_length_and_stays_stable_at_maximum_feedback()
    {
        var settings = new DelaySettings([500], [100], feedbackPercent: 90, dampingPercent: 0);

        var output = new DelayEffect(settings, settings.TailSeconds, preventClipping: false).Apply(Impulse(1, 1));

        Assert.Equal(Rate + (int)Math.Round(settings.TailSeconds * Rate), output.FrameCount);
        Assert.All(output.Samples.ToArray(), s => Assert.True(float.IsFinite(s) && Math.Abs(s) <= 1.0001f));
    }

    [Fact]
    public void Live_time_change_glides_instead_of_jumping()
    {
        var processor = new DelayProcessor(Rate, 1, new DelaySettings([100], [50], 0, 0));
        processor.Update(new DelaySettings([400], [50], 0, 0));

        // Nach dem Umstellen gleitet die Verzögerung – ein Klick erscheint weder bei 100 ms noch sofort bei 400 ms,
        // sondern irgendwo dazwischen, und das Ergebnis bleibt endlich.
        var block = Impulse(1, 1).Samples.ToArray();
        processor.Process(block);

        int peak = Array.IndexOf(block, block.Skip(1).Max());
        Assert.InRange(peak, 4410, 17640);
    }

    [Fact]
    public void Name_describes_the_settings()
    {
        Assert.Equal("Echo (350 ms)", new DelayEffect(new DelaySettings([350], [30])).Name);
        Assert.Equal("Echo (L 300 ms, R 450 ms, Ping-Pong)",
            new DelayEffect(new DelaySettings([300, 450], [30], pingPong: true)).Name);
    }
}
