namespace MusicWorker.Core.Audio;

/// <summary>
/// Unveränderlicher Audiopuffer mit verschachtelten (interleaved) 32-Bit-Float-Samples
/// im Bereich -1 … +1. Ein „Frame“ ist ein Zeitpunkt mit je einem Sample pro Kanal.
/// </summary>
/// <remarks>
/// Jede Bearbeitung liefert einen <em>neuen</em> Puffer zurück. Das macht Rückgängig/Wiederholen
/// einfach und sorgt dafür, dass die Wiedergabe nie einen halb geänderten Puffer liest.
/// </remarks>
public sealed class AudioBuffer
{
    private readonly float[] _samples;

    /// <summary>Erstellt einen Puffer. Das übergebene Array wird übernommen (nicht kopiert).</summary>
    public AudioBuffer(float[] samples, int sampleRate, int channels)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        if (samples.Length % channels != 0)
        {
            throw new ArgumentException(
                "Die Anzahl der Samples muss ein Vielfaches der Kanalanzahl sein.", nameof(samples));
        }

        _samples = samples;
        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>Abtastrate in Hz, z. B. 44100.</summary>
    public int SampleRate { get; }

    /// <summary>Anzahl der Kanäle (1 = Mono, 2 = Stereo).</summary>
    public int Channels { get; }

    /// <summary>Anzahl der Frames (Länge pro Kanal).</summary>
    public int FrameCount => _samples.Length / Channels;

    /// <summary>Spieldauer.</summary>
    public TimeSpan Duration => FrameToTime(FrameCount);

    /// <summary>Alle Samples, verschachtelt (L, R, L, R, …).</summary>
    public ReadOnlySpan<float> Samples => _samples;

    public bool IsEmpty => _samples.Length == 0;

    public float GetSample(int frame, int channel) => _samples[frame * Channels + channel];

    public static AudioBuffer CreateEmpty(int sampleRate, int channels) =>
        new(Array.Empty<float>(), sampleRate, channels);

    public static AudioBuffer CreateSilence(int sampleRate, int channels, int frameCount) =>
        new(new float[checked(frameCount * channels)], sampleRate, channels);

    public TimeSpan FrameToTime(long frame) => TimeSpan.FromSeconds((double)frame / SampleRate);

    public int TimeToFrame(TimeSpan time) =>
        (int)Math.Clamp(Math.Round(time.TotalSeconds * SampleRate), 0, FrameCount);

    public bool HasSameFormat(AudioBuffer other) =>
        other.SampleRate == SampleRate && other.Channels == Channels;

    /// <summary>Kopiert einen Ausschnitt in einen neuen Puffer.</summary>
    public AudioBuffer Slice(int startFrame, int frameCount)
    {
        ValidateRange(startFrame, frameCount);
        var result = new float[frameCount * Channels];
        Array.Copy(_samples, startFrame * Channels, result, 0, result.Length);
        return new AudioBuffer(result, SampleRate, Channels);
    }

    /// <summary>
    /// Ersetzt <paramref name="frameCount"/> Frames ab <paramref name="startFrame"/> durch
    /// <paramref name="replacement"/> (der beliebig lang sein darf). Grundlage für Löschen,
    /// Einfügen und Effekte.
    /// </summary>
    public AudioBuffer ReplaceRange(int startFrame, int frameCount, AudioBuffer replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ValidateRange(startFrame, frameCount);
        EnsureSameFormat(replacement);

        int head = startFrame * Channels;
        int tailStart = (startFrame + frameCount) * Channels;
        int tailLength = _samples.Length - tailStart;
        var result = new float[checked(head + replacement._samples.Length + tailLength)];

        Array.Copy(_samples, 0, result, 0, head);
        Array.Copy(replacement._samples, 0, result, head, replacement._samples.Length);
        Array.Copy(_samples, tailStart, result, head + replacement._samples.Length, tailLength);
        return new AudioBuffer(result, SampleRate, Channels);
    }

    public AudioBuffer RemoveRange(int startFrame, int frameCount) =>
        ReplaceRange(startFrame, frameCount, CreateEmpty(SampleRate, Channels));

    public AudioBuffer Insert(int atFrame, AudioBuffer other) => ReplaceRange(atFrame, 0, other);

    private void ValidateRange(int startFrame, int frameCount)
    {
        if (startFrame < 0 || frameCount < 0 || startFrame > FrameCount - frameCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startFrame),
                $"Bereich {startFrame}+{frameCount} liegt außerhalb des Puffers (Länge {FrameCount}).");
        }
    }

    private void EnsureSameFormat(AudioBuffer other)
    {
        if (!HasSameFormat(other))
        {
            throw new ArgumentException(
                $"Audioformat passt nicht: {other.SampleRate} Hz/{other.Channels} Kanäle " +
                $"statt {SampleRate} Hz/{Channels} Kanäle.", nameof(other));
        }
    }
}
