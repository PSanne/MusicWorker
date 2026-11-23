using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Mixing;

namespace MusicWorker.Core.Tests;

public class AutomationTests
{
    private const int Rate = 8000;

    private static AudioBuffer Constant(float value, int frames, int channels = 2) =>
        new(Enumerable.Repeat(value, frames * channels).ToArray(), Rate, channels);

    private static AutomationLane Lane(AutomationTarget target, params (long Frame, double Value)[] points) =>
        new(target, points.Select(p => new AutomationPoint(p.Frame, p.Value)).ToArray());

    private static double Rms(AudioBuffer mix, int channel, int from, int to)
    {
        double sum = 0;
        for (int i = from; i < to; i++)
        {
            double v = mix.GetSample(i, channel);
            sum += v * v;
        }

        return Math.Sqrt(sum / (to - from));
    }

    [Fact]
    public void Lane_interpolates_linearly_and_holds_the_end_values()
    {
        var lane = Lane(AutomationTarget.Volume, (100, 0), (200, -20));

        Assert.Equal(0, lane.ValueAt(0));
        Assert.Equal(-10, lane.ValueAt(150), 6);
        Assert.Equal(-20, lane.ValueAt(5000));
        Assert.Equal(0, new AutomationLane(AutomationTarget.Volume, []).ValueAt(10)); // leer = Standardwert
    }

    [Fact]
    public void High_pass_lane_interpolates_logarithmically()
    {
        var lane = Lane(AutomationTarget.HighPass, (0, 20), (100, 2000));

        Assert.Equal(200, lane.ValueAt(50), 3); // geometrische Mitte von 20 und 2000
    }

    [Fact]
    public void Points_are_inserted_in_order_and_moves_stay_between_neighbours()
    {
        var lane = Lane(AutomationTarget.Pan, (0, 0), (100, 1));

        var (added, index) = lane.WithPoint(50, -0.5);
        Assert.Equal(1, index);
        Assert.Equal([0L, 50L, 100L], added.Points.Select(p => p.Frame));

        var moved = added.WithMovedPoint(1, 500, 3); // zu weit rechts und zu groß
        Assert.Equal(100, moved.Points[1].Frame);
        Assert.Equal(1, moved.Points[1].Value);

        Assert.Equal(2, moved.WithoutPoint(0).Points.Count);
    }

    [Fact]
    public void Volume_envelope_fades_the_track_relative_to_the_fader()
    {
        var track = new MixTrack(
            [new MixClip(Constant(0.5f, 4000), 0)],
            VolumeDb: -6,
            Automation: [Lane(AutomationTarget.Volume, (1000, 0), (2000, -60))]);

        var mix = MixEngine.Mixdown(new MixSnapshot(Rate, [track], [], []));

        float fader = (float)Decibels.ToGain(-6);
        Assert.Equal(0.5f * fader, mix.GetSample(500, 0), 3);
        Assert.InRange(mix.GetSample(1500, 0), 0.5f * fader * 0.02f, 0.5f * fader * 0.05f); // ≈ -30 dB
        Assert.Equal(0f, mix.GetSample(3000, 0), 4);
    }

    [Fact]
    public void Pan_envelope_moves_a_mono_track_from_left_to_right()
    {
        var track = new MixTrack(
            [new MixClip(Constant(0.5f, 4000, channels: 1), 0)],
            Pan: 0,
            Automation: [Lane(AutomationTarget.Pan, (0, -1), (4000, 1))]);

        var mix = MixEngine.Mixdown(new MixSnapshot(Rate, [track], [], []));

        Assert.True(mix.GetSample(100, 0) > 10 * Math.Abs(mix.GetSample(100, 1)));
        Assert.True(mix.GetSample(3900, 1) > 10 * Math.Abs(mix.GetSample(3900, 0)));
    }

    [Fact]
    public void Send_envelope_feeds_the_echo_bus_without_a_send_knob()
    {
        var bus = new MixBus(BusEffect.Delay, 0, Delay: new DelaySettings([100, 100], [100], 0, 0, false));
        var dry = new MixTrack([new MixClip(Constant(0.5f, 400), 0)], SendLevels: [0]);
        var automated = dry with { Automation = [Lane(AutomationTarget.SendEcho, (0, 1))] };

        var without = MixEngine.Mixdown(new MixSnapshot(Rate, [dry], [], [bus]), tailSeconds: 0.2);
        var with = MixEngine.Mixdown(new MixSnapshot(Rate, [automated], [], [bus]), tailSeconds: 0.2);

        Assert.Equal(0f, without.GetSample(1000, 0), 5); // 100 ms Echo endet bei Frame 1200
        Assert.True(Math.Abs(with.GetSample(1000, 0)) > 0.1f);
    }

    [Fact]
    public void High_pass_envelope_removes_bass()
    {
        var bass = Signals.Sine(60, 1, 0.5f, sampleRate: Rate);
        var plain = new MixTrack([new MixClip(bass, 0)]);
        var filtered = plain with { Automation = [Lane(AutomationTarget.HighPass, (0, 1000))] };

        var a = MixEngine.Mixdown(new MixSnapshot(Rate, [plain], [], []));
        var b = MixEngine.Mixdown(new MixSnapshot(Rate, [filtered], [], []));

        Assert.True(Rms(b, 0, 2000, 8000) < Rms(a, 0, 2000, 8000) * 0.05);
    }

    [Fact]
    public void Ducking_lowers_the_music_while_the_voice_plays_and_recovers_afterwards()
    {
        // Stimme links (1,0 … 2,0 s), Musik rechts – so lässt sich die Musik allein messen.
        var voice = new MixTrack([new MixClip(Signals.Sine(300, 1, 0.5f, sampleRate: Rate, channels: 1), Rate)], Pan: -1);
        var music = new MixTrack(
            [new MixClip(Signals.Sine(200, 4, 0.5f, sampleRate: Rate, channels: 1), 0)],
            Pan: 1,
            Ducking: new DuckingSettings(SourceTrack: 0, AmountDb: 12, AttackMs: 20, ReleaseMs: 200));

        var mix = MixEngine.Mixdown(new MixSnapshot(Rate, [voice, music], [], []));

        double before = Rms(mix, 1, 2000, 7000);
        double during = Rms(mix, 1, 10000, 15000);
        double after = Rms(mix, 1, 26000, 31000);
        Assert.InRange(20 * Math.Log10(during / before), -13, -10.5);
        Assert.InRange(20 * Math.Log10(after / before), -0.5, 0.5);
    }

    [Fact]
    public void Muted_source_does_not_duck()
    {
        var voice = new MixTrack([new MixClip(Constant(0.5f, 8000, channels: 1), 0)], Pan: -1, Mute: true);
        var music = new MixTrack([new MixClip(Constant(0.5f, 8000, channels: 1), 0)], Pan: 1, Ducking: new DuckingSettings(0));

        var mix = MixEngine.Mixdown(new MixSnapshot(Rate, [voice, music], [], []));

        Assert.Equal(0.5f, mix.GetSample(4000, 1), 3);
    }
}
