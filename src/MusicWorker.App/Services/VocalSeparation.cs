using System.Formats.Tar;
using System.IO;
using System.Net.Http;
using ICSharpCode.SharpZipLib.BZip2;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Effects;
using SherpaOnnx;

namespace MusicWorker.App.Services;

/// <summary>Was die KI-Trennung liefert.</summary>
public enum Stem
{
    Vocals,
    Accompaniment,
}

/// <summary>
/// Gesang und Begleitung per KI trennen (Spleeter-Modell von Deezer, MIT-Lizenz, ausgeführt mit sherpa-onnx).
/// Das Modell (ca. 35 MB) wird beim ersten Benutzen einmalig heruntergeladen und lokal gespeichert –
/// die Trennung selbst läuft komplett auf dem eigenen Rechner.
/// </summary>
public sealed class VocalSeparation
{
    public const string ModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/source-separation-models/sherpa-onnx-spleeter-2stems-fp16.tar.bz2";

    /// <summary>Ungefähre Downloadgröße für den Hinweis an den Nutzer.</summary>
    public const int ModelMegabytes = 35;

    private const int ModelRate = 44100;
    private const int ChunkSeconds = 30;
    private const int ContextSeconds = 1;

    private static readonly Lazy<VocalSeparation> Shared = new(() => new VocalSeparation(DefaultFolder));

    private readonly object _lock = new();
    private OfflineSourceSeparation? _engine;

    public VocalSeparation(string modelFolder)
    {
        ModelFolder = modelFolder;
    }

    public static VocalSeparation Default => Shared.Value;

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicWorker", "models", "spleeter-2stems");

    public string ModelFolder { get; }

    private string VocalsModel => Path.Combine(ModelFolder, "vocals.fp16.onnx");

    private string AccompanimentModel => Path.Combine(ModelFolder, "accompaniment.fp16.onnx");

    public bool IsModelInstalled => File.Exists(VocalsModel) && File.Exists(AccompanimentModel)
        && new FileInfo(VocalsModel).Length > 1_000_000 && new FileInfo(AccompanimentModel).Length > 1_000_000;

