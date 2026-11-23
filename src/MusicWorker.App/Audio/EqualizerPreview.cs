using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>
/// Live-Vorhören für den Equalizer: spielt einen Bereich in Schleife und übernimmt
/// jede Reglerbewegung sofort, ohne die Wiedergabe neu zu starten.
/// </summary>
public sealed class EqualizerPreview
{
    private readonly PlaybackService _playback;
    private readonly AudioBuffer _buffer;
    private readonly int _startFrame;
    private readonly int _endFrame;
    private EqualizerProcessor? _processor;

    public EqualizerPreview(PlaybackService playback, AudioBuffer buffer, int startFrame, int endFrame)
    {
        _playback = playback;
        _buffer = buffer;
        _startFrame = startFrame;
        _endFrame = endFrame;
    }

    public bool IsPlaying => _processor is not null && _playback.State == PlaybackState.Playing;

    public void Start(EqualizerSettings settings)
    {
        var processor = new EqualizerProcessor(_buffer.SampleRate, _buffer.Channels, settings);
        _playback.Play(_buffer, _startFrame, _endFrame, loop: true,
            effects: source => new EqualizerSampleProvider(source, processor));
        _processor = processor;
    }

    public void Update(EqualizerSettings settings) => _processor?.Update(settings);

    public void Stop()
    {
        if (_processor is null)
        {
            return;
        }

        _processor = null;
        _playback.Stop();
    }
}
