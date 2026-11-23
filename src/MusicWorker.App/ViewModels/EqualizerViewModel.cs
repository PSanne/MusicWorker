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
/// Zustand des Live-Equalizers im Hauptfenster: 15 Regler, Vorverstärkung, Presets, Ein/Aus.
/// Jede Änderung löst <see cref="SettingsChanged"/> aus – die Wiedergabe übernimmt sie sofort.
/// </summary>
public sealed partial class EqualizerViewModel : ObservableObject
{
    private bool _loading;

    [ObservableProperty]
    private double _preampDb;

    [ObservableProperty]
    private bool _preventClipping;

    [ObservableProperty]
    private EqualizerPreset? _selectedPreset;

    [ObservableProperty]
    private bool _isEnabled;

    public EqualizerViewModel(EqualizerSettings initial, bool isEnabled, bool preventClipping)
    {
        _isEnabled = isEnabled;
        _preventClipping = preventClipping;

        Bands = EqualizerSettings.BandFrequencies
            .Select(frequency => new EqualizerBandViewModel(frequency, 0))
            .ToArray();
        foreach (var band in Bands)
        {
            band.GainChanged += OnBandChanged;
        }

        LoadSettings(initial);
    }

    /// <summary>Regler, Pegel oder Ein/Aus wurden verändert.</summary>
    public event EventHandler? SettingsChanged;

    public IReadOnlyList<EqualizerBandViewModel> Bands { get; }

    public IReadOnlyList<EqualizerPreset> Presets => EqualizerPresets.All;

    /// <summary>Die Einstellung laut Reglern (unabhängig davon, ob der EQ eingeschaltet ist).</summary>
    public EqualizerSettings ToSettings() => new(Bands.Select(b => b.GainDb).ToArray(), PreampDb);

    /// <summary>Das, was tatsächlich zu hören sein soll: neutral, wenn der EQ ausgeschaltet ist.</summary>
    public EqualizerSettings EffectiveSettings => IsEnabled ? ToSettings() : EqualizerSettings.Neutral;

    /// <summary>True, wenn der EQ eingeschaltet ist und den Klang tatsächlich verändert.</summary>
    public bool IsActive => IsEnabled && !ToSettings().IsNeutral;

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
        RaiseSettingsChanged();
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
        RaiseSettingsChanged();
    }

    partial void OnPreampDbChanged(double value)
    {
        if (!_loading)
        {
            RaiseSettingsChanged();
        }
    }

    partial void OnIsEnabledChanged(bool value) => RaiseSettingsChanged();

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
        RaiseSettingsChanged();
    }

    private void RaiseSettingsChanged()
    {
        OnPropertyChanged(nameof(IsActive));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
