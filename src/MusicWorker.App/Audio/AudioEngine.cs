using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MusicWorker.App.Audio;

/// <summary>Wie das Mischpult mit der Soundkarte spricht.</summary>
public enum AudioDriverKind
{
    /// <summary>Windows-Standard (MME) – funktioniert überall, aber mit spürbarer Verzögerung.</summary>
    Standard,

    /// <summary>WASAPI geteilt – geringere Latenz, andere Programme bleiben hörbar.</summary>
    Wasapi,

    /// <summary>WASAPI exklusiv – sehr geringe Latenz, die Soundkarte gehört dann nur MusicWorker.</summary>
    WasapiExclusive,

    /// <summary>ASIO – Treiber von Audio-Interfaces, geringste Latenz.</summary>
    Asio,
}

/// <summary>Eintrag in der Treiberauswahl.</summary>
public sealed record AudioDriverChoice(AudioDriverKind Kind, string? AsioDriver, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Treiber, Puffergröße und (bei ASIO) die Eingänge.</summary>
public sealed record AudioEngineSettings(AudioDriverKind Kind = AudioDriverKind.Standard, string? AsioDriver = null, int BufferMs = 20)
{
    public static AudioEngineSettings Default { get; } = new();

    public bool IsLowLatency => Kind != AudioDriverKind.Standard;
}

/// <summary>Treiber und Geräte für Wiedergabe und Aufnahme mit geringer Latenz.</summary>
public static class AudioDrivers
{
    /// <summary>Alle nutzbaren Treiber (ASIO nur, wenn ein ASIO-Treiber installiert ist).</summary>
    public static IReadOnlyList<AudioDriverChoice> Available()
    {
        var list = new List<AudioDriverChoice>
        {
            new(AudioDriverKind.Standard, null, "Standard (MME)"),
            new(AudioDriverKind.Wasapi, null, "WASAPI (geringe Latenz)"),
            new(AudioDriverKind.WasapiExclusive, null, "WASAPI exklusiv (sehr gering)"),
        };
        foreach (string name in AsioDriverNames())
        {
            list.Add(new AudioDriverChoice(AudioDriverKind.Asio, name, $"ASIO: {name}"));
        }

        return list;
    }

    public static IReadOnlyList<string> AsioDriverNames()
    {
        try
        {
            return AsioOut.isSupported() ? AsioOut.GetDriverNames() : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Eingänge eines ASIO-Treibers als Auswahl: jeder Eingang einzeln (Mono) und Paare (Stereo).
    /// <see cref="InputDevice.Number"/> ist der erste Eingang (0-basiert).
    /// </summary>
    public static IReadOnlyList<InputDevice> AsioInputs(string driver)
    {
        try
        {
            using var asio = new AsioOut(driver);
            int count = asio.DriverInputChannelCount;
            var list = new List<InputDevice>();
            for (int i = 0; i + 1 < count; i += 2)
            {
                list.Add(new InputDevice(i, $"Eingänge {i + 1}+{i + 2} (Stereo)", 2));
            }

            for (int i = 0; i < count; i++)
            {
                string name = asio.AsioInputChannelName(i);
                list.Add(new InputDevice(i, $"Eingang {i + 1} (Mono){(string.IsNullOrWhiteSpace(name) ? string.Empty : $" – {name}")}", 1));
            }

            return list;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>ASIO-Einstellungen des Treibers öffnen (Puffergröße usw.). Liefert eine Fehlermeldung oder null.</summary>
    public static string? ShowAsioControlPanel(string driver)
    {
        try
        {
            using var asio = new AsioOut(driver);
            asio.ShowControlPanel();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>WASAPI-Wiedergabegerät passend zu einem Gerät aus der MME-Liste (-1 = Standard).</summary>
    public static MMDevice RenderDevice(int waveOutNumber) => Find(DataFlow.Render, waveOutNumber < 0 ? null : SafeName(() => WaveOut.GetCapabilities(waveOutNumber).ProductName));

    /// <summary>WASAPI-Aufnahmegerät passend zu einem Gerät aus der MME-Liste.</summary>
    public static MMDevice CaptureDevice(int waveInNumber) => Find(DataFlow.Capture, waveInNumber < 0 ? null : SafeName(() => WaveInEvent.GetCapabilities(waveInNumber).ProductName));

    /// <summary>Geschätzte Verzögerung von Ein- und Ausgang zusammen (für den Latenzausgleich).</summary>
    public static double EstimatedRoundTripMs(AudioEngineSettings settings) => settings.Kind switch
    {
        AudioDriverKind.Wasapi => (settings.BufferMs * 2) + 10,
        AudioDriverKind.WasapiExclusive => settings.BufferMs * 2,
        AudioDriverKind.Asio => settings.BufferMs * 2,
        _ => 60,
    };

    private static string? SafeName(Func<string> name)
    {
        try
        {
            return name();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static MMDevice Find(DataFlow flow, string? name)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrWhiteSpace(name))
        {
            // MME kürzt Gerätenamen auf 31 Zeichen – deshalb Anfang vergleichen.
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                if (device.FriendlyName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }
        }

        return enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
    }
}
