using System.IO;
using MusicWorker.Core.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MusicWorker.App.Audio;

/// <summary>Lädt Audiodateien in einen <see cref="AudioBuffer"/> und exportiert sie wieder.</summary>
public sealed class AudioFileService
{
    public const string OpenFilter =
        "Audiodateien|*.wav;*.mp3;*.flac;*.aiff;*.aif;*.wma;*.m4a;*.aac|Alle Dateien|*.*";

    public const string ExportFilter = "WAV-Datei (16 Bit)|*.wav|MP3-Datei (192 kbit/s)|*.mp3";

    private static readonly int[] Mp3SampleRates = [32000, 44100, 48000];

    /// <summary>
    /// Lädt WAV, MP3, AIFF sowie alles, was Windows Media Foundation versteht (FLAC, M4A/AAC, WMA …).
    /// </summary>
    public AudioBuffer Load(string path)
    {
        using var reader = new AudioFileReader(path);
        var format = reader.WaveFormat;

        long expectedSamples = reader.Length / sizeof(float);
        var samples = new List<float>((int)Math.Clamp(expectedSamples, 0, int.MaxValue / 2));
        var block = new float[format.SampleRate * format.Channels];

        int read;
        while ((read = reader.Read(block, 0, block.Length)) > 0)
        {
            samples.AddRange(new ArraySegment<float>(block, 0, read));
        }

        int incomplete = samples.Count % format.Channels;
        if (incomplete != 0)
        {
            samples.RemoveRange(samples.Count - incomplete, incomplete);
        }

        return new AudioBuffer(samples.ToArray(), format.SampleRate, format.Channels);
    }

    /// <summary>Exportiert als WAV oder MP3 – je nach Dateiendung.</summary>
    public void Export(AudioBuffer buffer, string path)
    {
        ISampleProvider source = new AudioBufferSampleProvider(buffer, 0, buffer.FrameCount);

        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".mp3":
                ExportMp3(source, path);
                break;
            default:
                WaveFileWriter.CreateWaveFile16(path, source);
                break;
        }
    }

    private static void ExportMp3(ISampleProvider source, string path)
    {
        if (source.WaveFormat.Channels > 2)
        {
            throw new NotSupportedException("MP3 unterstützt höchstens zwei Kanäle (Stereo).");
        }

        if (!Mp3SampleRates.Contains(source.WaveFormat.SampleRate))
        {
            // Der Windows-MP3-Encoder kann nur bestimmte Abtastraten – also vorher umrechnen.
            source = new WdlResamplingSampleProvider(source, 44100);
        }

        try
        {
            MediaFoundationEncoder.EncodeToMp3(new SampleToWaveProvider16(source), path, 192_000);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "Auf diesem System wurde kein MP3-Encoder gefunden. Bitte als WAV exportieren.", ex);
        }
    }
}
