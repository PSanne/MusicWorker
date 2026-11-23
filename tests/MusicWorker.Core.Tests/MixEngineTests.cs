using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Mixing;

namespace MusicWorker.Core.Tests;

public class MixEngineTests
{
    private const int Rate = 1000; // kleine Abtastrate: Frames sind leicht nachzurechnen

    private static AudioBuffer Constant(float value, int frames, int channels = 1) =>
        new(Enumerable.Repeat(value, frames * channels).ToArray(), Rate, channels);

    private static MixSnapshot Project(params MixTrack[] tracks) => new(Rate, tracks, [], []);

    private static AudioBuffer Mix(MixSnapshot snapshot, double tail = 0) => MixEngine.Mixdown(snapshot, tail);

    [Fact]
    public void Mono_track_in_the_center_is_split_with_constant_power()
    {
        var mix = Mix(Project(new MixTrack([new MixClip(Constant(1f, 10), 0)])));

        Assert.Equal(10, mix.FrameCount);
        Assert.Equal(2, mix.Channels);
        Assert.Equal(0.7071f, mix.GetSample(5, 0), 1e-4f);
        Assert.Equal(0.7071f, mix.GetSample(5, 1), 1e-4f);
    }

    [Theory]
    [InlineData(-1, 1f, 0f)]
    [InlineData(1, 0f, 1f)]
    public void Hard_pan_sends_a_mono_track_to_one_side(double pan, float left, float right)
    {
        var mix = Mix(Project(new MixTrack([new MixClip(Constant(1f, 10), 0)], Pan: pan)));

        Assert.Equal(left, mix.GetSample(3, 0), 1e-5f);
        Assert.Equal(right, mix.GetSample(3, 1), 1e-5f);
    }

    [Fact]
    public void Stereo_track_pan_works_as_balance()
    {
        var mix = Mix(Project(new MixTrack([new MixClip(Constant(1f, 10, channels: 2), 0)], Pan: 0.5)));

        Assert.Equal(0.5f, mix.GetSample(3, 0), 1e-5f); // links leiser
        Assert.Equal(1f, mix.GetSample(3, 1), 1e-5f);   // rechts unverändert
    }

    [Fact]
    public void Fader_changes_the_level()
    {
        var mix = Mix(Project(new MixTrack([new MixClip(Constant(1f, 10, 2), 0)], VolumeDb: -6.0206)));

        Assert.Equal(0.5f, mix.GetSample(0, 0), 1e-4f);
    }

    [Fact]
    public void Clip_starts_at_its_position()
    {
        var mix = Mix(Project(new MixTrack([new MixClip(Constant(1f, 5, 2), StartFrame: 20)])));

        Assert.Equal(25, mix.FrameCount);
        Assert.Equal(0f, mix.GetSample(19, 0));
        Assert.Equal(1f, mix.GetSample(20, 0));
        Assert.Equal(1f, mix.GetSample(24, 0));
    }

    [Fact]
    public void Tracks_are_summed()
    {
        var mix = Mix(Project(
            new MixTrack([new MixClip(Constant(0.25f, 10, 2), 0)]),
            new MixTrack([new MixClip(Constant(0.5f, 10, 2), 0)])));

        Assert.Equal(0.75f, mix.GetSample(5, 0), 1e-5f);
    }

    [Fact]
    public void Mute_and_solo_decide_what_is_heard()
    {
        var a = new MixClip(Constant(0.25f, 10, 2), 0);
        var b = new MixClip(Constant(0.5f, 10, 2), 0);

        Assert.Equal(0.5f, Mix(Project(new MixTrack([a], Mute: true), new MixTrack([b]))).GetSample(1, 0), 1e-5f);
        Assert.Equal(0.25f, Mix(Project(new MixTrack([a], Solo: true), new MixTrack([b]))).GetSample(1, 0), 1e-5f);
    }

    [Fact]
    public void Group_fader_and_mute_affect_all_members()
    {
        var clip = new MixClip(Constant(1f, 10, 2), 0);
        var tracks = new[] { new MixTrack([clip], GroupIndex: 0), new MixTrack([clip], GroupIndex: 0) };

        var quieter = Mix(new MixSnapshot(Rate, tracks, [new MixGroup(VolumeDb: -6.0206)], []));
        var muted = Mix(new MixSnapshot(Rate, tracks, [new MixGroup(Mute: true)], []));

        Assert.Equal(1f, quieter.GetSample(1, 0), 1e-4f); // 2 × 0,5
        Assert.Equal(0f, muted.GetSample(1, 0));
    }

    [Fact]
    public void Master_fader_scales_everything()
    {
        var snapshot = new MixSnapshot(Rate, [new MixTrack([new MixClip(Constant(1f, 10, 2), 0)])], [], [], MasterDb: -6.0206);

        Assert.Equal(0.5f, Mix(snapshot).GetSample(1, 1), 1e-4f);
    }

