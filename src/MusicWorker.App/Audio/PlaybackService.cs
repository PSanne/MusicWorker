using MusicWorker.Core.Audio;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>Spielt einen Bereich eines <see cref="AudioBuffer"/> über die Standard-Soundkarte ab.</summary>
public sealed class PlaybackService : IDisposable
{
    private WaveOutEvent? _output;
    private int _startFrame;
    private int _endFrame;
    private bool _loop;

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
            int length = _endFrame - _startFrame;
            if (_loop && length > 0)
            {
                return _startFrame + (int)(playedFrames % length);
            }

            return (int)Math.Min(_startFrame + playedFrames, _endFrame);
        }
    }

    /// <summary>
    /// Startet die Wiedergabe von <paramref name="startFrame"/> bis <paramref name="endFrame"/>.
    /// </summary>
    /// <param name="loop">Bereich endlos wiederholen (z. B. zum Vorhören von Effekten).</param>
    /// <param name="effects">Optionale Live-Effekte, die zwischen Puffer und Soundkarte geschaltet werden.</param>
    public void Play(
        AudioBuffer buffer,
        int startFrame,
        int endFrame,
        bool loop = false,
        Func<ISampleProvider, ISampleProvider>? effects = null)
    {
        Stop();

        ISampleProvider provider = new AudioBufferSampleProvider(buffer, startFrame, endFrame, loop);
        if (effects is not null)
        {
            provider = effects(provider);
        }

        var output = new WaveOutEvent { DesiredLatency = 120 };
        output.PlaybackStopped += OnPlaybackStopped;
        output.Init(provider);

        _output = output;
        _startFrame = startFrame;
        _endFrame = endFrame;
        _loop = loop;
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
