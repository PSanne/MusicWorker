namespace MusicWorker.Core.Dsp;

/// <summary>
/// Hall nach dem Freeverb-Prinzip (Schroeder/Moorer): pro Kanal 8 parallele, gedämpfte
/// Kammfilter und 4 Allpässe in Reihe. Jeder Kanal hat seinen eigenen „Raum“ (leicht versetzte
/// Verzögerungen) und eine eigene Hall-Stärke. Einstellungen lassen sich während der Wiedergabe
/// thread-sicher ändern (<see cref="Update"/>).
/// </summary>
public sealed class ReverbProcessor
{
    private static readonly int[] CombTunings = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
    private static readonly int[] AllpassTunings = [556, 441, 341, 225];
    private const int StereoSpread = 23;
    private const float FixedGain = 0.015f;
    private const float WetScale = 1.2f;
    private const float DryReduction = 0.3f;

    private readonly int _channels;
    private readonly ChannelTank[] _tanks;
    private Snapshot _snapshot = null!;

    /// <summary>
    /// Erstellt den Hall. <c>channelOffset</c> ist die Kanalnummer des ersten verarbeiteten Kanals –
    /// damit klingt z. B. „nur rechts“ genauso wie der rechte Kanal einer Stereo-Verarbeitung.
    /// </summary>
    public ReverbProcessor(int sampleRate, int channels, ReverbSettings settings, int channelOffset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _channels = channels;
        double scale = sampleRate / 44100.0;
        _tanks = Enumerable.Range(0, channels)
            .Select(c => new ChannelTank(scale, (c + channelOffset) * StereoSpread))
            .ToArray();
        Update(settings);
    }

    public void Update(ReverbSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        float room = (float)(settings.RoomSizePercent / 100);
        float damp = (float)(settings.DampingPercent / 100 * 0.4);
        var wet = new float[_channels];
        var dry = new float[_channels];
        for (int c = 0; c < _channels; c++)
        {
            float amount = (float)(settings.AmountFor(c) / 100);
            wet[c] = amount * WetScale;
            dry[c] = 1f - amount * DryReduction;
        }

        Volatile.Write(ref _snapshot, new Snapshot(room * 0.28f + 0.7f, damp, 1f - damp, wet, dry));
    }

    /// <summary>Verarbeitet verschachtelte Samples (L, R, L, R, …) an Ort und Stelle.</summary>
    public void Process(Span<float> samples)
    {
        var s = Volatile.Read(ref _snapshot);
        for (int i = 0; i < samples.Length; i++)
        {
            int channel = i % _channels;
            float input = samples[i];
            float reverb = _tanks[channel].Process(input * FixedGain, s.Feedback, s.Damp1, s.Damp2);
            samples[i] = input * s.Dry[channel] + reverb * s.Wet[channel];
        }
    }

    private sealed record Snapshot(float Feedback, float Damp1, float Damp2, float[] Wet, float[] Dry);

    /// <summary>Der „Raum“ eines Kanals: 8 Kammfilter parallel, dann 4 Allpässe in Reihe.</summary>
    private sealed class ChannelTank
    {
        private const float AllpassFeedback = 0.5f;

        private readonly float[][] _combs;
        private readonly int[] _combIndex;
        private readonly float[] _combFilter;
        private readonly float[][] _allpasses;
        private readonly int[] _allpassIndex;

        public ChannelTank(double scale, int spread)
        {
            _combs = CombTunings.Select(t => new float[Math.Max(1, (int)((t + spread) * scale))]).ToArray();
            _combIndex = new int[_combs.Length];
            _combFilter = new float[_combs.Length];
            _allpasses = AllpassTunings.Select(t => new float[Math.Max(1, (int)((t + spread) * scale))]).ToArray();
            _allpassIndex = new int[_allpasses.Length];
        }

        public float Process(float input, float feedback, float damp1, float damp2)
        {
            float output = 0;
            for (int i = 0; i < _combs.Length; i++)
            {
                var buffer = _combs[i];
                int index = _combIndex[i];
                float delayed = buffer[index];
                float filtered = delayed * damp2 + _combFilter[i] * damp1;
                _combFilter[i] = Math.Abs(filtered) < 1e-25f ? 0 : filtered; // „Denormals“ vermeiden
                buffer[index] = input + _combFilter[i] * feedback;
                _combIndex[i] = index + 1 == buffer.Length ? 0 : index + 1;
                output += delayed;
            }

            for (int i = 0; i < _allpasses.Length; i++)
            {
                var buffer = _allpasses[i];
                int index = _allpassIndex[i];
                float delayed = buffer[index];
                float stored = output + delayed * AllpassFeedback;
                buffer[index] = Math.Abs(stored) < 1e-25f ? 0 : stored;
                _allpassIndex[i] = index + 1 == buffer.Length ? 0 : index + 1;
                output = delayed - output;
            }

            return output;
        }
    }
}