    [Fact]
    public void Delay_bus_returns_only_the_echo()
    {
        // Ein Klick auf der Spur, voll in den Echo-Bus geschickt (100 ms, keine Rückkopplung).
        const int rate = 44100;
        var impulse = new float[rate];
        impulse[0] = 1f;
        var track = new MixTrack([new MixClip(new AudioBuffer(impulse, rate, 1), 0)], Pan: -1, SendLevels: [1.0]);
        var bus = new MixBus(BusEffect.Delay, Delay: new DelaySettings([100], [100], 0, 0));

        var mix = MixEngine.Mixdown(new MixSnapshot(rate, [track], [], [bus]));

        Assert.Equal(1f, mix.GetSample(0, 0), 1e-5f);    // Original (trocken) genau einmal
        Assert.Equal(1f, mix.GetSample(4410, 0), 1e-4f); // Echo nach 100 ms
        Assert.Equal(0f, mix.GetSample(8820, 0), 1e-6f); // keine zweite Wiederholung
    }

    [Fact]
    public void Reverb_send_adds_a_tail_without_doubling_the_dry_signal()
    {
        const int rate = 44100;
        var impulse = new float[rate / 10];
        impulse[0] = 1f;
        var clip = new MixClip(new AudioBuffer(impulse, rate, 1), 0);
        var bus = new MixBus(BusEffect.Reverb, Reverb: new ReverbSettings([100], 70, 30));

        var dry = MixEngine.Mixdown(new MixSnapshot(rate, [new MixTrack([clip], Pan: -1, SendLevels: [0.0])], [], [bus]), 1);
        var wet = MixEngine.Mixdown(new MixSnapshot(rate, [new MixTrack([clip], Pan: -1, SendLevels: [1.0])], [], [bus]), 1);

        Assert.Equal(dry.GetSample(0, 0), wet.GetSample(0, 0), 1e-6f); // Original unverändert
        double tail = Enumerable.Range(rate / 5, rate / 2).Sum(f => (double)wet.GetSample(f, 0) * wet.GetSample(f, 0));
        double none = Enumerable.Range(rate / 5, rate / 2).Sum(f => (double)dry.GetSample(f, 0) * dry.GetSample(f, 0));
        Assert.True(tail > 1e-5 && none == 0, $"Hallfahne {tail}, ohne Send {none}");
    }

    [Fact]
    public void Mixdown_includes_the_tail()
    {
        var mix = Mix(Project(new MixTrack([new MixClip(Constant(1f, 10), 0)])), tail: 0.5);

        Assert.Equal(10 + 500, mix.FrameCount);
    }

    [Fact]
    public void Seek_continues_at_the_new_position()
    {
        var ramp = new AudioBuffer(Enumerable.Range(0, 100).Select(i => (float)i).ToArray(), Rate, 1);
        var engine = new MixEngine(Project(new MixTrack([new MixClip(ramp, 0)], Pan: -1)));
        var block = new float[10];

        engine.Seek(42);
        engine.Render(block);

        Assert.Equal(42f, block[0], 1e-4f);
        Assert.Equal(47, engine.Position);
    }

    [Fact]
    public void Live_update_takes_effect_in_the_next_block()
    {
        var clip = new MixClip(Constant(1f, 100, 2), 0);
        var engine = new MixEngine(Project(new MixTrack([clip])));
        var block = new float[20];

        engine.Update(Project(new MixTrack([clip], Mute: true)));
        engine.Render(block);

        Assert.All(block, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Meters_report_track_and_master_peaks()
    {
        var engine = new MixEngine(Project(new MixTrack([new MixClip(Constant(0.5f, 100, 2), 0)], VolumeDb: 0)));

        engine.Render(new float[20]);

        Assert.Equal(0.5f, engine.GetTrackPeak(0), 1e-5f);
        Assert.Equal(0.5f, engine.GetMasterPeak(0), 1e-5f);
        Assert.Equal(0f, engine.GetTrackPeak(5));
    }

    [Fact]
    public void Project_length_is_the_end_of_the_last_clip()
    {
        var snapshot = Project(
            new MixTrack([new MixClip(Constant(1f, 10), 5)]),
            new MixTrack([new MixClip(Constant(1f, 30), 100)]));

        Assert.Equal(130, snapshot.Length);
    }

    [Fact]
    public void Reverb_wet_only_mode_has_no_dry_signal()
    {
        var samples = new float[2000];
        samples[0] = 1f;
        new ReverbProcessor(44100, 1, new ReverbSettings([100]), wetOnly: true).Process(samples);

        Assert.Equal(0f, samples[0]);
    }
}
