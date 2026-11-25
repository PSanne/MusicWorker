using System.Text.Json;

namespace MusicWorker.Core.Persistence;

/// <summary>Kennzeichnet eine laufende Programmsitzung (Prozess-ID plus Startzeit, damit wiederverwendete IDs nicht täuschen).</summary>
public sealed record SessionInfo(int ProcessId, long StartTicks);

/// <summary>Eine Sitzung, die nicht ordnungsgemäß beendet wurde und gesicherte Arbeit hinterlassen hat.</summary>
public sealed record AbandonedSession(string Folder, DateTime SavedAt, bool HasEditor, bool HasMixer);

/// <summary>
/// Ordner für die automatische Sicherung: Jede laufende Sitzung bekommt einen eigenen Unterordner mit
/// <c>session.json</c>. Wird das Programm normal beendet, verschwindet der Ordner. Bleibt er nach einem
/// Absturz liegen, kann die Arbeit beim nächsten Start wiederhergestellt werden.
/// </summary>
public sealed class RecoveryStore
{
    public const string SessionFile = "session.json";
    public const string EditorFolderName = "Editor";
    public const string MixerFolderName = "Mischpult";

    public RecoveryStore(string root)
    {
        Root = root;
    }

    public string Root { get; }

    /// <summary>Legt den Ordner für die aktuelle Sitzung an.</summary>
    public RecoverySession StartSession(SessionInfo session)
    {
        string folder = Path.Combine(Root, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, SessionFile), JsonSerializer.Serialize(session));
        return new RecoverySession(folder);
    }

    /// <summary>
    /// Sucht Sitzungen, deren Programm nicht mehr läuft und die gesicherte Arbeit enthalten.
    /// Leere, verwaiste Ordner werden dabei gleich aufgeräumt.
    /// </summary>
    public IReadOnlyList<AbandonedSession> FindAbandoned(Func<SessionInfo, bool> isRunning)
    {
        ArgumentNullException.ThrowIfNull(isRunning);
        var result = new List<AbandonedSession>();
        if (!Directory.Exists(Root))
        {
            return result;
        }

        foreach (string folder in Directory.EnumerateDirectories(Root))
        {
            SessionInfo? session = null;
            try
            {
                string file = Path.Combine(folder, SessionFile);
                if (File.Exists(file))
                {
                    session = JsonSerializer.Deserialize<SessionInfo>(File.ReadAllText(file));
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                session = null;
            }

            if (session is not null && isRunning(session))
            {
                continue; // gehört zu einem anderen, noch laufenden MusicWorker-Fenster
            }

            bool hasEditor = File.Exists(Path.Combine(folder, EditorFolderName, EditorRecovery.InfoFile));
            bool hasMixer = Directory.Exists(Path.Combine(folder, MixerFolderName)) &&
                Directory.EnumerateFiles(Path.Combine(folder, MixerFolderName), "*.mwproj").Any();
            if (!hasEditor && !hasMixer)
            {
                Delete(folder);
                continue;
            }

            var savedAt = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTime)
                .DefaultIfEmpty(Directory.GetLastWriteTime(folder))
                .Max();
            result.Add(new AbandonedSession(folder, savedAt, hasEditor, hasMixer));
        }

        return result.OrderByDescending(s => s.SavedAt).ToArray();
    }

    public static void Delete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Eine Datei ist evtl. noch geöffnet – beim nächsten Start erneut versuchen.
        }
    }
}

/// <summary>Der Sicherungsordner der laufenden Sitzung.</summary>
public sealed class RecoverySession(string folder)
{
    public string Folder { get; } = folder;

    public string EditorFolder => Path.Combine(Folder, RecoveryStore.EditorFolderName);

    public string MixerFolder => Path.Combine(Folder, RecoveryStore.MixerFolderName);

    /// <summary>Normales Programmende: Sicherung wird nicht mehr gebraucht.</summary>
    public void End() => RecoveryStore.Delete(Folder);

    /// <summary>Sicherung eines Bereichs verwerfen (z. B. nach dem Speichern).</summary>
    public void ClearEditor() => RecoveryStore.Delete(EditorFolder);

    public void ClearMixer() => RecoveryStore.Delete(MixerFolder);
}

/// <summary>Beschreibt die Sicherung des Editors: Audio liegt als <see cref="AudioFile"/> daneben.</summary>
public sealed class EditorRecovery
{
    public const string InfoFile = "editor.json";

    /// <summary>Datei, aus der das Audio ursprünglich stammt (null = neu bzw. aus dem Mischpult).</summary>
    public string? OriginalPath { get; set; }

    /// <summary>Anzeigename im Fenstertitel.</summary>
    public string DisplayName { get; set; } = "Unbenannt";

    /// <summary>Dateiname der gesicherten WAV-Datei im selben Ordner.</summary>
    public string AudioFile { get; set; } = string.Empty;

    public List<RecoveryMarker> Markers { get; set; } = [];

    public static EditorRecovery? Read(string folder)
    {
        try
        {
            string path = Path.Combine(folder, InfoFile);
            return File.Exists(path) ? JsonSerializer.Deserialize<EditorRecovery>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Schreibt die Beschreibung so, dass eine halb geschriebene Datei nie eine gültige ersetzt.</summary>
    public void Write(string folder) =>
        AtomicFile.WriteAllText(Path.Combine(folder, InfoFile), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}

public sealed class RecoveryMarker
{
    public string Name { get; set; } = string.Empty;

    public long Frame { get; set; }
}

/// <summary>Schreibt Dateien erst unter einem Hilfsnamen und ersetzt dann die alte Datei in einem Schritt.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}
