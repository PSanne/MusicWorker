using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.Core.Dsp;

namespace MusicWorker.App.ViewModels;

/// <summary>Ein Regler des Equalizers.</summary>
public sealed partial class EqualizerBandViewModel : ObservableObject
{
    [ObservableProperty]
    private double _gainDb;

    public EqualizerBandViewModel(double frequency, double gainDb)
    {
        Frequency = frequency;
        _gainDb = gainDb;
    }

    public event EventHandler? GainChanged;

    public double Frequency { get; }

    /// <summary>Beschriftung, z. B. „63“, „1k“ oder „2,5k“.</summary>
    public string Label => Frequency >= 1000
        ? (Frequency / 1000).ToString("0.#", CultureInfo.CurrentCulture) + "k"
        : Frequency.ToString("0", CultureInfo.CurrentCulture);

    public string ToolTip => (Frequency >= 1000
        ? (Frequency / 1000).ToString("0.#", CultureInfo.CurrentCulture) + " kHz"
        : Frequency.ToString("0", CultureInfo.CurrentCulture) + " Hz") + " – Doppelklick setzt auf 0 dB";

    partial void OnGainDbChanged(double value) => GainChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Die 15 Regler, Vorverstärkung und Voreinstellung eines Equalizers. Beim Pro-Kanal-EQ gibt es
/// zwei davon (links und rechts). Jede Änderung löst <see cref="SettingsChanged"/> aus – die
/// Wiedergabe übernimmt sie sofort.
/// </summary>
public sealed partial class EqualizerViewModel : ObservableObject
{
    private bool _loading;

    [ObservableProperty]
    private double _preampDb;

    [ObservableProperty]
    private EqualizerPreset? _selectedPreset;

    public EqualizerViewModel(EqualizerSettings initial)
    {
        Bands = EqualizerSettings.BandFrequencies
            .Select(frequency => new EqualizerBandViewModel(frequency, 0))
            .ToArray();
        foreach (var band in Bands)
        {
            band.GainChanged += OnBandChanged;
        }

        LoadSettings(initial);
    }

    /// <summary>Regler oder Pegel wurden verändert.</summary>
    public event EventHandler? SettingsChanged;

    public IReadOnlyList<EqualizerBandViewModel> Bands { get; }

    public IReadOnlyList<EqualizerPreset> Presets => EqualizerPresets.All;

    /// <summary>Die aktuelle Einstellung der Regler.</summary>
    public EqualizerSettings ToSettings() => new(Bands.Select(b => b.GainDb).ToArray(), PreampDb);

    /// <summary>True, wenn alle Regler und der Pegel auf 0 dB stehen.</summary>
    public bool IsNeutral => ToSettings().IsNeutral;

    /// <summary>Setzt alle Regler auf die angegebenen Werte.</summary>
    public void LoadSettings(EqualizerSettings settings)
    {
        _loading = true;
        for (int i = 0; i < Bands.Count; i++)
        {
            Bands[i].GainDb = settings.GainsDb[i];
        }

        PreampDb = settings.PreampDb;
        SelectedPreset = EqualizerPresets.All.FirstOrDefault(p => p.GainsDb.SequenceEqual(settings.GainsDb));
        _loading = false;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Reset() => LoadSettings(EqualizerSettings.Neutral);

    partial void OnSelectedPresetChanged(EqualizerPreset? value)
    {
        if (value is null || _loading)
        {
            return;
        }

        _loading = true;
        for (int i = 0; i < Bands.Count; i++)
        {
            Bands[i].GainDb = value.GainsDb[i];
        }

        _loading = false;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnPreampDbChanged(double value)
    {
        if (!_loading)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnBandChanged(object? sender, EventArgs e)
    {
        if (_loading)
        {
            return;
        }

        // Von Hand verändert → passt nicht mehr zur gewählten Voreinstellung.
        _loading = true;
        SelectedPreset = null;
        _loading = false;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
