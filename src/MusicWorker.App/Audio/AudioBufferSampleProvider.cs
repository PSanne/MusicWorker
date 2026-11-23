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
    private readonly int _startFrame;
    private readonly int _endFrame;
    private readonly bool _loop;
    private int _positionFrame;

    public AudioBufferSampleProvider(AudioBuffer buffer, int startFrame, int endFrame, bool loop = false)
    {
        _buffer = buffer;
        _startFrame = Math.Clamp(startFrame, 0, buffer.FrameCount);
        _endFrame = Math.Clamp(endFrame, _startFrame, buffer.FrameCount);
        _positionFrame = _startFrame;
        _loop = loop && _endFrame > _startFrame;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(buffer.SampleRate, buffer.Channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int channels = _buffer.Channels;
        int written = 0;
        int position = Volatile.Read(ref _positionFrame);

        while (written < count)
        {
            if (position >= _endFrame)
            {
                if (!_loop)
                {
                    break;
                }

                position = _startFrame; // Schleife: wieder von vorn
            }

            int frames = Math.Min(_endFrame - position, (count - written) / channels);
            if (frames <= 0)
            {
                break;
            }

            _buffer.Samples.Slice(position * channels, frames * channels)
                .CopyTo(buffer.AsSpan(offset + written, frames * channels));
            position += frames;
            written += frames * channels;
        }

        Volatile.Write(ref _positionFrame, position);
        return written;
    }
}
