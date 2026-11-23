namespace MusicWorker.Core.Recording;

/// <summary>Ein zusammenhängendes Stück einer Aufnahme: ab <see cref="SourceStart"/> (in der Aufnahme) gehören <see cref="Length"/> Frames an <see cref="TimelineStart"/>.</summary>
public readonly record struct RecordedRun(long SourceStart, long Length, long TimelineStart)
{
    public long TimelineEnd => TimelineStart + Length;
}

/// <summary>
/// Ordnet eine Aufnahme der Zeitleiste zu. Während der Aufnahme lief die Wiedergabe – mit Vorzähler,
/// Sprüngen und Loop-Durchläufen. Die Wiedergabe hat sich jeden Sprung gemerkt (wie viele Frames bis dahin
/// ausgegeben waren → welche Stelle danach kam). Damit lässt sich jedes aufgenommene Sample der Stelle
/// zuordnen, die beim Einspielen zu hören war. <c>latencyFrames</c> gleicht die Verzögerung von Aus- und
/// Eingang aus (was man hört, kommt etwas später an, als es ausgegeben wurde).
/// </summary>
public static class RecordingMapper
{
    public static IReadOnlyList<RecordedRun> Map(long recordedFrames, IReadOnlyList<(long Delivered, long Timeline)> jumps, long latencyFrames)
    {
        var runs = new List<RecordedRun>();
        if (recordedFrames <= 0 || jumps.Count == 0)
        {
            return runs;
        }

        for (int i = 0; i < jumps.Count; i++)
        {
            long from = jumps[i].Delivered + latencyFrames;
            long to = i + 1 < jumps.Count ? jumps[i + 1].Delivered + latencyFrames : long.MaxValue;
            long start = Math.Max(0, from);
            long end = Math.Min(recordedFrames, to);
            if (end > start)
            {
                runs.Add(new RecordedRun(start, end - start, jumps[i].Timeline + (start - from)));
            }
        }

        return runs;
    }

    /// <summary>Beschneidet die Stücke auf den Bereich [from, to) der Zeitleiste (z. B. Punch-In oder ab Aufnahmestart).</summary>
    public static IReadOnlyList<RecordedRun> Limit(IEnumerable<RecordedRun> runs, long from, long to)
    {
        var result = new List<RecordedRun>();
        foreach (var run in runs)
        {
            long start = Math.Max(run.TimelineStart, from);
            long end = Math.Min(run.TimelineEnd, to);
            if (end > start)
            {
                result.Add(new RecordedRun(run.SourceStart + (start - run.TimelineStart), end - start, start));
            }
        }

        return result;
    }
}
