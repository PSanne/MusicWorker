using MusicWorker.Core.Mixing;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>Liefert das live gemischte Stereo-Signal des Mischpults an die Soundkarte.</summary>
internal sealed class MixEngineSampleProvider : ISampleProvider
{
    private readonly MixEngine _engine;
    private readonly Func<long> _endFrame;

    /// <param name="engine">Die Misch-Engine.</param>
    /// <param name="endFrame">Liefert das aktuelle Ende (Projektlänge + Ausklingen) – danach endet die Wiedergabe.</param>
    public MixEngineSampleProvider(MixEngine engine, Func<long> endFrame)
    {
        _engine = engine;
        _endFrame = endFrame;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(engine.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        long remaining = _endFrame() - _engine.Position;
        if (remaining <= 0)
        {
            return 0;
        }

        int frames = (int)Math.Min(count / 2, remaining);
        _engine.Render(buffer.AsSpan(offset, frames * 2));
        return frames * 2;
    }
}
