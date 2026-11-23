using System.IO;
using System.Text.Json;

namespace MusicWorker.App.Services;

/// <summary>Einstellungen, die zwischen zwei Programmstarts erhalten bleiben.</summary>
public sealed class AppSettings
{
    public List<string> RecentFiles { get; set; } = [];

    public double[]? EqualizerGainsDb { get; set; }

    public double EqualizerPreampDb { get; set; }

    public bool EqualizerPreventClipping { get; set; } = true;

    public bool EqualizerEnabled { get; set; } = true;

    public bool EqualizerVisible { get; set; }
}

/// <summary>Lädt und speichert <see cref="AppSettings"/> unter %APPDATA%\MusicWorker\settings.json.</summary>
public sealed class SettingsService
{
    public const int MaxRecentFiles = 8;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicWorker", "settings.json"))
    {
    }

    public SettingsService(string path)
    {
        _path = path;
        Settings = Load();
    }

    public AppSettings Settings { get; }

    /// <summary>Setzt eine Datei an den Anfang der „Zuletzt geöffnet“-Liste.</summary>
    public void AddRecentFile(string path)
    {
        Settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Settings.RecentFiles.Insert(0, path);
        if (Settings.RecentFiles.Count > MaxRecentFiles)
        {
            Settings.RecentFiles.RemoveRange(MaxRecentFiles, Settings.RecentFiles.Count - MaxRecentFiles);
        }

        Save();
    }

    public void RemoveRecentFile(string path)
    {
        Settings.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Einstellungen sind „nice to have“ – ein Fehler beim Speichern soll nicht stören.
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Beschädigte oder unlesbare Datei → mit Standardwerten starten.
        }

        return new AppSettings();
    }
}
