using MusicWorker.Core.Notes;
using MusicWorker.Core.Recording;
using MusicWorker.Core.Synthesis;

namespace MusicWorker.Core.Tests;

public class TempoMapTests
{
    private const int Rate = 48000;
    private const int Q = MusicalTime.TicksPerQuarter;

    [Fact]
    public void Constant_tempo_matches_simple_conversion()
    {
        var map = TempoMap.Constant(120);

        Assert.True(map.IsConstant);
        Assert.Equal(MusicalTime.TicksToFrames(Q * 7, 120, Rate), map.TicksToFrames(Q * 7, Rate));
        Assert.Equal(Q * 7, map.FramesToTicks(map.TicksToFrames(Q * 7, Rate), Rate));
    }

    [Fact]
    public void Tempo_change_slows_down_later_beats()
    {
        // 4 Schläge in 120 BPM (2 s), danach 60 BPM (1 s pro Schlag)
        var map = new TempoMap(120, [new TempoChange(Q * 4, 60)]);

        Assert.Equal(2.0, map.TicksToSeconds(Q * 4), 6);
        Assert.Equal(4.0, map.TicksToSeconds(Q * 6), 6);
        Assert.Equal(Q * 6, map.SecondsToTicks(4.0), 6);
        Assert.Equal(1.0, map.TicksToSeconds(Q * 2), 6);
        Assert.Equal(60, map.BpmAt(Q * 5));
        Assert.Equal(120, map.BpmAt(Q * 3));
    }

    [Fact]
    public void Negative_times_use_the_starting_tempo()
    {
        var map = new TempoMap(120, [new TempoChange(Q * 4, 60)]);

        Assert.Equal(-0.5, map.TicksToSeconds(-Q), 6);
        Assert.Equal(-Q, map.SecondsToTicks(-0.5), 6);
    }

    [Fact]
    public void Changes_are_sorted_and_redundant_ones_dropped()
    {
        var map = new TempoMap(100, [new TempoChange(Q * 8, 90), new TempoChange(Q * 4, 100), new TempoChange(0, 110)]);

        Assert.Equal(110, map.BaseBpm);
        Assert.Equal([new TempoChange(0, 110), new TempoChange(Q * 4, 100), new TempoChange(Q * 8, 90)], map.Changes);
        Assert.Equal(2, map.ChangesAfterStart.Count());
    }

    [Fact]
    public void Snapping_follows_the_tempo_change()
    {
        var map = new TempoMap(120, [new TempoChange(Q * 4, 60)]);

        // Schlag 5 liegt bei 3 s, Schlag 6 bei 4 s
        Assert.Equal(3 * Rate, map.NearestBeatFrame((long)(3.3 * Rate), Rate));
        Assert.Equal(4 * Rate, map.NearestBeatFrame((long)(3.7 * Rate), Rate));
        Assert.Equal(Rate / 2, map.NearestBeatFrame((long)(0.6 * Rate), Rate));
    }

    [Fact]
    public void Clip_relative_conversion_uses_the_clip_position()
    {
        var map = new TempoMap(120, [new TempoChange(Q * 4, 60)]);
        long clipStart = 1 * Rate; // Schlag 2 (120 BPM)

        // 4 Viertel ab Schlag 2: 2 in 120 BPM (1 s) + 2 in 60 BPM (2 s)
        Assert.Equal(3 * Rate, map.ClipTicksToFrames(clipStart, Q * 4, Rate));
        Assert.Equal(Q * 4, map.ClipFramesToTicks(clipStart, 3 * Rate, Rate));
    }

    [Fact]
    public void Notes_follow_the_tempo_map_when_rendered()
    {
        var map = new TempoMap(120, [new TempoChange(Q * 2, 60)]);
        Note[] notes = [new(69, Q * 3, Q / 4, 110)]; // Schlag 4: 1 s (2 Schläge à 0,5 s) + 1 s = 2 s
        var audio = NoteRenderer.Render(notes, new InstrumentSpec(), map, 0, Rate, maxTailSeconds: 0.5);

        long onset = FirstLoud(audio.Samples, 0.01f) / 2;
        Assert.InRange(onset, (long)(1.99 * Rate), (long)(2.02 * Rate));
    }

    [Fact]
    public void Metronome_clicks_follow_the_tempo_map()
    {
        var map = new TempoMap(120, [new TempoChange(Q * 2, 60)]);
        var metronome = new Metronome(Rate);
        var buffer = new float[Rate * 4 * 2];
        metronome.Render(buffer, 0, map, 4);

        // Klicks bei 0; 0,5; 1,0; 2,0; 3,0 s – nicht bei 1,5 s
        Assert.True(Peak(buffer, 1.0) > 0.05);
        Assert.True(Peak(buffer, 2.0) > 0.05);
        Assert.True(Peak(buffer, 1.5) < 1e-6);
        Assert.True(Peak(buffer, 2.5) < 1e-6);
    }

    [Fact]
    public void Midi_round_trip_keeps_tempo_changes()
    {
        var song = new MidiSong(100, 4, [new MidiTrackData("Klavier", 0, 0, [new Note(60, 0, Q)])], [new TempoChange(Q * 8, 80), new TempoChange(Q * 16, 132)]);
        var read = MidiFile.Read(MidiFile.Write(song));

        Assert.Equal(100, read.Bpm, 1);
        Assert.NotNull(read.TempoChanges);
        Assert.Equal(2, read.TempoChanges!.Count);
        Assert.Equal(Q * 8, read.TempoChanges[0].Tick);
        Assert.Equal(80, read.TempoChanges[0].Bpm, 1);
        Assert.Equal(132, read.TempoChanges[1].Bpm, 1);
    }

    private static int FirstLoud(ReadOnlySpan<float> samples, float threshold)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            if (Math.Abs(samples[i]) > threshold)
            {
                return i;
            }
        }

        return -1;
    }

    private static double Peak(float[] stereo, double seconds)
    {
        int from = (int)(seconds * Rate) * 2;
        double peak = 0;
        for (int i = from; i < Math.Min(stereo.Length, from + (Rate / 50 * 2)); i++)
        {
            peak = Math.Max(peak, Math.Abs(stereo[i]));
        }

        return peak;
    }
}
