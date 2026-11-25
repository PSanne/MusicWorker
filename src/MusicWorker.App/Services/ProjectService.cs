using System.IO;
using System.Text.Json;
using MusicWorker.App.Audio;
using MusicWorker.Core.Audio;

namespace MusicWorker.App.Services;

/// <summary>Inhalt einer Projektdatei (.mwproj) – Einstellungen als JSON, Audio als eigene WAV-Dateien.</summary>
public sealed class ProjectFile
{
    public int Version { get; set; } = 1;

    public int SampleRate { get; set; } = 44100;

    public double MasterDb { get; set; }

    public double MasterEqLow { get; set; }

    public double MasterEqMid { get; set; }

    public double MasterEqHigh { get; set; }

    public double MasterCompression { get; set; }

    public bool MasterLimiter { get; set; } = true;

    public double Bpm { get; set; } = 120;

    public int BeatsPerBar { get; set; } = 4;

    public bool Snap { get; set; }

    public long LoopStart { get; set; }

    public long LoopEnd { get; set; }

    public List<ProjectTrack> Tracks { get; set; } = [];

    public List<ProjectGroup> Groups { get; set; } = [];

    public List<ProjectBus> Buses { get; set; } = [];

    /// <summary>Marker im Projekt (z. B. „Strophe“, „Refrain“).</summary>
    public List<ProjectMarker> Markers { get; set; } = [];

    /// <summary>Nur in automatischen Sicherungen: wo das Projekt ursprünglich gespeichert war.</summary>
    public string? OriginalPath { get; set; }
}

public sealed class ProjectMarker
{
    public string Name { get; set; } = "Marker";

    public long Frame { get; set; }
}

public sealed class ProjectTrack
{
    public string Name { get; set; } = "Spur";

    public double VolumeDb { get; set; }

    public double Pan { get; set; }

    public bool Mute { get; set; }

    public bool Solo { get; set; }

    public double SendHall { get; set; }

    public double SendEcho { get; set; }

    public int GroupIndex { get; set; } = -1;

    public double EqLow { get; set; }

    public double EqMid { get; set; }

    public double EqHigh { get; set; }

    public double HighPass { get; set; } = 20;

    public double Compression { get; set; }

    /// <summary>Index der Spur, die diese Spur absenkt (-1 = keine).</summary>
    public int DuckSource { get; set; } = -1;

    public double DuckAmount { get; set; } = 12;

    public double DuckThreshold { get; set; } = -40;

    public string ShownAutomation { get; set; } = "Volume";

    public List<ProjectAutomation> Automation { get; set; } = [];

    /// <summary>Eingebautes Instrument einer Notenspur (z. B. „Piano“); null = Audiospur.</summary>
    public string? Instrument { get; set; }

    public string? SoundFont { get; set; }

    public int Bank { get; set; }

    public int Preset { get; set; }

    public string? PresetName { get; set; }

    public List<ProjectClip> Clips { get; set; } = [];
}

/// <summary>Eine Hüllkurve: Ziel (z. B. „Volume“) und ihre Punkte.</summary>
public sealed class ProjectAutomation
{
    public string Target { get; set; } = "Volume";

    public List<long> Frames { get; set; } = [];

    public List<double> Values { get; set; } = [];
}

public sealed class ProjectClip
{
    /// <summary>Dateiname im Audio-Ordner des Projekts.</summary>
    public string File { get; set; } = string.Empty;

    public long Start { get; set; }

    /// <summary>Noten eines Notenclips: je Note [Tonhöhe, Beginn, Länge, Anschlag, Stimme]; null = Audioclip.</summary>
    public List<long[]>? Notes { get; set; }

    public long LengthTicks { get; set; }

    /// <summary>Dateinamen der Takes (null = keine Takes); die Zusammenstellung steht in <see cref="Comp"/>.</summary>
    public List<string>? Takes { get; set; }

    /// <summary>Zusammenstellung: je Abschnitt [Anfang, Ende, Take].</summary>
    public List<long[]>? Comp { get; set; }
}

public sealed class ProjectGroup
{
    public string Name { get; set; } = "Gruppe";

    public double VolumeDb { get; set; }

    public bool Mute { get; set; }
}

public sealed class ProjectBus
{
    public string Name { get; set; } = "Bus";

    public double ReturnDb { get; set; }

    public double RoomSize { get; set; } = 60;

    public double Damping { get; set; } = 40;

    public double TimeLeft { get; set; } = 375;

    public double TimeRight { get; set; } = 500;

    public double Feedback { get; set; } = 35;

    public bool PingPong { get; set; }
}

/// <summary>
/// Liest und schreibt Projekte: <c>Name.mwproj</c> (JSON) und daneben den Ordner <c>Name-Audio</c>
/// mit den Clips als verlustfreie 32-Bit-Float-WAV-Dateien.
/// </summary>
public sealed class ProjectService
{
    public const string Filter = "MusicWorker-Projekt (*.mwproj)|*.mwproj";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly AudioFileService _files;

    public ProjectService(AudioFileService files)
    {
        _files = files;
    }

    /// <summary>Ordner für die Audiodateien eines Projekts.</summary>
    public static string AudioFolderFor(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(projectPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(projectPath) + "-Audio");

    public ProjectFile Read(string projectPath) =>
        JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(projectPath), JsonOptions)
        ?? throw new InvalidDataException("Die Projektdatei ist leer oder beschädigt.");

    public void Write(string projectPath, ProjectFile project) =>
        File.WriteAllText(projectPath, JsonSerializer.Serialize(project, JsonOptions));

    /// <summary>Schreibt erst eine Hilfsdatei und ersetzt dann die alte – nie eine halb geschriebene Projektdatei.</summary>
    public void WriteAtomic(string projectPath, ProjectFile project) =>
        MusicWorker.Core.Persistence.AtomicFile.WriteAllText(projectPath, JsonSerializer.Serialize(project, JsonOptions));

    public AudioBuffer LoadClip(string projectPath, string fileName) =>
        _files.Load(Path.Combine(AudioFolderFor(projectPath), fileName));

    public bool ClipExists(string projectPath, string fileName) =>
        File.Exists(Path.Combine(AudioFolderFor(projectPath), fileName));

    public void SaveClip(string projectPath, string fileName, AudioBuffer audio)
    {
        string folder = AudioFolderFor(projectPath);
        Directory.CreateDirectory(folder);
        _files.SaveLossless(audio, Path.Combine(folder, fileName));
    }

    /// <summary>Löscht WAV-Dateien im Audio-Ordner, die das Projekt nicht mehr verwendet.</summary>
    public void RemoveUnusedAudio(string projectPath, IEnumerable<string> usedFiles)
    {
        string folder = AudioFolderFor(projectPath);
        if (!Directory.Exists(folder))
        {
            return;
        }

        var used = new HashSet<string>(usedFiles, StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(folder, "*.wav"))
        {
            if (!used.Contains(Path.GetFileName(file)))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // Wird evtl. gerade verwendet – beim nächsten Speichern erneut versuchen.
                }
            }
        }
    }
}
