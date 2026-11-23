using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>Ein Wiedergabegerät (Soundkarte, Kopfhörer, Lautsprecher …).</summary>
public sealed record AudioDevice(int Number, string Name);

/// <summary>Listet die Wiedergabegeräte von Windows auf.</summary>
public static class AudioDevices
{
    public const string DefaultName = "Windows-Standardgerät";

    /// <summary>Alle Geräte, beginnend mit dem Windows-Standardgerät (Nummer -1).</summary>
    public static IReadOnlyList<AudioDevice> List()
    {
        var devices = new List<AudioDevice> { new(-1, DefaultName) };
        try
        {
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                devices.Add(new AudioDevice(i, WaveOut.GetCapabilities(i).ProductName));
            }
        }
        catch (Exception)
        {
            // Ohne Zugriff auf die Geräteliste bleibt das Standardgerät.
        }

        return devices;
    }
}
