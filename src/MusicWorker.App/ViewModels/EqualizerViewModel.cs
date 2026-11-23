using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Audio;
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

    public string ToolTip => Frequency >= 1000
        ? (Frequency / 1000).ToString("0.#", CultureInfo.CurrentCulture) + " kHz"
        : Frequency.ToString("0", CultureInfo.CurrentCulture) + " Hz";

    partial void OnGainDbChanged(double value) => GainChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Steuerlogik des Equalizer-Fensters inklusive Presets und Live-Vorhören.</summary>
public sealed partial class EqualizerViewModel : ObservableObject, IDisposable
{
    private readonly EqualizerPreview? _preview;
    private bool _applyingPreset;

    [ObservableProperty]
    private double _preampDb;

    [ObservableProperty]
    private bool _preventClipping;

    [ObservableProperty]
    private EqualizerPreset? _selectedPreset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewButtonText))]
    private bool _isPreviewing;

    public EqualizerViewModel(EqualizerSettings initial, bool preventClipping, EqualizerPreview? preview)
    {
        _preview = preview;
        _preampDb = initial.PreampDb;
        _preventClipping = preventClipping;

        Bands = EqualizerSettings.BandFrequencies
            .Select((frequency, i) => new EqualizerBandViewModel(frequency, initial.GainsDb[i]))
            .ToArray();
        foreach (var band in Bands)
        {
            band.GainChanged += OnBandChanged;
        }

        _selectedPreset = EqualizerPresets.All.FirstOrDefault(p => p.GainsDb.SequenceEqual(initial.GainsDb));
    }

    public IReadOnlyList<EqualizerBandViewModel> Bands { get; }

    public IReadOnlyList<EqualizerPreset> Presets => EqualizerPresets.All;

    public double MaxGainDb => EqualizerSettings.MaxGainDb;

    public bool CanPreview => _preview is not null;

    public string PreviewButtonText => IsPreviewing ? "■  Vorhören stoppen" : "▶  Vorhören";

    public EqualizerSettings ToSettings() => new(Bands.Select(b => b.GainDb).ToArray(), PreampDb);

    [RelayCommand]
    private void Reset()
    {
        SelectedPreset = EqualizerPresets.All[0];
        PreampDb = 0;
    }

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private void TogglePreview()
    {
        if (IsPreviewing)
        {
            StopPreview();
            return;
        }

        _preview!.Start(ToSettings());
        IsPreviewing = true;
    }

    public void StopPreview()
    {
        _preview?.Stop();
        IsPreviewing = false;
    }

    public void Dispose()
    {
        StopPreview();
        foreach (var band in Bands)
        {
            band.GainChanged -= OnBandChanged;
        }
    }

    partial void OnSelectedPresetChanged(EqualizerPreset? value)
    {
        if (value is null)
        {
            return;
        }

        _applyingPreset = true;
        for (int i = 0; i < Bands.Count; i++)
        {
            Bands[i].GainDb = value.GainsDb[i];
        }

        _applyingPreset = false;
        PushToPreview();
    }

    partial void OnPreampDbChanged(double value) => PushToPreview();

    private void OnBandChanged(object? sender, EventArgs e)
    {
        if (_applyingPreset)
        {
            return;
        }

        // Von Hand verändert → passt nicht mehr zur gewählten Voreinstellung.
        SelectedPreset = null;
        PushToPreview();
    }

    private void PushToPreview()
    {
        if (IsPreviewing)
        {
            _preview?.Update(ToSettings());
        }
    }
}
