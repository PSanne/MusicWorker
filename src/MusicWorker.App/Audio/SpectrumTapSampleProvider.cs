using MusicWorker.Core.Dsp;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>Reicht das Audio unverändert durch und gibt eine Kopie an den Spektrumanalysator.</summary>
internal sealed class SpectrumTapSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly SpectrumAnalyzer _analyzer;

    public SpectrumTapSampleProvider(ISampleProvider source, SpectrumAnalyzer analyzer)
    {
        _source = source;
        _analyzer = analyzer;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        _analyzer.Write(buffer.AsSpan(offset, read), WaveFormat.Channels);
        return read;
    }
}
