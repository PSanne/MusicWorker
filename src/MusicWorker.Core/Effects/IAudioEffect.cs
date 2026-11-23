using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Effects;

/// <summary>Ein Effekt verarbeitet einen Audio-Ausschnitt und liefert das Ergebnis als neuen Puffer.</summary>
public interface IAudioEffect
{
    /// <summary>Anzeigename, erscheint z. B. im Rückgängig-Verlauf.</summary>
    string Name { get; }

    AudioBuffer Apply(AudioBuffer input);
}

/// <summary>
/// Basisklasse für Effekte, die Samples an Ort und Stelle verändern und die Länge nicht ändern.
/// Abgeleitete Klassen müssen nur <see cref="Process"/> implementieren.
/// </summary>
public abstract class SampleEffect : IAudioEffect
{
    public abstract string Name { get; }

    public AudioBuffer Apply(AudioBuffer input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var samples = input.Samples.ToArray();
        Process(samples, input.Channels, input.SampleRate);
        return new AudioBuffer(samples, input.SampleRate, input.Channels);
    }

    /// <summary>Verarbeitet verschachtelte Samples (L, R, L, R, …) direkt im Array.</summary>
    protected abstract void Process(Span<float> samples, int channels, int sampleRate);
}
