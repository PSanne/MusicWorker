using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Mixing;

namespace MusicWorker.Core.Tests;

public class StemsTests
{
    private const int Rate = 8000;

    private static AudioBuffer Tone(double hz, double seconds, float amplitude = 0.4f)
    {
        int frames = (int)(seconds * Rate);
        var samples = new float[frames * 2];
        for (int f = 0; f < frames; f++)
        {
            samples[2 * f] = samples[2 * f + 1] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * f / Rate));
        }

        return new AudioBuffer(samples, Rate, 2);
    }

    private static MixSnapshot Project()
    {
        var voice = new MixTrack([new MixClip(Tone(300, 1), Rate / 2)], VolumeDb: -3, Pan: -0.5, SendLevels: [0.3]);
        var music = new MixTrack([new MixClip(Tone(110, 2.5), 0)], VolumeDb: 0, Pan: 0.4, SendLevels: [0.1],
            Inserts: new TrackInserts(LowDb: 3, CompressionPercent: 40), Ducking: new DuckingSettings(0));
        var bus = new MixBus(BusEffect.Reverb, -6, new ReverbSettings([30], 60, 40));
        return new MixSnapshot(Rate, [voice, music], [], [bus], MasterDb: -2, MasterInserts: new TrackInserts(HighDb: 4), MasterLimiter: true);
    }

    [Fact]
    public void Stems_add_up_to_the_mix_without_master_processing()
    {
        var project = Project();
        long length = project.Length;
        var reference = MixEngine.Mixdown(project with { MasterDb = 0, MasterInserts = null, MasterLimiter = false }, 1, length);

        var stems = Enumerable.Range(0, 2).Select(i => MixEngine.Mixdown(Stems.ForTrack(project, i, raw: false), 1, length)).ToArray();

        Assert.All(stems, s => Assert.Equal(reference.FrameCount, s.FrameCount));
        double maxError = 0;
        for (int i = 0; i < reference.Samples.Length; i++)
        {
            maxError = Math.Max(maxError, Math.Abs(reference.Samples[i] - stems[0].Samples[i] - stems[1].Samples[i]));
        }

        Assert.True(maxError < 1e-4, $"Abweichung {maxError}");
    }

    [Fact]
    public void Raw_stem_is_just_the_clips()
    {
        var project = Project();

        var raw = MixEngine.Mixdown(Stems.ForTrack(project, 0, raw: true), 0, project.Length);

        Assert.Equal(project.Length, raw.FrameCount);
        Assert.Equal(0f, raw.GetSample(Rate / 4, 0)); // vor dem Clip still
        Assert.Equal(Tone(300, 1).GetSample(100, 0), raw.GetSample(Rate / 2 + 100, 0), 4);
        Assert.Equal(raw.GetSample(Rate / 2 + 100, 0), raw.GetSample(Rate / 2 + 100, 1), 4); // kein Panorama
    }

    [Fact]
    public void Ducked_stem_keeps_the_ducking()
    {
        var project = Project();
        var duckedMix = MixEngine.Mixdown(Stems.ForTrack(project, 1, raw: false), 0, project.Length);
        var unducked = MixEngine.Mixdown(Stems.ForTrack(project with { Tracks = [project.Tracks[0] with { Mute = true }, project.Tracks[1]] }, 1, raw: false), 0, project.Length);

        // Während die Stimme spielt (ab 0,5 s), ist die Musik in der Einzelspur leiser als ohne Stimme.
        double Rms(AudioBuffer b) => Math.Sqrt(Enumerable.Range(Rate, Rate / 4).Average(f => (double)b.GetSample(f, 1) * b.GetSample(f, 1)));
        Assert.True(Rms(duckedMix) < Rms(unducked) * 0.5);
    }
}
