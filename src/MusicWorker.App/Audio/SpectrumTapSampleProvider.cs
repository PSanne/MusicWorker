using MusicWorker.Core.Dsp;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>
/// Reicht das Audio unverändert durch und gibt eine Kopie an die Spektrumanalysatoren:
/// einen pro Kanal (Stereo: links und rechts getrennt) oder einen für die Mono-Summe.
/// </summary>
internal sealed class SpectrumTapSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly IReadOnlyList<SpectrumAnalyzer> _analyzers;

    public SpectrumTapSampleProvider(ISampleProvider source, IReadOnlyList<SpectrumAnalyzer> analyzers)
    {
        _source = source;
        _analyzers = analyzers;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        var samples = buffer.AsSpan(offset, read);
        int channels = WaveFormat.Channels;

        if (_analyzers.Count == 1)
        {
            _analyzers[0].Write(samples, channels);
        }
        else
        {
            for (int c = 0; c < _analyzers.Count && c < channels; c++)
            {
                _analyzers[c].WriteChannel(samples, channels, c);
            }
        }

        return read;
    }
}
