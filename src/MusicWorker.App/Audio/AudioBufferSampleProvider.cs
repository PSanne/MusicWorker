using MusicWorker.Core.Audio;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>
/// Liefert die Samples eines <see cref="AudioBuffer"/> an NAudio – für Wiedergabe und Export.
/// Da der Puffer unveränderlich ist, kann die Wiedergabe gefahrlos in einem anderen Thread lesen.
/// Mit <see cref="Seek"/> lässt sich während der Wiedergabe an eine andere Stelle springen.
/// </summary>
internal sealed class AudioBufferSampleProvider : ISampleProvider
{
    private readonly AudioBuffer _buffer;
    private readonly bool _loop;
    private int _startFrame;
    private int _endFrame;
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

    public int EndFrame => Volatile.Read(ref _endFrame);

    /// <summary>
    /// Springt an <paramref name="frame"/> (thread-sicher). Liegt die Stelle außerhalb des
    /// abgespielten Bereichs, wird ab dort bis zum Ende der Datei weitergespielt.
    /// </summary>
    public void Seek(int frame)
    {
        frame = Math.Clamp(frame, 0, _buffer.FrameCount);
        if (frame < Volatile.Read(ref _startFrame) || frame >= Volatile.Read(ref _endFrame))
        {
            Volatile.Write(ref _startFrame, 0);
            Volatile.Write(ref _endFrame, _buffer.FrameCount);
        }

        Volatile.Write(ref _positionFrame, frame);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int channels = _buffer.Channels;
        int written = 0;
        int original = Volatile.Read(ref _positionFrame);
        int position = original;
        int end = Volatile.Read(ref _endFrame);

        while (written < count)
        {
            if (position >= end)
            {
                if (!_loop)
                {
                    break;
                }

                position = Volatile.Read(ref _startFrame); // Schleife: wieder von vorn
            }

            int frames = Math.Min(end - position, (count - written) / channels);
            if (frames <= 0)
            {
                break;
            }

            _buffer.Samples.Slice(position * channels, frames * channels)
                .CopyTo(buffer.AsSpan(offset + written, frames * channels));
            position += frames;
            written += frames * channels;
        }

        // Nur fortschreiben, wenn nicht inzwischen gesprungen wurde – sonst ginge der Sprung verloren.
        Interlocked.CompareExchange(ref _positionFrame, position, original);
        return written;
    }
}
