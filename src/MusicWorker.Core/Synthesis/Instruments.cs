using MeltySynth;

namespace MusicWorker.Core.Synthesis;

/// <summary>Die eingebauten Instrumente – sie brauchen keine Dateien und klingen auf jedem Rechner gleich.</summary>
public enum BuiltInInstrument
{
    Piano,
    ElectricPiano,
    Organ,
    Strings,
    Pad,
    Bass,
    Lead,
    Guitar,
    Bells,
    Drums,
}

/// <summary>
/// Ein Klangerzeuger, der Noten live spielt: <see cref="NoteOn"/>/<see cref="NoteOff"/> von jedem Thread,
/// <see cref="Render"/> im Audio-Thread.
/// </summary>
public interface IInstrumentEngine
{
    int SampleRate { get; }

    void NoteOn(int pitch, int velocity);

    void NoteOff(int pitch);

    /// <summary>Alle Noten beenden (mit Ausklang).</summary>
    void AllNotesOff();

    /// <summary>Schreibt die nächsten Frames (Stereo, verschachtelt) nach <paramref name="stereo"/>.</summary>
    void Render(Span<float> stereo);
}

/// <summary>
/// Welches Instrument eine Notenspur spielt: ein eingebautes oder ein Klang aus einer SoundFont-Datei (.sf2).
/// Fehlt die SoundFont-Datei, wird das eingebaute Instrument verwendet.
/// </summary>
public sealed record InstrumentSpec(
    BuiltInInstrument BuiltIn = BuiltInInstrument.Piano,
    string? SoundFontPath = null,
    int Bank = 0,
    int Preset = 0,
    string? PresetName = null)
{
    public bool IsSoundFont => !string.IsNullOrWhiteSpace(SoundFontPath);

    public bool IsDrums => IsSoundFont ? Bank == 128 : BuiltIn == BuiltInInstrument.Drums;

    public string DisplayName => IsSoundFont
        ? $"{PresetName ?? $"Klang {Preset}"} ({Path.GetFileNameWithoutExtension(SoundFontPath)})"
        : Instruments.NameOf(BuiltIn);

    public IInstrumentEngine CreateEngine(int sampleRate)
    {
        if (IsSoundFont && File.Exists(SoundFontPath))
        {
            return new SoundFontEngine(SoundFontLibrary.Load(SoundFontPath!), sampleRate, Bank, Preset);
        }

        return new BuiltInSynth(BuiltIn, sampleRate);
    }
}

/// <summary>Namen und General-MIDI-Zuordnung der eingebauten Instrumente.</summary>
public static class Instruments
{
    public static IReadOnlyList<BuiltInInstrument> All { get; } = Enum.GetValues<BuiltInInstrument>();

    public static string NameOf(BuiltInInstrument instrument) => instrument switch
    {
        BuiltInInstrument.Piano => "Klavier",
        BuiltInInstrument.ElectricPiano => "E-Piano",
        BuiltInInstrument.Organ => "Orgel",
        BuiltInInstrument.Strings => "Streicher",
        BuiltInInstrument.Pad => "Flächenklang",
        BuiltInInstrument.Bass => "Synth-Bass",
        BuiltInInstrument.Lead => "Synth-Lead",
        BuiltInInstrument.Guitar => "Gezupfte Gitarre",
        BuiltInInstrument.Bells => "Glocken",
        _ => "Schlagzeug",
    };

    /// <summary>Passendes eingebautes Instrument für einen General-MIDI-Klang (Kanal 10 = Schlagzeug).</summary>
    public static BuiltInInstrument FromGeneralMidi(int program, int channel)
    {
        if (channel == 9)
        {
            return BuiltInInstrument.Drums;
        }

        return program switch
        {
            4 or 5 => BuiltInInstrument.ElectricPiano,
            < 8 => BuiltInInstrument.Piano,
            < 16 => BuiltInInstrument.Bells,
            < 24 => BuiltInInstrument.Organ,
            < 32 => BuiltInInstrument.Guitar,
            < 40 => BuiltInInstrument.Bass,
            < 52 => BuiltInInstrument.Strings,
            < 56 => BuiltInInstrument.Pad,
            < 88 => BuiltInInstrument.Lead,
            < 104 => BuiltInInstrument.Pad,
            < 112 => BuiltInInstrument.Guitar,
            < 120 => BuiltInInstrument.Bells,
            _ => BuiltInInstrument.Pad,
        };
    }

