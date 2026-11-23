namespace MusicWorker.Core.Dsp;

/// <summary>
/// Echo mit Rückkopplung, gedämpften Wiederholungen und optionalem Ping-Pong (Wiederholungen
/// wechseln zwischen links und rechts). Zeitänderungen gleiten weich (ohne Knacken).
/// Einstellungen lassen sich während der Wiedergabe thread-sicher ändern.
/// </summary>
public sealed class DelayProcessor
{
    /// <summary>So schnell nähert sich die tatsächliche Verzögerung einer neuen Zeit (pro Sample).</summary>
    private const float Glide = 0.0005f;

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly float[][] _lines;
    private readonly float[] _currentDelay;
    private readonly float[] _lowpass;
    private readonly float[] _delayed;
    private int _write;
    private Snapshot _snapshot = null!;

    public DelayProcessor(int sampleRate, int channels, DelaySettings settings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _sampleRate = sampleRate;
        _channels = channels;
        int length = (int)(DelaySettings.MaxTimeMs / 1000 * sampleRate) + 4;
        _lines = Enumerable.Range(0, channels).Select(_ => new float[length]).ToArray();
        _lowpass = new float[channels];
        _delayed = new float[channels];
        _currentDelay = new float[channels];
        Update(settings);
        for (int c = 0; c < channels; c++)
        {
            _currentDelay[c] = _snapshot.TargetDelay[c]; // anfangs ohne Gleiten
        }
    }

    public void Update(DelaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var delay = new float[_channels];
        var wet = new float[_channels];
        for (int c = 0; c < _channels; c++)
        {
            delay[c] = (float)(settings.TimeFor(c) / 1000 * _sampleRate);
            wet[c] = (float)(settings.MixFor(c) / 100);
        }

        Volatile.Write(ref _snapshot, new Snapshot(
            delay, wet,
            (float)(settings.FeedbackPercent / 100),
            (float)(settings.DampingPercent / 100 * 0.85),
            settings.PingPong && _channels == 2));
    }

    /// <summary>Verarbeitet verschachtelte Samples (L, R, L, R, …) an Ort und Stelle.</summary>
    public void Process(Span<float> samples)
    {
        var s = Volatile.Read(ref _snapshot);
        int frames = samples.Length / _channels;
        int length = _lines[0].Length;

        for (int frame = 0; frame < frames; frame++)
        {
            int offset = frame * _channels;

            for (int c = 0; c < _channels; c++)
            {
                _currentDelay[c] += (s.TargetDelay[c] - _currentDelay[c]) * Glide;
                _delayed[c] = Read(_lines[c], _currentDelay[c], length);
            }

            for (int c = 0; c < _channels; c++)
            {
                // Rückkopplung (bei Ping-Pong über Kreuz), mit einfachem Tiefpass gedämpft
                float source = s.PingPong ? _delayed[1 - c] : _delayed[c];
                float filtered = (1 - s.Damping) * source + s.Damping * _lowpass[c];
                _lowpass[c] = Math.Abs(filtered) < 1e-25f ? 0 : filtered;

                float input = samples[offset + c];
                _lines[c][_write] = input + _lowpass[c] * s.Feedback;
                samples[offset + c] = input + _delayed[c] * s.Wet[c];
            }

            _write = _write + 1 == length ? 0 : _write + 1;
        }
    }

    /// <summary>Liest den Wert von vor <paramref name="delay"/> Samples (linear interpoliert).</summary>
    private float Read(float[] line, float delay, int length)
    {
        float position = _write - delay;
        while (position < 0)
        {
            position += length;
        }

        int index = (int)position;
        float t = position - index;
        int next = index + 1 == length ? 0 : index + 1;
        return line[index] * (1 - t) + line[next] * t;
    }

    private sealed record Snapshot(float[] TargetDelay, float[] Wet, float Feedback, float Damping, bool PingPong);
}
