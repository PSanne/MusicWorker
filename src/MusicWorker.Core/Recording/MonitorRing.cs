namespace MusicWorker.Core.Recording;

/// <summary>
/// Puffer zum Mithören: Der Eingang schreibt Stereo-Samples hinein, die Wiedergabe liest sie wieder aus.
/// Damit die Verzögerung klein bleibt, werden ältere Samples verworfen, sobald zu viel gepuffert ist.
/// Thread-sicher (Eingang und Ausgang laufen in verschiedenen Threads).
/// </summary>
public sealed class MonitorRing
{
    private readonly object _lock = new();
    private readonly float[] _buffer;
    private int _read;
    private int _count; // in Frames

    public MonitorRing(int capacityFrames)
    {
        _buffer = new float[Math.Max(64, capacityFrames) * 2];
    }

    /// <summary>Höchstens so viele Frames dürfen beim Lesen noch warten (sonst wird vorgespult).</summary>
    public int MaxLatencyFrames { get; set; } = 4096;

    public int CapacityFrames => _buffer.Length / 2;

    public int AvailableFrames
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    /// <summary>Stereo-Samples (L, R, L, R …) anhängen; ist der Puffer voll, fallen die ältesten heraus.</summary>
    public void Write(ReadOnlySpan<float> stereo)
    {
        int frames = stereo.Length / 2;
        lock (_lock)
        {
            int capacity = CapacityFrames;
            if (frames >= capacity)
            {
                stereo = stereo[((frames - capacity) * 2)..];
                frames = capacity;
            }

            int overflow = _count + frames - capacity;
            if (overflow > 0)
            {
                _read = (_read + overflow) % capacity;
                _count -= overflow;
            }

            int write = (_read + _count) % capacity;
            for (int i = 0; i < frames; i++)
            {
                int at = ((write + i) % capacity) * 2;
                _buffer[at] = stereo[i * 2];
                _buffer[at + 1] = stereo[(i * 2) + 1];
            }

            _count += frames;
        }
    }

    /// <summary>
    /// Mischt bis zu <c>output.Length / 2</c> Frames mit <paramref name="gain"/> in <paramref name="output"/> (addiert).
    /// Liefert, wie viele Frames vorhanden waren; fehlende bleiben still.
    /// </summary>
    public int MixInto(Span<float> output, float gain)
    {
        int wanted = output.Length / 2;
        lock (_lock)
        {
            int capacity = CapacityFrames;
            int excess = _count - wanted - MaxLatencyFrames;
            if (excess > 0)
            {
                _read = (_read + excess) % capacity; // zu viel Verzögerung: vorspulen
                _count -= excess;
            }

            int frames = Math.Min(wanted, _count);
            for (int i = 0; i < frames; i++)
            {
                int at = ((_read + i) % capacity) * 2;
                output[i * 2] += _buffer[at] * gain;
                output[(i * 2) + 1] += _buffer[at + 1] * gain;
            }

            _read = (_read + frames) % capacity;
            _count -= frames;
            return frames;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _read = 0;
            _count = 0;
        }
    }
}
