using System.Text.Json;

namespace MusicWorker.Core.Persistence;

/// <summary>Ein gespeicherter Satz Reglerwerte für einen Effekt (oder die ganze Effektkette).</summary>
/// <param name="Effect">Schlüssel des Effekts, z. B. „Kompressor“ oder „Klang“ für die ganze Kette.</param>
/// <param name="Name">Name, den der Nutzer vergeben hat, z. B. „Meine Stimme“.</param>
/// <param name="Values">Reglerwerte (Ein/Aus als 1/0).</param>
public sealed record EffectPreset(string Effect, string Name, IReadOnlyDictionary<string, double> Values);

/// <summary>Speichert eigene Presets als JSON (z. B. %APPDATA%\MusicWorker\presets.json).</summary>
public sealed class PresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly List<EffectPreset> _presets;

    public PresetStore(string path)
    {
        _path = path;
        _presets = Load(path);
    }

    /// <summary>Wird nach jeder Änderung ausgelöst (Menüs neu aufbauen).</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<EffectPreset> For(string effect) =>
        _presets.Where(p => p.Effect == effect).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    /// <summary>Speichert ein Preset; ein gleichnamiges desselben Effekts wird ersetzt.</summary>
    public void Save(EffectPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            throw new ArgumentException("Bitte einen Namen angeben.", nameof(preset));
        }

        var trimmed = preset with { Name = preset.Name.Trim(), Values = new Dictionary<string, double>(preset.Values) };
        _presets.RemoveAll(p => p.Effect == trimmed.Effect && string.Equals(p.Name, trimmed.Name, StringComparison.CurrentCultureIgnoreCase));
        _presets.Add(trimmed);
        Persist();
    }

    public bool Delete(string effect, string name)
    {
        int removed = _presets.RemoveAll(p => p.Effect == effect && string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (removed > 0)
        {
            Persist();
        }

        return removed > 0;
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var data = _presets.Select(p => new StoredPreset { Effect = p.Effect, Name = p.Name, Values = p.Values.ToDictionary(v => v.Key, v => v.Value) }).ToList();
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Presets sind „nice to have“ – ein Fehler beim Speichern soll die Arbeit nicht stören.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static List<EffectPreset> Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<List<StoredPreset>>(File.ReadAllText(path), JsonOptions) is { } stored)
            {
                return stored
                    .Where(p => !string.IsNullOrWhiteSpace(p.Effect) && !string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => new EffectPreset(p.Effect, p.Name, p.Values ?? []))
                    .ToList();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Beschädigte Datei → ohne eigene Presets starten.
        }

        return [];
    }

    private sealed class StoredPreset
    {
        public string Effect { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public Dictionary<string, double>? Values { get; set; }
    }
}
