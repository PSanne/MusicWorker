namespace MusicWorker.Core.Notes;

/// <summary>Tempowechsel: ab <paramref name="Tick"/> (vom Songanfang) gilt <paramref name="Bpm"/>.</summary>
public readonly record struct TempoChange(long Tick, double Bpm);

/// <summary>
/// Tempoverlauf eines Songs: ein Grundtempo ab dem Anfang und beliebig viele Tempowechsel.
/// Rechnet zwischen musikalischer Zeit (Ticks, Takte) und Audio-Zeit (Sekunden, Frames) um.
/// Vor dem Anfang (negative Zeiten, z. B. Vorzähler) gilt das Grundtempo.
/// </summary>
public sealed class TempoMap
{
    public const double MinBpm = 10;
    public const double MaxBpm = 1000;

    private readonly TempoChange[] _changes; // sortiert, der erste bei Tick 0
    private readonly double[] _seconds; // Zeitpunkt jedes Wechsels in Sekunden

    public TempoMap(double bpm, IEnumerable<TempoChange>? changes = null)
    {
        var list = new SortedDictionary<long, double> { [0] = Clamp(bpm) };
        foreach (var change in changes ?? [])
        {
            list[Math.Max(0, change.Tick)] = Clamp(change.Bpm);
        }

        // Wechsel, die das Tempo nicht ändern, weglassen
        var result = new List<TempoChange>();
        foreach (var (tick, value) in list)
        {
            if (result.Count == 0 || Math.Abs(result[^1].Bpm - value) > 1e-9)
            {
                result.Add(new TempoChange(tick, value));
            }
        }

        _changes = [.. result];
        _seconds = new double[_changes.Length];
        for (int i = 1; i < _changes.Length; i++)
        {
            _seconds[i] = _seconds[i - 1] + ((_changes[i].Tick - _changes[i - 1].Tick) * SecondsPerTick(_changes[i - 1].Bpm));
        }
    }

    /// <summary>Gleichbleibendes Tempo.</summary>
    public static TempoMap Constant(double bpm) => new(bpm);

    /// <summary>Tempo am Anfang.</summary>
    public double BaseBpm => _changes[0].Bpm;

    /// <summary>Alle Tempi samt Anfang (der erste Eintrag liegt bei Tick 0).</summary>
    public IReadOnlyList<TempoChange> Changes => _changes;

    /// <summary>Tempowechsel nach dem Anfang.</summary>
    public IEnumerable<TempoChange> ChangesAfterStart => _changes.Skip(1);

    public bool IsConstant => _changes.Length == 1;

    public double BpmAt(long tick) => _changes[IndexAtTick(tick)].Bpm;

    public double TicksToSeconds(double ticks)
    {
        if (ticks <= 0)
        {
            return ticks * SecondsPerTick(BaseBpm);
        }

        int i = IndexAtTick((long)Math.Floor(ticks));
        return _seconds[i] + ((ticks - _changes[i].Tick) * SecondsPerTick(_changes[i].Bpm));
    }

    public double SecondsToTicks(double seconds)
    {
        if (seconds <= 0)
        {
            return seconds / SecondsPerTick(BaseBpm);
        }

        int i = Array.BinarySearch(_seconds, seconds);
        if (i < 0)
        {
            i = ~i - 1;
        }

        i = Math.Clamp(i, 0, _changes.Length - 1);
        return _changes[i].Tick + ((seconds - _seconds[i]) / SecondsPerTick(_changes[i].Bpm));
    }

    public long TicksToFrames(long ticks, int sampleRate) => (long)Math.Round(TicksToSeconds(ticks) * sampleRate);

    public double TicksToFramesExact(double ticks, int sampleRate) => TicksToSeconds(ticks) * sampleRate;

    public long FramesToTicks(long frames, int sampleRate) => (long)Math.Round(SecondsToTicks(frames / (double)sampleRate));

    public double FramesToTicksExact(double frames, int sampleRate) => SecondsToTicks(frames / sampleRate);

    /// <summary>Frame des Schlags (Viertel), der <paramref name="frame"/> am nächsten liegt.</summary>
    public long NearestBeatFrame(long frame, int sampleRate)
    {
        double beat = Math.Round(FramesToTicksExact(frame, sampleRate) / MusicalTime.TicksPerQuarter);
        return (long)Math.Round(TicksToFramesExact(beat * MusicalTime.TicksPerQuarter, sampleRate));
    }

    /// <summary>
    /// Ein Tick-Abschnitt eines Clips, der bei <paramref name="clipStartFrame"/> beginnt, in Frames relativ zum Clip-Anfang.
    /// </summary>
    public long ClipTicksToFrames(long clipStartFrame, long ticks, int sampleRate)
    {
        double startTick = FramesToTicksExact(clipStartFrame, sampleRate);
        return (long)Math.Round(TicksToFramesExact(startTick + ticks, sampleRate) - clipStartFrame);
    }

    /// <summary>Gegenstück zu <see cref="ClipTicksToFrames"/>: Frames relativ zum Clip-Anfang in Ticks relativ zum Clip-Anfang.</summary>
    public long ClipFramesToTicks(long clipStartFrame, long frames, int sampleRate)
    {
        double startTick = FramesToTicksExact(clipStartFrame, sampleRate);
        return (long)Math.Round(FramesToTicksExact(clipStartFrame + frames, sampleRate) - startTick);
    }

    /// <summary>Gleicher Tempoverlauf?</summary>
    public bool SameAs(TempoMap? other) =>
        other is not null && other._changes.Length == _changes.Length && other._changes.SequenceEqual(_changes);

    private int IndexAtTick(long tick)
    {
        int lo = 0;
        int hi = _changes.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_changes[mid].Tick <= tick)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    private static double SecondsPerTick(double bpm) => 60.0 / bpm / MusicalTime.TicksPerQuarter;

    private static double Clamp(double bpm) => double.IsFinite(bpm) ? Math.Clamp(bpm, MinBpm, MaxBpm) : 120;
}
