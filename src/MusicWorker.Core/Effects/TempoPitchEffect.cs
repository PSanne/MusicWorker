using System.Globalization;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Effects;

/// <summary>
/// Ändert Tempo und Tonhöhe unabhängig voneinander.
/// Tonhöhe = Zeitstreckung (WSOLA) + anschließendes Umrechnen der Geschwindigkeit.
/// </summary>
public sealed class TempoPitchEffect : IAudioEffect
{
    public const double MinTempoPercent = 50;
    public const double MaxTempoPercent = 200;
    public const double MaxSemitones = 12;

    public TempoPitchEffect(double tempoPercent, double semitones)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tempoPercent, MinTempoPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tempoPercent, MaxTempoPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Math.Abs(semitones), MaxSemitones);
        TempoPercent = tempoPercent;
        Semitones = semitones;
    }

    /// <summary>Tempo in Prozent (100 = unverändert, 200 = doppelt so schnell).</summary>
    public double TempoPercent { get; }

    /// <summary>Tonhöhenänderung in Halbtönen (12 = eine Oktave höher).</summary>
    public double Semitones { get; }

    public bool IsNeutral => TempoPercent == 100 && Semitones == 0;

    public string Name
    {
        get
        {
            var parts = new List<string>();
            if (TempoPercent != 100)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, "Tempo {0:0.#} %", TempoPercent));
            }

            if (Semitones != 0)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, "Tonhöhe {0:+0.#;-0.#} HT", Semitones));
            }

            return parts.Count == 0 ? "Tempo & Tonhöhe" : string.Join(", ", parts);
        }
    }

    public AudioBuffer Apply(AudioBuffer input)
    {
        ArgumentNullException.ThrowIfNull(input);
        double tempo = TempoPercent / 100.0;
        double pitchRatio = Math.Pow(2, Semitones / 12.0);

        // Erst so strecken, dass nach dem Beschleunigen um pitchRatio die gewünschte Länge herauskommt.
        var stretched = TimeStretcher.Stretch(input, pitchRatio / tempo);
        return Resampler.ChangeSpeed(stretched, pitchRatio);
    }
}