    /// <summary>Lädt das Modell herunter und entpackt es (Fortschritt 0 … 1).</summary>
    public async Task DownloadModelAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ModelFolder);
        string archive = Path.Combine(ModelFolder, "download.tar.bz2");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MusicWorker");
            using var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? ModelMegabytes * 1_000_000L;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = File.Create(archive);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                done += read;
                progress?.Report(Math.Min(0.95, done / (double)total * 0.95));
            }
        }

        await Task.Run(() => Extract(archive), cancellationToken).ConfigureAwait(false);
        File.Delete(archive);
        progress?.Report(1);
        if (!IsModelInstalled)
        {
            throw new InvalidDataException("Das heruntergeladene Modell ist unvollständig.");
        }
    }

    private void Extract(string archive)
    {
        using var file = File.OpenRead(archive);
        using var bzip = new BZip2InputStream(file);
        using var tar = new TarReader(bzip);
        while (tar.GetNextEntry() is { } entry)
        {
            string name = Path.GetFileName(entry.Name);
            if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile && name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
            {
                entry.ExtractToFile(Path.Combine(ModelFolder, name), overwrite: true);
            }
        }
    }

    /// <summary>
    /// Trennt <paramref name="input"/> in Gesang und Begleitung (gleiche Länge, Abtastrate und Kanalzahl wie das Original).
    /// Lange Aufnahmen werden in Abschnitten mit etwas Kontext davor und danach verarbeitet.
    /// </summary>
    public (AudioBuffer Vocals, AudioBuffer Accompaniment) Separate(AudioBuffer input, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!IsModelInstalled)
        {
            throw new InvalidOperationException("Das KI-Modell ist noch nicht installiert.");
        }

        var stereo = AudioConversion.ToStereo(input.Channels > 2 ? Downmix(input) : input);
        var audio = AudioConversion.Resample(stereo, ModelRate);
        int frames = audio.FrameCount;
        var vocals = new float[frames * 2];
        var accompaniment = new float[frames * 2];
        var weights = new float[frames];
        int chunk = ChunkSeconds * ModelRate;
        int context = ContextSeconds * ModelRate;
        var engine = Engine();
        for (int start = 0; start < frames; start += chunk)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int end = Math.Min(frames, start + chunk);
            int from = Math.Max(0, start - context);
            int to = Math.Min(frames, end + context);
            int pad = ModelRate / 2; // Stille am Rand vermeidet Knackser am Anfang und Ende
            var left = new float[(to - from) + (2 * pad)];
            var right = new float[left.Length];
            for (int f = from; f < to; f++)
            {
                left[f - from + pad] = audio.GetSample(f, 0);
                right[f - from + pad] = audio.GetSample(f, 1);
            }

            float[][] v;
            float[][] a;
            lock (_lock)
            {
                using var output = engine.Process([left, right], ModelRate);
                v = output.GetStemSamples(0);
                a = output.GetStemSamples(1);
            }

            // Überlappende Abschnitte weich überblenden: im eigenen Abschnitt Gewicht 1, im Kontext linear auslaufend.
            for (int f = from; f < to; f++)
            {
                float w = f < start ? (f - from + 1) / (float)(start - from + 1)
                    : f >= end ? (to - f) / (float)(to - end + 1)
                    : 1f;
                int i = f - from + pad;
                vocals[f * 2] += w * Get(v[0], i);
                vocals[(f * 2) + 1] += w * Get(v[Math.Min(1, v.Length - 1)], i);
                accompaniment[f * 2] += w * Get(a[0], i);
                accompaniment[(f * 2) + 1] += w * Get(a[Math.Min(1, a.Length - 1)], i);
                weights[f] += w;
            }

            progress?.Report(Math.Min(1, end / (double)frames));
        }

        for (int f = 0; f < frames; f++)
        {
            float w = weights[f] > 0 ? 1 / weights[f] : 0;
            vocals[f * 2] *= w;
            vocals[(f * 2) + 1] *= w;
            accompaniment[f * 2] *= w;
            accompaniment[(f * 2) + 1] *= w;
        }

        return (Back(vocals, input), Back(accompaniment, input));
    }

    private static float Get(float[] data, int index) => index >= 0 && index < data.Length ? data[index] : 0;

    /// <summary>Zurück auf Abtastrate, Länge und Kanalzahl des Originals.</summary>
    private static AudioBuffer Back(float[] stereo, AudioBuffer original)
    {
        var buffer = AudioConversion.Resample(new AudioBuffer(stereo, ModelRate, 2), original.SampleRate);
        int frames = original.FrameCount;
        var samples = new float[frames * original.Channels];
        for (int f = 0; f < frames; f++)
        {
            int source = Math.Min(f, buffer.FrameCount - 1);
            float l = source >= 0 ? buffer.GetSample(source, 0) : 0;
            float r = source >= 0 ? buffer.GetSample(source, 1) : 0;
            if (original.Channels == 1)
            {
                samples[f] = (l + r) * 0.5f;
            }
            else
            {
                samples[f * original.Channels] = l;
                samples[(f * original.Channels) + 1] = r;
            }
        }

        return new AudioBuffer(samples, original.SampleRate, original.Channels);
    }

    private static AudioBuffer Downmix(AudioBuffer input)
    {
        var samples = new float[input.FrameCount * 2];
        for (int f = 0; f < input.FrameCount; f++)
        {
            samples[f * 2] = input.GetSample(f, 0);
            samples[(f * 2) + 1] = input.GetSample(f, 1);
        }

        return new AudioBuffer(samples, input.SampleRate, 2);
    }

    private OfflineSourceSeparation Engine()
    {
        lock (_lock)
        {
            if (_engine is null)
            {
                var config = new OfflineSourceSeparationConfig();
                config.Model.Spleeter.Vocals = VocalsModel;
                config.Model.Spleeter.Accompaniment = AccompanimentModel;
                config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
                config.Model.Provider = "cpu";
                _engine = new OfflineSourceSeparation(config);
            }

            return _engine;
        }
    }
}

/// <summary>Effekt für den Editor: behält nur den Gesang bzw. nur die Begleitung (wirkt auf die Auswahl).</summary>
public sealed class KeepStemEffect(VocalSeparation separation, Stem stem) : IAudioEffect
{
    public string Name => stem == Stem.Vocals ? "Nur Gesang (KI)" : "Nur Begleitung (KI, Karaoke)";

    public AudioBuffer Apply(AudioBuffer input)
    {
        var (vocals, accompaniment) = separation.Separate(input);
        return stem == Stem.Vocals ? vocals : accompaniment;
    }
}
