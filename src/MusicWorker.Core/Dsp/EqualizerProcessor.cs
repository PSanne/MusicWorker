using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Dsp;

/// <summary>
/// Verarbeitet Audio fortlaufend durch den 15-Band-Equalizer (15 Glockenfilter in Reihe pro Kanal).
/// Jeder Kanal kann eine eigene Einstellung haben (Pro-Kanal-EQ, z. B. links anders als rechts).
/// Die Einstellungen können während der Wiedergabe aus einem anderen Thread geändert werden
/// (<see cref="Update"/>) – so funktioniert das Live-Vorhören.
/// </summary>
public sealed class EqualizerProcessor
{
    // Winziger Gleichanteil gegen „Denormals“ (sehr kleine Zahlen, die die CPU stark bremsen können).
    private const double AntiDenormal = 1e-20;

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly double[] _state; // je Kanal und Band zwei Zustandswerte
    private Snapshot _snapshot = null!;

    public EqualizerProcessor(int sampleRate, int channels, EqualizerSettings settings)
        : this(sampleRate, channels, [settings])
    {
    }

    /// <summary>
    /// Erstellt den Prozessor mit einer Einstellung pro Kanal. Gibt es weniger Einstellungen als
    /// Kanäle, gilt die letzte auch für die übrigen Kanäle.
    /// </summary>
    public EqualizerProcessor(int sampleRate, int channels, IReadOnlyList<EqualizerSettings> perChannel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _sampleRate = sampleRate;
        _channels = channels;
        _state = new double[channels * EqualizerSettings.BandFrequencies.Count * 2];
        Update(perChannel);
    }

    /// <summary>Übernimmt eine Einstellung für alle Kanäle (thread-sicher, ohne Knacken durch Zurücksetzen).</summary>
    public void Update(EqualizerSettings settings) => Update([settings]);

    /// <summary>Übernimmt eine Einstellung pro Kanal (thread-sicher, ohne Knacken durch Zurücksetzen).</summary>
    public void Update(IReadOnlyList<EqualizerSettings> perChannel)
    {
        ArgumentNullException.ThrowIfNull(perChannel);
        if (perChannel.Count == 0)
        {
            throw new ArgumentException("Mindestens eine Einstellung ist erforderlich.", nameof(perChannel));
        }

        var coefficients = new BiquadCoefficients[_channels][];
        var preamps = new double[_channels];
        for (int c = 0; c < _channels; c++)
        {
            var settings = perChannel[Math.Min(c, perChannel.Count - 1)];
            coefficients[c] = settings.CreateCoefficients(_sampleRate);
            preamps[c] = Decibels.ToGain(settings.PreampDb);
        }

        Volatile.Write(ref _snapshot, new Snapshot(coefficients, preamps));
    }

    /// <summary>Verarbeitet verschachtelte Samples (L, R, L, R, …) an Ort und Stelle.</summary>
    public void Process(Span<float> samples)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        int bands = EqualizerSettings.BandFrequencies.Count;
        var state = _state;

        for (int i = 0; i < samples.Length; i++)
        {
            int channel = i % _channels;
            var coefficients = snapshot.Coefficients[channel];
            int s = channel * bands * 2;
            double x = samples[i] * snapshot.Preamps[channel] + AntiDenormal;

            for (int band = 0; band < bands; band++, s += 2)
            {
                ref readonly var c = ref coefficients[band];
                double y = c.B0 * x + state[s];
                state[s] = c.B1 * x - c.A1 * y + state[s + 1];
                state[s + 1] = c.B2 * x - c.A2 * y;
                x = y;
            }

            samples[i] = (float)x;
        }
    }

    private sealed record Snapshot(BiquadCoefficients[][] Coefficients, double[] Preamps);
}
