using MusicWorker.Core.Audio;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>Spielt einen Bereich eines <see cref="AudioBuffer"/> über die Standard-Soundkarte ab.</summary>
public sealed class PlaybackService : IDisposable
{
    private WaveOutEvent? _output;
    private int _startFrame;
    private int _endFrame;

    /// <summary>
    /// Die Wiedergabe ist zu Ende (Bereichsende erreicht oder Fehler).
    /// Wird im UI-Thread ausgelöst, weil NAudio den SynchronizationContext des Aufrufers nutzt.
    /// </summary>
    public event EventHandler<StoppedEventArgs>? PlaybackEnded;

    public PlaybackState State => _output?.PlaybackState ?? PlaybackState.Stopped;

    /// <summary>Frame, das gerade tatsächlich zu hören ist (berücksichtigt die Puffer-Latenz).</summary>
    public int CurrentFrame
    {
        get
        {
            if (_output is null)
            {
                return _startFrame;
            }

            long playedFrames = _output.GetPosition() / _output.OutputWaveFormat.BlockAlign;
            return (int)Math.Min(_startFrame + playedFrames, _endFrame);
        }
    }

    public void Play(AudioBuffer buffer, int startFrame, int endFrame)
    {
        Stop();

        var provider = new AudioBufferSampleProvider(buffer, startFrame, endFrame);
        var output = new WaveOutEvent { DesiredLatency = 120 };
        output.PlaybackStopped += OnPlaybackStopped;
        output.Init(provider);

        _output = output;
        _startFrame = startFrame;
        _endFrame = endFrame;
        output.Play();
    }

    public void Pause() => _output?.Pause();

    public void Resume() => _output?.Play();

    public void Stop()
    {
        var output = _output;
        if (output is null)
        {
            return;
        }

        _output = null;
        output.PlaybackStopped -= OnPlaybackStopped;
        output.Stop();
        output.Dispose();
    }

    public void Dispose() => Stop();

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (sender is not WaveOutEvent output || !ReferenceEquals(output, _output))
        {
            return;
        }

        _output = null;
        output.PlaybackStopped -= OnPlaybackStopped;
        output.Dispose();
        PlaybackEnded?.Invoke(this, e);
    }
}
