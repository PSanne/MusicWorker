using MusicWorker.Core.Dsp;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>Reicht das Audio unverändert weiter und misst dabei die Lautheit (für die Live-Anzeige in LUFS).</summary>
public sealed class LoudnessTapSampleProvider(ISampleProvider source, LoudnessMeter meter) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (read > 0)
        {
            meter.Process(buffer.AsSpan(offset, read));
        }

        return read;
    }
}
