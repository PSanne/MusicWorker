using System.IO;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Services;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Persistence;

namespace MusicWorker.App.ViewModels;

/// <summary>Eintrag in einem Preset-Menü (laden, speichern, löschen).</summary>
public sealed record PresetMenuItem(string Header, ICommand Command, object? Parameter);

/// <summary>Was geladen werden soll: Effekt (bzw. „Klang“ für alle) und Reglerwerte.</summary>
public sealed record EffectPresetChoice(string Effect, string Name, IReadOnlyDictionary<string, double> Values);

/// <summary>
/// Presets: fertige und eigene Einstellungen für jeden Live-Effekt – und „Klang-Presets“, die alle Effekte auf
/// einmal einstellen (z. B. „Podcast-Stimme“). Eigene Presets liegen in %APPDATA%\MusicWorker\presets.json.
/// </summary>
public sealed partial class MainViewModel
{
    public const string ChainPreset = "Klang";

    private static readonly (string Key, string Display)[] PresetEffectNames =
    [
        ("Equalizer", "Equalizer"), ("Hall", "Hall"), ("Kompressor", "Kompressor"), ("Echo", "Echo"), ("Filter", "Filter"),
        ("Verzerrung", "Verzerrung"), ("Chorus", "Chorus"), ("Flanger", "Flanger"), ("Gate", "Noise-Gate"),
        ("Saettigung", "Bandsättigung"), ("Phaser", "Phaser"), ("Tremolo", "Tremolo"), ("Breite", "Stereo-Breite"),
    ];

    private PresetStore? _presetStore;
    private Dictionary<string, PresetParameter[]>? _presetParameters;

    private sealed record PresetParameter(string Key, Func<double> Get, Action<double> Set);

