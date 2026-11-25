using MusicWorker.Core.Notes;

namespace MusicWorker.Core.Recording;

/// <summary>
/// Metronom: kurze Klicks auf jedem Schlag, der erste Schlag im Takt höher und lauter. Wird nur zur
/// Wiedergabe beigemischt (nie in den Mixdown).
/// </summary>
public sealed class Metronome(int sampleRate)
{
    private const double ClickSeconds = 0.035;

    public int SampleRate { get; } = sampleRate;

    /// <summary>Lautstärke 0 … 1.</summary>
    public double Volume { get; set; } = 0.5;

    /// <summary>Frames pro Schlag bei <paramref name="bpm"/>.</summary>
    public double BeatFrames(double bpm) => 60.0 / Math.Clamp(bpm, 20, 400) * SampleRate;

    /// <summary>
    /// Addiert die Klicks für die Frames ab <paramref name="startFrame"/> (Zeitleiste) in <paramref name="stereo"/>.
    /// Negative Positionen sind erlaubt (Vorzähler vor dem Start).
    /// </summary>
    public void Render(Span<float> stereo, long startFrame, double bpm, int beatsPerBar) =>
        Render(stereo, startFrame, TempoMap.Constant(Math.Clamp(bpm, 20, 400)), beatsPerBar);

    /// <summary>Wie oben, folgt aber den Tempowechseln von <paramref name="tempo"/>.</summary>
    public void Render(Span<float> stereo, long startFrame, TempoMap tempo, int beatsPerBar)
    {
        ArgumentNullException.ThrowIfNull(tempo);
        int frames = stereo.Length / 2;
        int clickLength = (int)(ClickSeconds * SampleRate);
        beatsPerBar = Math.Max(1, beatsPerBar);

        // Erster Schlag, dessen Klick in diesen Block hineinreicht
        long firstBeat = (long)Math.Floor(tempo.FramesToTicksExact(startFrame - clickLength, SampleRate) / MusicalTime.TicksPerQuarter);
        for (long b = firstBeat; ; b++)
        {
            long beatFrame = (long)Math.Round(tempo.TicksToFramesExact(b * MusicalTime.TicksPerQuarter, SampleRate));
            if (beatFrame >= startFrame + frames)
            {
                break;
            }

            bool accent = ((b % beatsPerBar) + beatsPerBar) % beatsPerBar == 0;
            double frequency = accent ? 1760 : 1180;
            double gain = Volume * (accent ? 0.9 : 0.55);
            long from = Math.Max(beatFrame, startFrame);
            long to = Math.Min(beatFrame + clickLength, startFrame + frames);
            for (long f = from; f < to; f++)
            {
                double t = (f - beatFrame) / (double)SampleRate;
                double envelope = Math.Exp(-t * 90) * Math.Min(1, t * 4000); // kurzer, weicher Einschwingvorgang
                float v = (float)(Math.Sin(2 * Math.PI * frequency * t) * envelope * gain);
                int i = (int)(f - startFrame) * 2;
                stereo[i] += v;
                stereo[i + 1] += v;
            }
        }
    }
}
