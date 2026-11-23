using MusicWorker.Core.Dsp;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>Schickt die Wiedergabe in Echtzeit durch den Hall.</summary>
internal sealed class ReverbSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly ReverbProcessor _processor;

    public ReverbSampleProvider(ISampleProvider source, ReverbProcessor processor)
    {
        _source = source;
        _processor = processor;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        _processor.Process(buffer.AsSpan(offset, read));
        return read;
    }
}
