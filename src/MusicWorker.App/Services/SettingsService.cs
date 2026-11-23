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

    /// <summary>Pro-Kanal-EQ: links und rechts getrennt einstellbar.</summary>
    public bool EqualizerPerChannel { get; set; }

    public double[]? EqualizerRightGainsDb { get; set; }

    public double EqualizerRightPreampDb { get; set; }

    public bool ReverbEnabled { get; set; } = true;

    public double ReverbLeftPercent { get; set; }

    public double ReverbRightPercent { get; set; }

    public double ReverbRoomSizePercent { get; set; } = 50;

    public double ReverbDampingPercent { get; set; } = 50;

    public bool CompressorEnabled { get; set; }

    public double CompressorThresholdDb { get; set; } = -18;

    public double CompressorRatio { get; set; } = 4;

    public double CompressorAttackMs { get; set; } = 10;

    public double CompressorReleaseMs { get; set; } = 150;

    public double CompressorMakeupDb { get; set; }

    public double CompressorMixLeft { get; set; } = 100;

    public double CompressorMixRight { get; set; } = 100;

    public bool CompressorLinked { get; set; } = true;

    public bool DelayEnabled { get; set; }

    public double DelayTimeLeftMs { get; set; } = 375;

    public double DelayTimeRightMs { get; set; } = 375;

    public double DelayMixLeft { get; set; } = 30;

    public double DelayMixRight { get; set; } = 30;

    public double DelayFeedbackPercent { get; set; } = 35;

    public double DelayDampingPercent { get; set; } = 30;

    public bool DelayPingPong { get; set; }

    public bool FilterEnabled { get; set; }

    public double FilterHighPassPosition { get; set; }

    public double FilterLowPassPosition { get; set; } = 100;

    public bool DistortionEnabled { get; set; }

    public int DistortionType { get; set; }

    public double DistortionDrive { get; set; } = 50;

    public double DistortionTone { get; set; } = 70;

    public double DistortionMixLeft { get; set; } = 100;

    public double DistortionMixRight { get; set; } = 100;

    public bool ChorusEnabled { get; set; }

    public double ChorusRate { get; set; } = 0.8;

    public double ChorusDepth { get; set; } = 50;

    public double ChorusMixLeft { get; set; } = 70;

    public double ChorusMixRight { get; set; } = 70;

    public bool FlangerEnabled { get; set; }

    public double FlangerRate { get; set; } = 0.3;

    public double FlangerDepth { get; set; } = 70;

    public double FlangerFeedback { get; set; } = 50;

    public double FlangerMixLeft { get; set; } = 70;

    public double FlangerMixRight { get; set; } = 70;

    /// <summary>Name des gewählten Wiedergabegeräts (null = Windows-Standard).</summary>
    public string? OutputDeviceName { get; set; }

    /// <summary>Gewähltes Aufnahmegerät.</summary>
    public string? InputDeviceName { get; set; }

    /// <summary>Latenzausgleich für Aufnahmen in Millisekunden.</summary>
    public double RecordingLatencyMs { get; set; } = 60;

    /// <summary>Zuletzt geladene SoundFont-Datei (.sf2).</summary>
    public string? LastSoundFont { get; set; }

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
