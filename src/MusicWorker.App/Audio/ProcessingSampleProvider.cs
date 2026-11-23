using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>Verarbeitet einen Block verschachtelter Samples an Ort und Stelle.</summary>
internal delegate void SampleProcessor(Span<float> samples);

/// <summary>Schickt die Wiedergabe in Echtzeit durch einen Effekt (z. B. Kompressor oder Echo).</summary>
internal sealed class ProcessingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly SampleProcessor _process;

    public ProcessingSampleProvider(ISampleProvider source, SampleProcessor process)
    {
        _source = source;
        _process = process;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        _process(buffer.AsSpan(offset, read));
        return read;
    }
}
