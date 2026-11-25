using System.IO;
using MusicWorker.Core.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MusicWorker.App.Audio;

/// <summary>Lädt Audiodateien in einen <see cref="AudioBuffer"/> und exportiert sie wieder.</summary>
public sealed class AudioFileService
{
    public const string OpenFilter =
        "Audio- und Videodateien|*.wav;*.mp3;*.flac;*.ogg;*.aiff;*.aif;*.wma;*.m4a;*.aac;*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv|" +
        "Audiodateien|*.wav;*.mp3;*.flac;*.ogg;*.aiff;*.aif;*.wma;*.m4a;*.aac|" +
        "Videos (Tonspur)|*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv|Alle Dateien|*.*";

    /// <summary>Alle Exportformate in der Reihenfolge des Speichern-Dialogs.</summary>
    public static IReadOnlyList<(ExportFormat Format, string Name, string Extension)> ExportFormats { get; } =
    [
        (ExportFormat.Wav16, "WAV (16 Bit, CD-Qualität)", ".wav"),
        (ExportFormat.Wav24, "WAV (24 Bit, Studio)", ".wav"),
        (ExportFormat.Flac16, "FLAC (verlustfrei, 16 Bit – etwa halb so groß wie WAV)", ".flac"),
        (ExportFormat.Flac24, "FLAC (verlustfrei, 24 Bit)", ".flac"),
        (ExportFormat.Mp3, "MP3 (192 kbit/s)", ".mp3"),
        (ExportFormat.M4a, "M4A / AAC (192 kbit/s, z. B. für Apple-Geräte)", ".m4a"),
        (ExportFormat.Ogg, "OGG Vorbis (ca. 160 kbit/s, freies Format)", ".ogg"),
    ];

    /// <summary>Filter für den Speichern-Dialog (Reihenfolge wie <see cref="ExportFormats"/>).</summary>
    public static string ExportFilter { get; } = string.Join("|", ExportFormats.Select(f => $"{f.Name}|*{f.Extension}"));

    private static readonly string[] VideoExtensions = [".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv"];

    private static readonly int[] Mp3SampleRates = [32000, 44100, 48000];

    /// <summary>Nur WAV-Dateien können Marker speichern.</summary>
    public static bool SupportsMarkers(string path) =>
        string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase);

    /// <summary>Format passend zur Dateiendung (für Pfade, bei denen kein Format gewählt wurde).</summary>
    public static ExportFormat FormatFor(string path, ExportFormat preferred)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        var match = ExportFormats.FirstOrDefault(f => f.Format == preferred);
        if (match.Extension == extension)
        {
            return preferred;
        }

        return ExportFormats.FirstOrDefault(f => f.Extension == extension, ExportFormats[0]).Format;
    }

    public static bool IsVideo(string path) => VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// Lädt WAV, MP3, FLAC, OGG, AIFF sowie alles, was Windows Media Foundation versteht (M4A/AAC, WMA …) –
    /// und die Tonspur von Videos (MP4, MOV, WMV, AVI, MKV).
    /// </summary>
    public AudioBuffer Load(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".flac":
                return FlacFile.Read(path);
            case ".ogg":
                return OggVorbisFile.Read(path);
        }

        if (IsVideo(path))
        {
            try
            {
                using var video = new MediaFoundationReader(path);
                return ReadAll(video.ToSampleProvider());
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
            {
                throw new InvalidOperationException(
                    "Die Tonspur dieses Videos konnte nicht gelesen werden. Windows kennt das Format womöglich nicht " +
                    "(MP4 und MOV klappen meist; für MKV kann die kostenlose „Web Media Extensions“ aus dem Microsoft Store helfen).", ex);
            }
        }

        using var reader = new AudioFileReader(path);
        return ReadAll(reader);
    }

    private static AudioBuffer ReadAll(ISampleProvider reader)
    {
        var format = reader.WaveFormat;
        long expectedSamples = reader is AudioFileReader file ? file.Length / sizeof(float) : 0;
        var samples = new List<float>((int)Math.Clamp(expectedSamples, 1024, int.MaxValue / 2));
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

    /// <summary>Exportiert im Format passend zur Dateiendung (WAV wird mit 16 Bit geschrieben).</summary>
    public void Export(AudioBuffer buffer, string path) => Export(buffer, path, FormatFor(path, ExportFormat.Wav16));

    /// <summary>Exportiert im gewählten Format.</summary>
    public void Export(AudioBuffer buffer, string path, ExportFormat format)
    {
        ISampleProvider source = new AudioBufferSampleProvider(buffer, 0, buffer.FrameCount);
        switch (format)
        {
            case ExportFormat.Wav24:
                WaveFileWriter.CreateWaveFile(path, new SampleToWaveProvider24(source));
                break;
            case ExportFormat.Flac16:
                FlacFile.Write(buffer, path, 16);
                break;
            case ExportFormat.Flac24:
                FlacFile.Write(buffer, path, 24);
                break;
            case ExportFormat.Mp3:
                ExportMp3(source, path);
                break;
            case ExportFormat.M4a:
                ExportAac(buffer, path);
                break;
            case ExportFormat.Ogg:
                OggVorbisFile.Write(buffer, path);
                break;
            default:
                WaveFileWriter.CreateWaveFile16(path, source);
                break;
        }
    }

    /// <summary>Speichert verlustfrei als 32-Bit-Float-WAV (für Projekt-Audiodateien).</summary>
    public void SaveLossless(AudioBuffer buffer, string path)
    {
        var source = new AudioBufferSampleProvider(buffer, 0, buffer.FrameCount);
        WaveFileWriter.CreateWaveFile(path, new SampleToWaveProvider(source));
    }

    private static void ExportAac(AudioBuffer buffer, string path)
    {
        if (buffer.Channels > 2)
        {
            throw new NotSupportedException("M4A/AAC wird hier mit höchstens zwei Kanälen (Stereo) geschrieben.");
        }

        if (buffer.SampleRate is not (44100 or 48000))
        {
            buffer = AudioConversion.Resample(buffer, buffer.SampleRate > 44100 ? 48000 : 44100);
        }

        var source = new AudioBufferSampleProvider(buffer, 0, buffer.FrameCount);
        try
        {
            MediaFoundationEncoder.EncodeToAac(new SampleToWaveProvider16(source), path, 192_000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            throw new InvalidOperationException(
                "Auf diesem System wurde kein AAC-Encoder gefunden. Bitte als MP3, OGG oder FLAC exportieren.", ex);
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

/// <summary>Exportformate.</summary>
public enum ExportFormat
{
    Wav16,
    Wav24,
    Flac16,
    Flac24,
    Mp3,
    M4a,
    Ogg,
}
