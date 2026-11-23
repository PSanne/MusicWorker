using MusicWorker.Core.Audio;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Editing;

/// <summary>Ergebnis einer kanalweisen Bearbeitung.</summary>
/// <param name="Buffer">Der neue Puffer (alle Kanäle gleich lang, der kürzere ist am Ende mit Stille aufgefüllt).</param>
/// <param name="ProcessedLength">Länge des längsten bearbeiteten Bereichs (für die Auswahl danach).</param>
/// <param name="ChannelLengths">Länge jedes Kanals vor dem Auffüllen (in Frames).</param>
public sealed record PerChannelResult(AudioBuffer Buffer, int ProcessedLength, IReadOnlyList<int> ChannelLengths)
{
    /// <summary>Wie viele Frames Stille an jeden Kanal angehängt wurden.</summary>
    public IReadOnlyList<int> PaddingFrames => ChannelLengths.Select(l => Buffer.FrameCount - l).ToArray();
}

/// <summary>
/// Wendet auf jeden Kanal einen eigenen Effekt an – auch solche, die die Länge ändern
/// (z. B. Tempo pro Kanal). Weil danach alle Kanäle wieder gleich lang sein müssen, wird der
/// kürzere Kanal <b>am Ende des Stücks</b> mit Stille aufgefüllt.
/// </summary>
public static class PerChannelProcessor
{
    /// <param name="buffer">Ausgangs-Audio.</param>
    /// <param name="range">Bearbeiteter Bereich (gilt für alle Kanäle).</param>
    /// <param name="effects">Ein Effekt pro Kanal; <c>null</c> = Kanal bleibt unverändert.</param>
    public static PerChannelResult Apply(AudioBuffer buffer, SelectionRange range, IReadOnlyList<IAudioEffect?> effects)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(effects);
        if (effects.Count != buffer.Channels)
        {
            throw new ArgumentException("Es wird genau ein Effekt (oder null) pro Kanal erwartet.", nameof(effects));
        }

        range = range.Clamp(buffer.FrameCount);
        int channels = buffer.Channels;
        int tailLength = buffer.FrameCount - range.End;
        var channelData = new float[channels][];
        int processedLength = 0;

        for (int c = 0; c < channels; c++)
        {
            var mono = buffer.GetChannel(c);
            var region = mono.Slice(range.Start, range.Length);
            var processed = effects[c]?.Apply(region) ?? region;
            if (processed.Channels != 1 || processed.SampleRate != buffer.SampleRate)
            {
                throw new InvalidOperationException("Der Effekt hat Kanalanzahl oder Abtastrate verändert.");
            }

            // Kanal = Anfang + bearbeiteter Bereich + Rest (der Rest rückt bei längerem Bereich nach hinten).
            var data = new float[range.Start + processed.FrameCount + tailLength];
            mono.Samples[..range.Start].CopyTo(data);
            processed.Samples.CopyTo(data.AsSpan(range.Start));
            mono.Samples[range.End..].CopyTo(data.AsSpan(range.Start + processed.FrameCount));

            channelData[c] = data;
            processedLength = Math.Max(processedLength, processed.FrameCount);
        }

        // Zusammenführen; kürzere Kanäle behalten am Ende Nullen (= Stille).
        int totalFrames = channelData.Max(d => d.Length);
        var result = new float[totalFrames * channels];
        for (int c = 0; c < channels; c++)
        {
            var data = channelData[c];
            for (int f = 0; f < data.Length; f++)
            {
                result[f * channels + c] = data[f];
            }
        }

        return new PerChannelResult(
            new AudioBuffer(result, buffer.SampleRate, channels),
            processedLength,
            channelData.Select(d => d.Length).ToArray());
    }
}