    private PresetStore PresetStore
    {
        get
        {
            if (_presetStore is null)
            {
                _presetStore = new PresetStore(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MusicWorker", "presets.json"));
                _presetStore.Changed += (_, _) => OnPropertyChanged(nameof(PresetMenus));
            }

            return _presetStore;
        }
    }

    /// <summary>Menüeinträge je Effekt (Schlüssel wie „Hall“, „Kompressor“ … und „Klang“ für alle).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PresetMenuItem>> PresetMenus =>
        PresetEffectNames.Select(e => e.Key).Append(ChainPreset).ToDictionary(key => key, BuildPresetMenu);

    private IReadOnlyList<PresetMenuItem> BuildPresetMenu(string effect)
    {
        var items = new List<PresetMenuItem>();
        foreach (var (name, values) in BuiltInPresets(effect))
        {
            items.Add(new PresetMenuItem(name, ApplyPresetCommand, new EffectPresetChoice(effect, name, values)));
        }

        foreach (var preset in PresetStore.For(effect))
        {
            items.Add(new PresetMenuItem("★ " + preset.Name, ApplyPresetCommand, new EffectPresetChoice(effect, preset.Name, preset.Values)));
        }

        items.Add(new PresetMenuItem("Aktuelle Einstellung als Preset speichern …", SavePresetCommand, effect));
        if (PresetStore.For(effect).Count > 0)
        {
            items.Add(new PresetMenuItem("Eigenes Preset löschen …", DeletePresetCommand, effect));
        }

        return items;
    }

    [RelayCommand]
    private void ApplyPreset(EffectPresetChoice? choice)
    {
        if (choice is null)
        {
            return;
        }

        var map = PresetParameterMap;
        if (choice.Effect == ChainPreset)
        {
            // Klang-Preset: erst alles aus und den EQ gerade stellen, dann die Werte des Presets setzen.
            foreach (var (key, _) in PresetEffectNames)
            {
                Set(map[key], "On", 0);
            }

            foreach (var band in Equalizer.Bands)
            {
                band.GainDb = 0;
            }

            foreach (var (fullKey, value) in choice.Values)
            {
                int dot = fullKey.IndexOf('.');
                if (dot > 0 && map.TryGetValue(fullKey[..dot], out var parameters))
                {
                    Set(parameters, fullKey[(dot + 1)..], value);
                }
            }
        }
        else if (map.TryGetValue(choice.Effect, out var parameters))
        {
            foreach (var (key, value) in choice.Values)
            {
                Set(parameters, key, value);
            }

            if (!choice.Values.ContainsKey("On"))
            {
                Set(parameters, "On", 1); // ein Preset soll man sofort hören
            }
        }

        if (!IsEqualizerVisible)
        {
            IsEqualizerVisible = true; // Effekte einblenden, damit man sieht, was sich geändert hat
        }

        StatusText = $"Preset „{choice.Name}“ geladen.";

        static void Set(PresetParameter[] parameters, string key, double value)
        {
            if (parameters.FirstOrDefault(p => p.Key == key) is { } parameter)
            {
                parameter.Set(value);
            }
        }
    }

    [RelayCommand]
    private void SavePreset(string? effect)
    {
        if (effect is null)
        {
            return;
        }

        string display = effect == ChainPreset ? "Klang (alle Effekte)" : PresetEffectNames.First(e => e.Key == effect).Display;
        if (_dialogs.AskText("Preset speichern", $"Name für das Preset „{display}“ (z. B. „Meine Stimme“ oder „Podcast-Klang“):", string.Empty) is not { } name ||
            string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (PresetStore.For(effect).Any(p => string.Equals(p.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase)) &&
            !_dialogs.Confirm($"Es gibt schon ein Preset „{name.Trim()}“. Überschreiben?", "Preset speichern"))
        {
            return;
        }

        PresetStore.Save(new EffectPreset(effect, name.Trim(), CapturePreset(effect)));
        StatusText = $"Preset „{name.Trim()}“ gespeichert – du findest es im Preset-Menü mit ★.";
    }

    [RelayCommand]
    private void DeletePreset(string? effect)
    {
        if (effect is null || PresetStore.For(effect) is not { Count: > 0 } own)
        {
            return;
        }

        var answer = _dialogs.AskParameters(new ParameterRequest(
            "Eigenes Preset löschen", "Welches Preset soll gelöscht werden?",
            [new ChoiceParameter("Preset", own.Select(p => p.Name).ToArray(), 0)]));
        if (answer is not null)
        {
            string name = own[(int)answer[0]].Name;
            PresetStore.Delete(effect, name);
            StatusText = $"Preset „{name}“ gelöscht.";
        }
    }

    /// <summary>Aktuelle Reglerwerte eines Effekts – oder aller Effekte („Effekt.Regler“).</summary>
    private Dictionary<string, double> CapturePreset(string effect)
    {
        var map = PresetParameterMap;
        if (effect != ChainPreset)
        {
            return map[effect].ToDictionary(p => p.Key, p => p.Get());
        }

        var values = new Dictionary<string, double>();
        foreach (var (key, _) in PresetEffectNames)
        {
            foreach (var parameter in map[key])
            {
                values[$"{key}.{parameter.Key}"] = parameter.Get();
            }
        }

        return values;
    }

    private Dictionary<string, PresetParameter[]> PresetParameterMap => _presetParameters ??= new()
    {
        ["Equalizer"] =
        [
            P("On", () => B(IsEqualizerEnabled), v => IsEqualizerEnabled = v > 0.5),
            P("Vorverstärkung", () => CurrentEqualizer.PreampDb, v => CurrentEqualizer.PreampDb = v),
            .. Enumerable.Range(0, EqualizerSettings.BandFrequencies.Count).Select(i =>
                P($"B{i}", () => CurrentEqualizer.Bands[i].GainDb, v => CurrentEqualizer.Bands[i].GainDb = Math.Clamp(v, -12, 12))),
        ],
        ["Hall"] =
        [
            P("On", () => B(IsReverbEnabled), v => IsReverbEnabled = v > 0.5),
            P("Links", () => ReverbLeft, v => ReverbLeft = v), P("Rechts", () => ReverbRight, v => ReverbRight = v),
            P("Raum", () => ReverbRoomSize, v => ReverbRoomSize = v), P("Dämpfung", () => ReverbDamping, v => ReverbDamping = v),
        ],
        ["Kompressor"] =
        [
            P("On", () => B(IsCompressorEnabled), v => IsCompressorEnabled = v > 0.5),
            P("Schwelle", () => CompressorThreshold, v => CompressorThreshold = v), P("Verhältnis", () => CompressorRatio, v => CompressorRatio = v),
            P("Attack", () => CompressorAttack, v => CompressorAttack = v), P("Release", () => CompressorRelease, v => CompressorRelease = v),
            P("Aufholen", () => CompressorMakeup, v => CompressorMakeup = v),
            P("Links", () => CompressorMixLeft, v => CompressorMixLeft = v), P("Rechts", () => CompressorMixRight, v => CompressorMixRight = v),
            P("Gekoppelt", () => B(CompressorLinked), v => CompressorLinked = v > 0.5),
        ],
        ["Echo"] =
        [
            P("On", () => B(IsDelayEnabled), v => IsDelayEnabled = v > 0.5),
            P("ZeitL", () => DelayTimeLeft, v => DelayTimeLeft = v), P("ZeitR", () => DelayTimeRight, v => DelayTimeRight = v),
            P("Links", () => DelayMixLeft, v => DelayMixLeft = v), P("Rechts", () => DelayMixRight, v => DelayMixRight = v),
            P("Rückkopplung", () => DelayFeedback, v => DelayFeedback = v), P("Dämpfung", () => DelayDamping, v => DelayDamping = v),
            P("PingPong", () => B(DelayPingPong), v => DelayPingPong = v > 0.5),
        ],
        ["Filter"] =
        [
            P("On", () => B(IsFilterEnabled), v => IsFilterEnabled = v > 0.5),
            P("Hochpass", () => FilterHighPassPosition, v => FilterHighPassPosition = v),
            P("Tiefpass", () => FilterLowPassPosition, v => FilterLowPassPosition = v),
        ],
        ["Verzerrung"] =
        [
            P("On", () => B(IsDistortionEnabled), v => IsDistortionEnabled = v > 0.5),
            P("Art", () => DistortionTypeIndex, v => DistortionTypeIndex = Math.Clamp((int)Math.Round(v), 0, 2)),
            P("Drive", () => DistortionDrive, v => DistortionDrive = v), P("Ton", () => DistortionTone, v => DistortionTone = v),
            P("Links", () => DistortionMixLeft, v => DistortionMixLeft = v), P("Rechts", () => DistortionMixRight, v => DistortionMixRight = v),
        ],
        ["Chorus"] =
        [
            P("On", () => B(IsChorusEnabled), v => IsChorusEnabled = v > 0.5),
            P("Tempo", () => ChorusRate, v => ChorusRate = v), P("Tiefe", () => ChorusDepth, v => ChorusDepth = v),
            P("Links", () => ChorusMixLeft, v => ChorusMixLeft = v), P("Rechts", () => ChorusMixRight, v => ChorusMixRight = v),
        ],
        ["Flanger"] =
        [
            P("On", () => B(IsFlangerEnabled), v => IsFlangerEnabled = v > 0.5),
            P("Tempo", () => FlangerRate, v => FlangerRate = v), P("Tiefe", () => FlangerDepth, v => FlangerDepth = v),
            P("Rückkopplung", () => FlangerFeedback, v => FlangerFeedback = v),
            P("Links", () => FlangerMixLeft, v => FlangerMixLeft = v), P("Rechts", () => FlangerMixRight, v => FlangerMixRight = v),
        ],
        ["Gate"] =
        [
            P("On", () => B(IsGateEnabled), v => IsGateEnabled = v > 0.5),
            P("Schwelle", () => GateThreshold, v => GateThreshold = v), P("Absenkung", () => GateReduction, v => GateReduction = v),
            P("Halten", () => GateHold, v => GateHold = v), P("Release", () => GateRelease, v => GateRelease = v),
        ],
        ["Saettigung"] =
        [
            P("On", () => B(IsSaturationEnabled), v => IsSaturationEnabled = v > 0.5),
            P("Drive", () => SaturationDrive, v => SaturationDrive = v), P("Wärme", () => SaturationWarmth, v => SaturationWarmth = v),
            P("Anteil", () => SaturationMix, v => SaturationMix = v),
        ],
        ["Phaser"] =
        [
            P("On", () => B(IsPhaserEnabled), v => IsPhaserEnabled = v > 0.5),
            P("Tempo", () => PhaserRate, v => PhaserRate = v), P("Tiefe", () => PhaserDepth, v => PhaserDepth = v),
            P("Rückkopplung", () => PhaserFeedback, v => PhaserFeedback = v), P("Anteil", () => PhaserMix, v => PhaserMix = v),
        ],
        ["Tremolo"] =
        [
            P("On", () => B(IsTremoloEnabled), v => IsTremoloEnabled = v > 0.5),
            P("Tempo", () => TremoloRate, v => TremoloRate = v), P("Tiefe", () => TremoloDepth, v => TremoloDepth = v),
            P("Pendel", () => B(TremoloStereo), v => TremoloStereo = v > 0.5),
        ],
        ["Breite"] =
        [
            P("On", () => B(IsWidenerEnabled), v => IsWidenerEnabled = v > 0.5),
            P("Breite", () => WidenerWidth, v => WidenerWidth = v),
        ],
    };

    private static PresetParameter P(string key, Func<double> get, Action<double> set) => new(key, get, set);

    private static double B(bool value) => value ? 1 : 0;

    /// <summary>Filter-Regler steht auf einer Position 0 … 100 (logarithmisch); hier aus einer Frequenz berechnet.</summary>
    private static double FilterPosition(double hz) =>
        100 * Math.Log(hz / FilterSettings.MinHz) / Math.Log(FilterSettings.MaxHz / FilterSettings.MinHz);

    private static Dictionary<string, double> V(params (string Key, double Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value);

    /// <summary>Equalizer-Werte für ein Klang-Preset aus (Frequenz, dB)-Paaren – es gilt jeweils das nächste Band.</summary>
    private static IEnumerable<(string Key, double Value)> Eq(params (double Hz, double Db)[] bands)
    {
        var frequencies = EqualizerSettings.BandFrequencies;
        foreach (var (hz, db) in bands)
        {
            int index = Enumerable.Range(0, frequencies.Count).MinBy(i => Math.Abs(Math.Log(frequencies[i] / hz)));
            yield return ($"Equalizer.B{index}", db);
        }

        yield return ("Equalizer.On", 1);
    }

    private static IEnumerable<(string Name, IReadOnlyDictionary<string, double> Values)> BuiltInPresets(string effect) => effect switch
    {
        ChainPreset =>
        [
            ("Alles aus (neutral)", V()),
            ("Podcast-Stimme", V([
                ("Gate.On", 1), ("Gate.Schwelle", -45), ("Gate.Absenkung", 25), ("Gate.Halten", 80), ("Gate.Release", 200),
                ("Filter.On", 1), ("Filter.Hochpass", FilterPosition(80)), ("Filter.Tiefpass", 100),
                .. Eq((250, -2), (400, -1), (2500, 2), (4000, 3), (6300, 1)),
                ("Kompressor.On", 1), ("Kompressor.Schwelle", -22), ("Kompressor.Verhältnis", 3.5), ("Kompressor.Attack", 8),
                ("Kompressor.Release", 120), ("Kompressor.Aufholen", 6), ("Kompressor.Links", 100), ("Kompressor.Rechts", 100),
            ])),
            ("Warme Stimme", V([
                ("Saettigung.On", 1), ("Saettigung.Drive", 4), ("Saettigung.Wärme", 50), ("Saettigung.Anteil", 60),
                .. Eq((160, 2), (250, 1.5), (10000, -2)),
                ("Kompressor.On", 1), ("Kompressor.Schwelle", -18), ("Kompressor.Verhältnis", 2.5), ("Kompressor.Attack", 15),
                ("Kompressor.Release", 150), ("Kompressor.Aufholen", 3), ("Kompressor.Links", 100), ("Kompressor.Rechts", 100),
            ])),
            ("Radio-Moderation", V([
                ("Gate.On", 1), ("Gate.Schwelle", -40), ("Gate.Absenkung", 30), ("Gate.Halten", 60), ("Gate.Release", 150),
                .. Eq((100, 3), (400, -2), (4000, 3), (10000, 2)),
                ("Kompressor.On", 1), ("Kompressor.Schwelle", -26), ("Kompressor.Verhältnis", 6), ("Kompressor.Attack", 3),
                ("Kompressor.Release", 80), ("Kompressor.Aufholen", 9), ("Kompressor.Links", 100), ("Kompressor.Rechts", 100),
            ])),
            ("Telefon", V(
                ("Filter.On", 1), ("Filter.Hochpass", FilterPosition(300)), ("Filter.Tiefpass", FilterPosition(3400)),
                ("Verzerrung.On", 1), ("Verzerrung.Art", 0), ("Verzerrung.Drive", 25), ("Verzerrung.Ton", 60),
                ("Verzerrung.Links", 50), ("Verzerrung.Rechts", 50))),
            ("Großer Saal", V(("Hall.On", 1), ("Hall.Links", 40), ("Hall.Rechts", 40), ("Hall.Raum", 85), ("Hall.Dämpfung", 40))),
            ("Lo-Fi / Kassette", V(
                ("Saettigung.On", 1), ("Saettigung.Drive", 10), ("Saettigung.Wärme", 80), ("Saettigung.Anteil", 100),
                ("Filter.On", 1), ("Filter.Hochpass", FilterPosition(120)), ("Filter.Tiefpass", FilterPosition(6000)),
                ("Tremolo.On", 1), ("Tremolo.Tempo", 0.7), ("Tremolo.Tiefe", 8), ("Tremolo.Pendel", 0))),
            ("Gitarre mit Chorus", V(
                ("Chorus.On", 1), ("Chorus.Tempo", 0.8), ("Chorus.Tiefe", 55), ("Chorus.Links", 50), ("Chorus.Rechts", 50),
                ("Hall.On", 1), ("Hall.Links", 20), ("Hall.Rechts", 20), ("Hall.Raum", 55), ("Hall.Dämpfung", 50))),
            ("Breites Stereo", V(("Breite.On", 1), ("Breite.Breite", 160))),
        ],
        "Hall" =>
        [
            ("Kleiner Raum", V(("Links", 20), ("Rechts", 20), ("Raum", 25), ("Dämpfung", 60))),
            ("Großer Saal", V(("Links", 40), ("Rechts", 40), ("Raum", 85), ("Dämpfung", 40))),
            ("Kirche", V(("Links", 55), ("Rechts", 55), ("Raum", 98), ("Dämpfung", 25))),
        ],
        "Kompressor" =>
        [
            ("Sanft (Gesang)", V(("Schwelle", -18), ("Verhältnis", 2.5), ("Attack", 15), ("Release", 150), ("Aufholen", 3), ("Links", 100), ("Rechts", 100))),
            ("Sprache / Podcast", V(("Schwelle", -22), ("Verhältnis", 4), ("Attack", 8), ("Release", 120), ("Aufholen", 6), ("Links", 100), ("Rechts", 100))),
            ("Stark (Radio)", V(("Schwelle", -28), ("Verhältnis", 8), ("Attack", 3), ("Release", 80), ("Aufholen", 10), ("Links", 100), ("Rechts", 100))),
        ],
        "Echo" =>
        [
            ("Kurzes Slapback", V(("ZeitL", 110), ("ZeitR", 110), ("Links", 25), ("Rechts", 25), ("Rückkopplung", 0), ("PingPong", 0))),
            ("Viertel bei 120 BPM", V(("ZeitL", 500), ("ZeitR", 500), ("Links", 30), ("Rechts", 30), ("Rückkopplung", 35), ("PingPong", 0))),
            ("Ping-Pong", V(("ZeitL", 375), ("ZeitR", 375), ("Links", 35), ("Rechts", 35), ("Rückkopplung", 45), ("PingPong", 1))),
        ],
        "Filter" =>
        [
            ("Brummen weg (Hochpass 80 Hz)", V(("Hochpass", FilterPosition(80)), ("Tiefpass", 100))),
            ("Telefon (300–3400 Hz)", V(("Hochpass", FilterPosition(300)), ("Tiefpass", FilterPosition(3400)))),
            ("Dumpf (Tiefpass 2 kHz)", V(("Hochpass", 0), ("Tiefpass", FilterPosition(2000)))),
        ],
        "Verzerrung" =>
        [
            ("Leicht angeraut", V(("Art", 0), ("Drive", 25), ("Ton", 60), ("Links", 60), ("Rechts", 60))),
            ("Rock-Gitarre", V(("Art", 0), ("Drive", 70), ("Ton", 65), ("Links", 100), ("Rechts", 100))),
            ("Fuzz", V(("Art", 1), ("Drive", 80), ("Ton", 55), ("Links", 100), ("Rechts", 100))),
            ("Lo-Fi (Bitcrusher)", V(("Art", 2), ("Drive", 60), ("Ton", 50), ("Links", 70), ("Rechts", 70))),
        ],
        "Chorus" =>
        [
            ("Sanft", V(("Tempo", 0.5), ("Tiefe", 30), ("Links", 40), ("Rechts", 40))),
            ("Breit", V(("Tempo", 0.8), ("Tiefe", 60), ("Links", 60), ("Rechts", 60))),
            ("Schwebend", V(("Tempo", 0.3), ("Tiefe", 80), ("Links", 70), ("Rechts", 70))),
        ],
        "Flanger" =>
        [
            ("Langsam", V(("Tempo", 0.15), ("Tiefe", 70), ("Rückkopplung", 40), ("Links", 60), ("Rechts", 60))),
            ("Düsenjet", V(("Tempo", 0.3), ("Tiefe", 90), ("Rückkopplung", 75), ("Links", 80), ("Rechts", 80))),
        ],
        "Gate" =>
        [
            ("Sprache (sanft)", V(("Schwelle", -45), ("Absenkung", 25), ("Halten", 80), ("Release", 200))),
            ("Streng", V(("Schwelle", -35), ("Absenkung", 60), ("Halten", 40), ("Release", 80))),
            ("Schlagzeug", V(("Schwelle", -30), ("Absenkung", 80), ("Halten", 10), ("Release", 50))),
        ],
        "Saettigung" =>
        [
            ("Leicht warm", V(("Drive", 3), ("Wärme", 30), ("Anteil", 100))),
            ("Tonband", V(("Drive", 8), ("Wärme", 50), ("Anteil", 100))),
            ("Stark", V(("Drive", 16), ("Wärme", 60), ("Anteil", 100))),
        ],
        "Phaser" =>
        [
            ("Langsam", V(("Tempo", 0.2), ("Tiefe", 60), ("Rückkopplung", 30), ("Anteil", 50))),
            ("Klassisch", V(("Tempo", 0.6), ("Tiefe", 75), ("Rückkopplung", 50), ("Anteil", 60))),
            ("Schnell", V(("Tempo", 2.5), ("Tiefe", 50), ("Rückkopplung", 30), ("Anteil", 50))),
        ],
        "Tremolo" =>
        [
            ("Sanft", V(("Tempo", 4), ("Tiefe", 30), ("Pendel", 0))),
            ("Gitarren-Verstärker", V(("Tempo", 6), ("Tiefe", 60), ("Pendel", 0))),
            ("Stereo-Pendel", V(("Tempo", 2), ("Tiefe", 70), ("Pendel", 1))),
        ],
        "Breite" =>
        [
            ("Mono", V(("Breite", 0))),
            ("Etwas breiter", V(("Breite", 130))),
            ("Sehr breit", V(("Breite", 180))),
        ],
        _ => [],
    };
}
