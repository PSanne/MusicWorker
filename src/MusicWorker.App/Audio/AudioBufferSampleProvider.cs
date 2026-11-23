using MusicWorker.Core.Audio;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>
/// Liefert die Samples eines <see cref="AudioBuffer"/> an NAudio – für Wiedergabe und Export.
/// Da der Puffer unveränderlich ist, kann die Wiedergabe gefahrlos in einem anderen Thread lesen.
/// </summary>
internal sealed class AudioBufferSampleProvider : ISampleProvider
{
    private readonly AudioBuffer _buffer;
    private readonly int _endFrame;
    private int _positionFrame;

    public AudioBufferSampleProvider(AudioBuffer buffer, int startFrame, int endFrame)
    {
        _buffer = buffer;
        _positionFrame = Math.Clamp(startFrame, 0, buffer.FrameCount);
        _endFrame = Math.Clamp(endFrame, _positionFrame, buffer.FrameCount);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(buffer.SampleRate, buffer.Channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int channels = _buffer.Channels;
        int position = Volatile.Read(ref _positionFrame);
        int frames = Math.Min(_endFrame - position, count / channels);
        if (frames <= 0)
        {
            return 0;
        }

        _buffer.Samples.Slice(position * channels, frames * channels)
            .CopyTo(buffer.AsSpan(offset, frames * channels));
        Volatile.Write(ref _positionFrame, position + frames);
        return frames * channels;
    }
}
