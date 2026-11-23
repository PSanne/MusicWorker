using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>
/// Schaltet beim Abhören einzelne Kanäle stumm (z. B. nur links hören).
/// Betrifft nur die Wiedergabe, nie die Datei. Umschalten ist während der Wiedergabe möglich.
/// </summary>
internal sealed class ChannelMonitorSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly bool[] _muted;

    public ChannelMonitorSampleProvider(ISampleProvider source, IReadOnlyList<bool> muted)
    {
        _source = source;
        _muted = new bool[source.WaveFormat.Channels];
        for (int c = 0; c < _muted.Length && c < muted.Count; c++)
        {
            _muted[c] = muted[c];
        }
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public void SetMuted(int channel, bool muted)
    {
        if (channel >= 0 && channel < _muted.Length)
        {
            Volatile.Write(ref _muted[channel], muted);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        int channels = _muted.Length;
        for (int c = 0; c < channels; c++)
        {
            if (!Volatile.Read(ref _muted[c]))
            {
                continue;
            }

            for (int i = offset + c; i < offset + read; i += channels)
            {
                buffer[i] = 0;
            }
        }

        return read;
    }
}
