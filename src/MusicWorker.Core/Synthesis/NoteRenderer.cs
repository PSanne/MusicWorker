using MusicWorker.Core.Audio;
using MusicWorker.Core.Notes;

namespace MusicWorker.Core.Synthesis;

/// <summary>Rechnet Noten mit einem Instrument in Audio um (für das Mischpult und den Export).</summary>
public static class NoteRenderer
{
    /// <summary>
    /// Rendert <paramref name="notes"/> (Ticks relativ zum Clip-Anfang) mit dem Tempo <paramref name="bpm"/>.
    /// Das Ergebnis ist mindestens <paramref name="minimumFrames"/> lang und enthält danach den Ausklang
    /// (höchstens <paramref name="maxTailSeconds"/>, Stille am Ende wird abgeschnitten).
    /// </summary>
    public static AudioBuffer Render(
        IReadOnlyList<Note> notes,
        InstrumentSpec instrument,
        double bpm,
        int sampleRate,
        long minimumFrames = 0,
        double maxTailSeconds = 3,
        CancellationToken cancellationToken = default) =>
        Render(notes, instrument, TempoMap.Constant(bpm), 0, sampleRate, minimumFrames, maxTailSeconds, cancellationToken);

    /// <summary>
    /// Wie oben, aber mit Tempowechseln: Der Clip beginnt bei <paramref name="clipStartFrame"/> auf der Zeitleiste,
    /// jede Note erklingt dort, wo sie laut <paramref name="tempo"/> hingehört.
    /// </summary>
    public static AudioBuffer Render(
        IReadOnlyList<Note> notes,
        InstrumentSpec instrument,
        TempoMap tempo,
        long clipStartFrame,
        int sampleRate,
        long minimumFrames = 0,
        double maxTailSeconds = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(tempo);
        var engine = instrument.CreateEngine(sampleRate);

        var events = notes
            .SelectMany(n => new[]
            {
                (Frame: tempo.ClipTicksToFrames(clipStartFrame, n.Start, sampleRate), On: true, n.Pitch, n.Velocity),
                (Frame: tempo.ClipTicksToFrames(clipStartFrame, n.End, sampleRate), On: false, n.Pitch, n.Velocity),
            })
            .OrderBy(e => e.Frame)
            .ThenBy(e => e.On) // Note-Aus vor Note-An an derselben Stelle
            .ToArray();

        long lastEvent = events.Length > 0 ? events[^1].Frame : 0;
        long contentEnd = Math.Max(minimumFrames, lastEvent);
        long total = contentEnd + (long)(Math.Max(0, maxTailSeconds) * sampleRate);
        var output = new float[checked((int)Math.Max(1, total) * 2)];

        long position = 0;
        int next = 0;
        const int block = 512;
        while (position < total)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (next < events.Length && events[next].Frame <= position)
            {
                var e = events[next++];
                if (e.On)
                {
                    engine.NoteOn(e.Pitch, e.Velocity);
                }
                else
                {
                    engine.NoteOff(e.Pitch);
                }
            }

            long until = next < events.Length ? Math.Min(events[next].Frame, total) : total;
            int frames = (int)Math.Clamp(until - position, 1, block);
            engine.Render(output.AsSpan((int)position * 2, frames * 2));
            position += frames;
        }

        // Stille nach dem Ausklang abschneiden
        long end = total;
        while (end > contentEnd && Math.Abs(output[(end * 2) - 1]) < 1e-4f && Math.Abs(output[(end * 2) - 2]) < 1e-4f)
        {
            end--;
        }

        end = Math.Max(Math.Max(1, contentEnd), Math.Min(total, end + sampleRate / 50));
        if (end < total)
        {
            Array.Resize(ref output, (int)end * 2);
        }

        return new AudioBuffer(output, sampleRate, 2);
    }
}