    /// <summary>General-MIDI-Klang für den MIDI-Export.</summary>
    public static int ToGeneralMidi(BuiltInInstrument instrument) => instrument switch
    {
        BuiltInInstrument.Piano => 0,
        BuiltInInstrument.ElectricPiano => 4,
        BuiltInInstrument.Organ => 16,
        BuiltInInstrument.Strings => 48,
        BuiltInInstrument.Pad => 89,
        BuiltInInstrument.Bass => 38,
        BuiltInInstrument.Lead => 81,
        BuiltInInstrument.Guitar => 24,
        BuiltInInstrument.Bells => 14,
        _ => 0,
    };
}

/// <summary>Lädt SoundFont-Dateien einmal und hält sie im Speicher (sie können groß sein).</summary>
public static class SoundFontLibrary
{
    private static readonly Dictionary<string, SoundFont> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static SoundFont Load(string path)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(path, out var soundFont))
            {
                soundFont = new SoundFont(path);
                Cache[path] = soundFont;
            }

            return soundFont;
        }
    }

    /// <summary>Alle Klänge einer SoundFont (Bank, Nummer, Name), sortiert.</summary>
    public static IReadOnlyList<(int Bank, int Preset, string Name)> Presets(string path) =>
        Load(path).Presets
            .Select(p => (p.BankNumber, p.PatchNumber, p.Name.Trim()))
            .OrderBy(p => p.BankNumber)
            .ThenBy(p => p.PatchNumber)
            .ToArray();
}

/// <summary>Spielt einen Klang aus einer SoundFont (über MeltySynth).</summary>
public sealed class SoundFontEngine : IInstrumentEngine
{
    private readonly Synthesizer _synthesizer;
    private readonly int _channel;
    private readonly object _lock = new();
    private float[] _left = [];
    private float[] _right = [];

    public SoundFontEngine(SoundFont soundFont, int sampleRate, int bank, int preset)
    {
        SampleRate = sampleRate;
        _synthesizer = new Synthesizer(soundFont, new SynthesizerSettings(sampleRate)
        {
            MaximumPolyphony = 96,
            EnableReverbAndChorus = false, // Hall & Co. kommen aus dem Mischpult
        });
        _channel = bank == 128 ? 9 : 0;
        if (_channel == 0)
        {
            _synthesizer.ProcessMidiMessage(0, 0xB0, 0x00, Math.Clamp(bank, 0, 127)); // Bankwahl
        }

        _synthesizer.ProcessMidiMessage(_channel, 0xC0, Math.Clamp(preset, 0, 127), 0);
    }

    public int SampleRate { get; }

    public void NoteOn(int pitch, int velocity)
    {
        lock (_lock)
        {
            _synthesizer.NoteOn(_channel, Math.Clamp(pitch, 0, 127), Math.Clamp(velocity, 1, 127));
        }
    }

    public void NoteOff(int pitch)
    {
        lock (_lock)
        {
            _synthesizer.NoteOff(_channel, Math.Clamp(pitch, 0, 127));
        }
    }

    public void AllNotesOff()
    {
        lock (_lock)
        {
            _synthesizer.NoteOffAll(false);
        }
    }

    public void Render(Span<float> stereo)
    {
        int frames = stereo.Length / 2;
        if (_left.Length < frames)
        {
            _left = new float[frames];
            _right = new float[frames];
        }

        lock (_lock)
        {
            _synthesizer.Render(_left.AsSpan(0, frames), _right.AsSpan(0, frames));
        }

        for (int i = 0; i < frames; i++)
        {
            stereo[2 * i] = _left[i];
            stereo[2 * i + 1] = _right[i];
        }
    }
}
