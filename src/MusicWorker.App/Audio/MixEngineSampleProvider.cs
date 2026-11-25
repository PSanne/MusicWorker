using MusicWorker.Core.Mixing;
using MusicWorker.Core.Notes;
using MusicWorker.Core.Recording;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>
/// Liefert das live gemischte Stereo-Signal des Mischpults an die Soundkarte – mit Sprüngen, Loop-Bereich,
/// Vorzähler und Metronom. Merkt sich jeden Sprung (wie viele Frames ausgegeben waren → welche Stelle der
/// Zeitleiste danach kam), damit Abspielposition und Aufnahmen auch nach Sprüngen und Schleifen-Durchläufen
/// exakt der Zeitleiste zugeordnet werden können (<see cref="MapPlayedToTimeline"/>).
/// </summary>
internal sealed class MixEngineSampleProvider : ISampleProvider
{
    private const int MaxJumps = 4000;

    private readonly MixEngine _engine;
    private readonly Func<long> _endFrame;
    private readonly Func<(bool Active, long Start, long End)> _loop;
    private readonly Func<(bool Enabled, TempoMap Tempo, int BeatsPerBar)> _metronome;
    private readonly Metronome _click;
    private readonly object _lock = new();
    private readonly List<(long Delivered, long Timeline)> _jumps = [];
    private long _delivered;
    private long _pendingSeek = -1;
    private long _countIn;

    /// <param name="engine">Die Misch-Engine (steht schon an der Startposition).</param>
    /// <param name="endFrame">Aktuelles Ende (Projektlänge + Ausklingen).</param>
    /// <param name="loop">Aktueller Loop-Bereich (wird bei jedem Block abgefragt, also live umschaltbar).</param>
    /// <param name="metronome">Metronom an/aus, Tempo und Taktart (live abgefragt).</param>
    /// <param name="countInFrames">Vorzähler vor dem Start (nur Metronom, die Zeitleiste steht so lange vor dem Start).</param>
    public MixEngineSampleProvider(
        MixEngine engine,
        Func<long> endFrame,
        Func<(bool Active, long Start, long End)> loop,
        Func<(bool Enabled, TempoMap Tempo, int BeatsPerBar)>? metronome = null,
        long countInFrames = 0)
    {
        _engine = engine;
        _endFrame = endFrame;
        _loop = loop;
        _metronome = metronome ?? (() => (false, TempoMap.Constant(120), 4));
        _click = new Metronome(engine.SampleRate);
        _countIn = Math.Max(0, countInFrames);
        _jumps.Add((0, engine.Position - _countIn));
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(engine.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>Springt an <paramref name="frame"/> (wird beim nächsten Block ausgeführt, thread-sicher).</summary>
    public void Seek(long frame) => Interlocked.Exchange(ref _pendingSeek, Math.Max(0, frame));

    /// <summary>Rechnet „so viele Frames sind zu hören gewesen“ in eine Position auf der Zeitleiste um (im Vorzähler negativ bzw. vor dem Start).</summary>
    public long MapPlayedToTimeline(long played)
    {
        lock (_lock)
        {
            for (int i = _jumps.Count - 1; i >= 0; i--)
            {
                if (_jumps[i].Delivered <= played)
                {
                    return _jumps[i].Timeline + (played - _jumps[i].Delivered);
                }
            }

            return _jumps.Count > 0 ? _jumps[0].Timeline : 0;
        }
    }

    /// <summary>Alle Sprünge bisher (für die Zuordnung von Aufnahmen).</summary>
    public IReadOnlyList<(long Delivered, long Timeline)> Jumps
    {
        get
        {
            lock (_lock)
            {
                return _jumps.ToArray();
            }
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        long pending = Interlocked.Exchange(ref _pendingSeek, -1);
        if (pending >= 0)
        {
            _countIn = 0;
            _engine.Seek(pending);
            AddJump(_delivered, pending);
        }

        var (clickOn, tempo, beatsPerBar) = _metronome();
        int wanted = count / 2;
        int written = 0;

        // Vorzähler: nur Klicks, die Engine wartet an der Startposition
        if (_countIn > 0)
        {
            int frames = (int)Math.Min(wanted, _countIn);
            var span = buffer.AsSpan(offset, frames * 2);
            span.Clear();
            _click.Render(span, _engine.Position - _countIn, tempo, beatsPerBar);
            _countIn -= frames;
            written += frames;
        }

        while (written < wanted)
        {
            var (loopActive, loopStart, loopEnd) = _loop();
            bool looping = loopActive && loopEnd > loopStart;
            long position = _engine.Position;
            long end = looping ? loopEnd : _endFrame();

            if (position >= end)
            {
                if (!looping)
                {
                    break;
                }

                _engine.Seek(loopStart); // Schleife: zurück zum Anfang des Loop-Bereichs
                AddJump(_delivered + written, loopStart);
                continue;
            }

            int frames = (int)Math.Min(wanted - written, end - position);
            var span = buffer.AsSpan(offset + written * 2, frames * 2);
            _engine.Render(span);
            if (clickOn)
            {
                _click.Render(span, position, tempo, beatsPerBar);
            }

            written += frames;
        }

        _delivered += written;
        return written * 2;
    }

    private void AddJump(long delivered, long timeline)
    {
        lock (_lock)
        {
            _jumps.Add((delivered, timeline));
            if (_jumps.Count > MaxJumps)
            {
                _jumps.RemoveRange(0, _jumps.Count - MaxJumps);
            }
        }
    }
}
