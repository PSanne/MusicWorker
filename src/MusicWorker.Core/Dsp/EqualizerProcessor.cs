using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Dsp;

/// <summary>
/// Verarbeitet Audio fortlaufend durch den 15-Band-Equalizer (15 Glockenfilter in Reihe pro Kanal).
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
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _sampleRate = sampleRate;
        _channels = channels;
        _state = new double[channels * EqualizerSettings.BandFrequencies.Count * 2];
        Update(settings);
    }

    /// <summary>Übernimmt neue Einstellungen (thread-sicher, ohne Knacken durch Zurücksetzen).</summary>
    public void Update(EqualizerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var frequencies = EqualizerSettings.BandFrequencies;
        var coefficients = new BiquadCoefficients[frequencies.Count];
        for (int band = 0; band < coefficients.Length; band++)
        {
            coefficients[band] = BiquadCoefficients.PeakingEq(
                _sampleRate, frequencies[band], EqualizerSettings.BandQ, settings.GainsDb[band]);
        }

        Volatile.Write(ref _snapshot, new Snapshot(coefficients, Decibels.ToGain(settings.PreampDb)));
    }

    /// <summary>Verarbeitet verschachtelte Samples (L, R, L, R, …) an Ort und Stelle.</summary>
    public void Process(Span<float> samples)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        var coefficients = snapshot.Coefficients;
        int bands = coefficients.Length;
        var state = _state;

        for (int i = 0; i < samples.Length; i++)
        {
            int channel = i % _channels;
            int s = channel * bands * 2;
            double x = samples[i] * snapshot.Preamp + AntiDenormal;

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

    private sealed record Snapshot(BiquadCoefficients[] Coefficients, double Preamp);
}
