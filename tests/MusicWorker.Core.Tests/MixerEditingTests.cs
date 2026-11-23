using MusicWorker.Core.Audio;
using MusicWorker.Core.Editing;
using MusicWorker.Core.Mixing;

namespace MusicWorker.Core.Tests;

public class MixerEditingTests
{
    private const int Rate = 1000;

    private static AudioBuffer Constant(float value, int frames, int channels = 2) =>
        new(Enumerable.Repeat(value, frames * channels).ToArray(), Rate, channels);

    [Fact]
    public void Overlapping_clips_get_a_crossfade()
    {
        var a = new MixClip(Constant(1f, 100), 0);
        var b = new MixClip(Constant(1f, 100), 80); // 20 Frames Überlappung

        var faded = ClipTools.WithCrossfades([b, a]);

        Assert.Equal(20, faded[0].FadeOutFrames);
        Assert.Equal(20, faded[1].FadeInFrames);
        Assert.Equal(0, faded[0].FadeInFrames);
    }

    [Fact]
    public void Crossfade_hands_over_from_the_first_to_the_second_clip()
    {
        var first = new MixClip(Constant(1f, 100), 0);
        var second = new MixClip(Constant(-1f, 100), 80);
        var track = new MixTrack(ClipTools.WithCrossfades([first, second]), Pan: -1);

        var mix = MixEngine.Mixdown(new MixSnapshot(Rate, [track], [], []));

        Assert.Equal(1f, mix.GetSample(79, 0), 1e-4f);    // vor der Überblendung: nur der erste
        Assert.InRange(mix.GetSample(81, 0), 0.8f, 1f);     // am Anfang: fast nur der erste
        Assert.InRange(mix.GetSample(98, 0), -1f, -0.8f);   // am Ende: fast nur der zweite
        Assert.Equal(-1f, mix.GetSample(120, 0), 1e-4f);   // danach: nur der zweite
    }

    [Fact]
    public void Equal_power_fade_keeps_constant_power_for_different_material()
    {
        var clip = new MixClip(Constant(1f, 100), 0, FadeInFrames: 40, FadeOutFrames: 40);

        for (int f = 0; f < 40; f++)
        {
            float fadeIn = ClipTools.FadeGain(clip, f);
            float fadeOut = ClipTools.FadeGain(clip with { FadeInFrames = 0 }, 100 - 40 + f);
            Assert.Equal(1.0, fadeIn * fadeIn + fadeOut * fadeOut, 0.02); // sin² + cos² = 1
        }
    }

    [Fact]
    public void Merge_combines_clips_with_gaps_as_silence()
    {
        var a = new MixClip(Constant(0.5f, 10, 1), 100);
        var b = new MixClip(Constant(0.25f, 10, 2), 130);

        var (audio, start) = ClipTools.Merge([a, b]);

        Assert.Equal(100, start);
        Assert.Equal(40, audio.FrameCount);
        Assert.Equal(2, audio.Channels);                 // Mono + Stereo → Stereo
        Assert.Equal(0.5f, audio.GetSample(5, 1), 1e-6f);  // Mono-Clip auf beiden Kanälen
        Assert.Equal(0f, audio.GetSample(20, 0));           // Lücke = Stille
        Assert.Equal(0.25f, audio.GetSample(35, 0), 1e-6f);
    }

    [Fact]
    public void FindContent_skips_silence_at_start_and_end()
    {
        var samples = new float[1000];
        for (int i = 300; i < 600; i++)
        {
            samples[i] = 0.5f;
        }

        var range = ClipTools.FindContent(new AudioBuffer(samples, Rate, 1), -50, paddingSeconds: 0.01);

        Assert.Equal(new SelectionRange(290, 610), range);
    }

    [Fact]
    public void FindContent_of_silence_is_empty()
    {
        Assert.True(ClipTools.FindContent(AudioBuffer.CreateSilence(Rate, 2, 500)).IsEmpty);
    }

    [Fact]
    public void Track_eq_inserts_boost_the_lows()
    {
        const int rate = 44100;
        var bass = Signals.Sine(60, 1, 0.1f, rate, channels: 2);
        var plain = new MixTrack([new MixClip(bass, 0)], Pan: -1);
        var boosted = plain with { Inserts = new TrackInserts(LowDb: 9) };

        double Level(MixTrack t) => Signals.Rms(MixEngine.Mixdown(new MixSnapshot(rate, [t], [], [])).GetChannel(0), 0.3);

        Assert.Equal(9, 20 * Math.Log10(Level(boosted) / Level(plain)), 1.0);
    }

    [Fact]
    public void Track_high_pass_insert_removes_rumble()
    {
        const int rate = 44100;
        var rumble = Signals.Sine(30, 1, 0.3f, rate, channels: 2);
        var track = new MixTrack([new MixClip(rumble, 0)], Inserts: new TrackInserts(HighPassHz: 300));

        var mix = MixEngine.Mixdown(new MixSnapshot(rate, [track], [], []));

        Assert.True(Signals.Rms(mix.GetChannel(0), 0.3) < 0.01);
    }

    [Fact]
    public void Track_compression_reduces_the_range_between_loud_and_quiet()
    {
        const int rate = 44100;
        var loud = Signals.Sine(440, 1, 0.8f, rate, channels: 2);
        var quiet = Signals.Sine(440, 1, 0.02f, rate, channels: 2);
        var inserts = new TrackInserts(CompressionPercent: 100);

        double Level(AudioBuffer a, TrackInserts? i) =>
            Signals.Rms(MixEngine.Mixdown(new MixSnapshot(rate, [new MixTrack([new MixClip(a, 0)], Inserts: i)], [], [])).GetChannel(0), 0.5);

        double rangeBefore = 20 * Math.Log10(Level(loud, null) / Level(quiet, null));
        double rangeAfter = 20 * Math.Log10(Level(loud, inserts) / Level(quiet, inserts));
        Assert.True(rangeAfter < rangeBefore - 10, $"vorher {rangeBefore:0.0} dB, nachher {rangeAfter:0.0} dB");
    }

    [Fact]
    public void Master_limiter_prevents_clipping()
    {
        var tracks = Enumerable.Range(0, 4).Select(_ => new MixTrack([new MixClip(Constant(0.9f, 2000), 0)])).ToArray();

        var withoutLimiter = MixEngine.Mixdown(new MixSnapshot(Rate, tracks, [], []));
        var withLimiter = MixEngine.Mixdown(new MixSnapshot(Rate, tracks, [], [], MasterLimiter: true));

        Assert.True(Signals.Peak(withoutLimiter) > 1.5f);
        Assert.True(Signals.Peak(withLimiter) <= 0.995f);
    }

    [Fact]
    public void Master_inserts_shape_the_sum()
    {
        const int rate = 44100;
        var highs = Signals.Sine(12000, 1, 0.1f, rate, channels: 2);
        var tracks = new[] { new MixTrack([new MixClip(highs, 0)]) };

        double plain = Signals.Rms(MixEngine.Mixdown(new MixSnapshot(rate, tracks, [], [])).GetChannel(0), 0.3);
        double cut = Signals.Rms(MixEngine.Mixdown(new MixSnapshot(rate, tracks, [], [], MasterInserts: new TrackInserts(HighDb: -12))).GetChannel(0), 0.3);

        Assert.Equal(-12, 20 * Math.Log10(cut / plain), 1.5);
    }
}
