using NVorbis;
using OggVorbisEncoder;

namespace MusicWorker.Core.Audio;

/// <summary>
/// OGG Vorbis – freies, verlustbehaftetes Format (ähnlich MP3, bei gleicher Größe meist besser klingend).
/// Kodiert mit „.NET Ogg Vorbis Encoder“, gelesen mit „NVorbis“ (beide MIT-Lizenz, reines C#).
/// </summary>
public static class OggVorbisFile
{
    private const int WriteChunk = 1024;

    /// <summary>Abtastraten, die der Kodierer zuverlässig beherrscht; andere werden vorher umgerechnet.</summary>
    public static IReadOnlyList<int> SupportedSampleRates { get; } = [44100, 48000];

    /// <summary>
    /// Qualitätsstufe des Kodierers (etwa 160 kbit/s, sehr gute Qualität). Die verwendete Bibliothek arbeitet nur in
    /// diesem Bereich und nur in Stereo zuverlässig – das ist durch Tests abgesichert.
    /// </summary>
    public const float Quality = 0.5f;

    public static void Write(AudioBuffer buffer, string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        Write(buffer, stream);
    }

    /// <summary>Schreibt OGG Vorbis (Stereo; Mono wird auf beide Kanäle verteilt).</summary>
    public static void Write(AudioBuffer buffer, Stream output)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Channels > 2)
        {
            throw new NotSupportedException("OGG Vorbis wird hier mit Mono oder Stereo geschrieben.");
        }

        if (buffer.Channels == 1)
        {
            buffer = AudioConversion.ToStereo(buffer);
        }

        if (!SupportedSampleRates.Contains(buffer.SampleRate))
        {
            buffer = AudioConversion.Resample(buffer, buffer.SampleRate > 44100 ? 48000 : 44100);
        }

        int channels = buffer.Channels;
        int frames = buffer.FrameCount;
        var pages = new List<(byte[] Header, byte[] Body)>();

        // Der Kodierer verschluckt die ersten 1024 Samples (Vorlauf). Deshalb wird genau so viel Stille vorangestellt –
        // dekodiert hat die Datei dann wieder exakt die ursprüngliche Länge und Lage.
        const int priming = 1024;
        int paddedFrames = frames + priming;
        var info = VorbisInfo.InitVariableBitRate(channels, buffer.SampleRate, Quality);
        var ogg = new OggStream(new Random().Next());
        var comments = new Comments();
        comments.AddTag("ENCODER", "MusicWorker");
        ogg.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        ogg.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
        ogg.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));
        FlushPages(ogg, pages, force: true);

        var state = ProcessingState.Create(info);

        // Ganze Kanäle (mit Stille davor) einmal getrennt ablegen – der Kodierer liest daraus mit Versatz.
        var planar = new float[channels][];
        var samples = buffer.Samples;
        for (int c = 0; c < channels; c++)
        {
            planar[c] = new float[paddedFrames];
            for (int f = 0; f < frames; f++)
            {
                planar[c][priming + f] = samples[f * channels + c];
            }
        }

        int offset = 0;
        bool ended = false;
        while (!ended)
        {
            if (offset >= paddedFrames)
            {
                state.WriteEndOfStream();
                ended = true;
            }
            else
            {
                int n = Math.Min(WriteChunk, paddedFrames - offset);
                state.WriteData(planar, n, offset);
                offset += n;
            }

            while (!ogg.Finished && state.PacketOut(out var packet))
            {
                ogg.PacketIn(packet);
                FlushPages(ogg, pages, force: false);
            }
        }

        FlushPages(ogg, pages, force: true);
        foreach (var (header, body) in pages)
        {
            output.Write(header, 0, header.Length);
            output.Write(body, 0, body.Length);
        }
    }

    public static AudioBuffer Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream);
    }

    public static AudioBuffer Read(Stream stream)
    {
        using var reader = new VorbisReader(stream, closeOnDispose: false);
        int channels = reader.Channels;
        var samples = new List<float>(reader.TotalSamples > 0 ? (int)Math.Min(reader.TotalSamples * channels, int.MaxValue / 2) : 1 << 20);
        var block = new float[channels * 4096];
        int read;
        while ((read = reader.ReadSamples(block, 0, block.Length)) > 0)
        {
            samples.AddRange(new ArraySegment<float>(block, 0, read - read % channels));
        }

        return new AudioBuffer(samples.ToArray(), reader.SampleRate, channels);
    }

    private static void FlushPages(OggStream ogg, List<(byte[] Header, byte[] Body)> pages, bool force)
    {
        while (ogg.PageOut(out var page, force))
        {
            pages.Add((page.Header.ToArray(), page.Body.ToArray()));
        }
    }
}

/// <summary>Formatumwandlungen für den Export.</summary>
public static class AudioConversion
{
    /// <summary>Rechnet auf eine andere Abtastrate um (mit Tiefpass gegen Aliasing beim Verringern).</summary>
    public static AudioBuffer Resample(AudioBuffer input, int targetRate)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.SampleRate == targetRate || input.IsEmpty)
        {
            return input.SampleRate == targetRate ? input : new AudioBuffer([], targetRate, input.Channels);
        }

        var changed = Dsp.Resampler.ChangeSpeed(input, input.SampleRate / (double)targetRate);
        return new AudioBuffer(changed.Samples.ToArray(), targetRate, input.Channels);
    }

    /// <summary>Mono auf zwei gleiche Kanäle verteilen.</summary>
    public static AudioBuffer ToStereo(AudioBuffer mono)
    {
        ArgumentNullException.ThrowIfNull(mono);
        if (mono.Channels != 1)
        {
            return mono;
        }

        var samples = new float[mono.FrameCount * 2];
        var source = mono.Samples;
        for (int f = 0; f < mono.FrameCount; f++)
        {
            samples[2 * f] = samples[2 * f + 1] = source[f];
        }

        return new AudioBuffer(samples, mono.SampleRate, 2);
    }
}
