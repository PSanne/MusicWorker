using MusicWorker.Core.Mixing;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>
/// Liefert das live gemischte Stereo-Signal des Mischpults an die Soundkarte – mit Sprüngen und
/// Loop-Bereich. Merkt sich jeden Sprung, damit die Abspielposition auch nach Sprüngen und
/// Schleifen-Durchläufen exakt angezeigt werden kann (<see cref="MapPlayedToTimeline"/>).
/// </summary>
internal sealed class MixEngineSampleProvider : ISampleProvider
{
    private const int MaxJumps = 2000;

    private readonly MixEngine _engine;
    private readonly Func<long> _endFrame;
    private readonly Func<(bool Active, long Start, long End)> _loop;
    private readonly object _lock = new();
    private readonly List<(long Delivered, long Timeline)> _jumps = [];
    private long _delivered;
    private long _pendingSeek = -1;

    /// <param name="engine">Die Misch-Engine (steht schon an der Startposition).</param>
    /// <param name="endFrame">Aktuelles Ende (Projektlänge + Ausklingen).</param>
    /// <param name="loop">Aktueller Loop-Bereich (wird bei jedem Block abgefragt, also live umschaltbar).</param>
    public MixEngineSampleProvider(MixEngine engine, Func<long> endFrame, Func<(bool Active, long Start, long End)> loop)
    {
        _engine = engine;
        _endFrame = endFrame;
        _loop = loop;
        _jumps.Add((0, engine.Position));
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(engine.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>Springt an <paramref name="frame"/> (wird beim nächsten Block ausgeführt, thread-sicher).</summary>
    public void Seek(long frame) => Interlocked.Exchange(ref _pendingSeek, Math.Max(0, frame));

    /// <summary>Rechnet „so viele Frames sind zu hören gewesen“ in eine Position auf der Zeitleiste um.</summary>
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

    public int Read(float[] buffer, int offset, int count)
    {
        long pending = Interlocked.Exchange(ref _pendingSeek, -1);
        if (pending >= 0)
        {
            _engine.Seek(pending);
            AddJump(_delivered, pending);
        }

        int wanted = count / 2;
        int written = 0;
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
            _engine.Render(buffer.AsSpan(offset + written * 2, frames * 2));
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
