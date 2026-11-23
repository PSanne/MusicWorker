using MusicWorker.Core.Audio;
using MusicWorker.Core.Recording;

namespace MusicWorker.Core.Tests;

public class RecordingTests
{
    private const int Rate = 1000;

    private static AudioBuffer Constant(float value, int frames) =>
        new(Enumerable.Repeat(value, frames).ToArray(), Rate, 1);

    [Fact]
    public void Assign_splits_and_merges_segments()
    {
        var comp = TakeTools.ClearComp(100, 0);

        var assigned = TakeTools.Assign(comp, 20, 50, 1, 100);
        Assert.Equal([new CompSegment(0, 20, 0), new CompSegment(20, 50, 1), new CompSegment(50, 100, 0)], assigned);

        var back = TakeTools.Assign(assigned, 10, 60, 0, 100);
        Assert.Equal([new CompSegment(0, 100, 0)], back); // gleiche Nachbarn werden verbunden
    }

    [Fact]
    public void Normalize_fills_gaps()
    {
        var normalized = TakeTools.Normalize([new CompSegment(10, 20, 2)], 40);

        Assert.Equal([new CompSegment(0, 40, 2)], normalized);
    }

    [Fact]
    public void Punch_in_take_replaces_only_its_range()
    {
        var original = Constant(1f, 1000);
        var (takes, start, comp) = TakeTools.AddTake([], 0, [], original, 5000);
        Assert.Equal(5000, start);

        // Neuaufnahme von 5,2 s bis 5,5 s
        var punch = Constant(-1f, 300);
        (takes, start, comp) = TakeTools.AddTake(takes, start, comp, punch, 5200);

        Assert.Equal(2, takes.Count);
        Assert.Equal(5000, start);
        Assert.Equal([new CompSegment(0, 200, 0), new CompSegment(200, 500, 1), new CompSegment(500, 1000, 0)], comp);

        var audio = TakeTools.Compose(takes, comp);
        Assert.Equal(1000, audio.FrameCount);
        Assert.Equal(1f, audio.GetSample(100, 0), 4);
        Assert.Equal(-1f, audio.GetSample(350, 0), 4);
        Assert.Equal(1f, audio.GetSample(800, 0), 4);
    }

    [Fact]
    public void Earlier_take_extends_the_group_to_the_left()
    {
        var (takes, start, comp) = TakeTools.AddTake([], 0, [], Constant(1f, 100), 500);

        (takes, start, comp) = TakeTools.AddTake(takes, start, comp, Constant(0.5f, 100), 450);

        Assert.Equal(450, start);
        Assert.All(takes, t => Assert.Equal(150, t.FrameCount));
        Assert.Equal(0.5f, TakeTools.Compose(takes, comp).GetSample(10, 0), 4);
        Assert.Equal(1f, TakeTools.Compose(takes, comp).GetSample(140, 0), 4); // dahinter weiter der alte Take
    }

    [Fact]
    public void Compose_crossfades_without_a_level_dip()
    {
        var a = Constant(1f, 200);
        var b = Constant(1f, 200);
        var comp = TakeTools.Assign(TakeTools.ClearComp(200, 0), 100, 200, 1, 200);

        var audio = TakeTools.Compose([a, b], comp);

        // Gleiche Leistung: bei gleichen (korrelierten) Signalen höchstens leichte Überhöhung, kein Loch
        for (int i = 90; i < 110; i++)
        {
            Assert.InRange(audio.GetSample(i, 0), 0.99f, 1.42f);
        }
    }

    [Fact]
    public void Remove_take_hands_its_segments_to_another_take()
    {
        var takes = new[] { Constant(1f, 100), Constant(2f, 100), Constant(3f, 100) };
        var comp = TakeTools.Assign(TakeTools.ClearComp(100, 0), 50, 100, 2, 100);

        var (remaining, newComp) = TakeTools.RemoveTake(takes, comp, 2);

        Assert.Equal(2, remaining.Count);
        Assert.Equal([new CompSegment(0, 50, 0), new CompSegment(50, 100, 1)], newComp);
    }

    [Fact]
    public void Metronome_clicks_on_every_beat_with_an_accent()
    {
        var metronome = new Metronome(8000);
        var buffer = new float[8000 * 2 * 2]; // 2 Sekunden
        metronome.Render(buffer, 0, 120, 4); // Schlag alle 0,5 s

        float PeakAt(int frame) => Enumerable.Range(frame, 200).Max(i => Math.Abs(buffer[i * 2]));
        Assert.True(PeakAt(0) > 0.1f);
        Assert.True(PeakAt(4000) > 0.1f);
        Assert.True(PeakAt(0) > PeakAt(4000)); // Taktanfang betont
        Assert.Equal(0f, Math.Abs(buffer[2000 * 2]), 4); // zwischen den Schlägen Stille
    }

    [Fact]
    public void Metronome_supports_count_in_before_zero()
    {
        var metronome = new Metronome(8000);
        var buffer = new float[4000 * 2];
        metronome.Render(buffer, -4000, 120, 4); // halbe Sekunde vor dem Start

        Assert.True(Enumerable.Range(0, 200).Max(i => Math.Abs(buffer[i * 2])) > 0.1f); // Schlag bei -4000
    }
}

public class RecordingMapperTests
{
    [Fact]
    public void Count_in_and_latency_are_removed()
    {
        // Start bei Frame 10000 mit 2000 Frames Vorzähler; Latenz 300 Frames
        var runs = RecordingMapper.Map(5000, [(0, 8000)], 300);
        var limited = RecordingMapper.Limit(runs, 10000, long.MaxValue);

        var run = Assert.Single(limited);
        Assert.Equal(10000, run.TimelineStart);
        Assert.Equal(2300, run.SourceStart);   // 2000 Vorzähler + 300 Latenz
        Assert.Equal(2700, run.Length);
    }

    [Fact]
    public void Loop_passes_become_separate_runs()
    {
        // Loop 1000 … 2000, Start bei 1000: Sprünge nach 1000 und 2000 ausgegebenen Frames
        var runs = RecordingMapper.Map(3500, [(0, 1000), (1000, 1000), (2000, 1000)], 0);

        Assert.Equal(3, runs.Count);
        Assert.All(runs, r => Assert.Equal(1000, r.TimelineStart));
        Assert.Equal([1000L, 1000L, 1500L], runs.Select(r => r.Length));
    }

    [Fact]
    public void Punch_range_keeps_only_the_middle()
    {
        var runs = RecordingMapper.Map(10000, [(0, 0)], 0);

        var punched = RecordingMapper.Limit(runs, 4000, 6000);

        Assert.Equal([new RecordedRun(4000, 2000, 4000)], punched);
    }
}
