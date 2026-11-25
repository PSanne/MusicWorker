using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;

namespace MusicWorker.App.ViewModels;

/// <summary>
/// Weitere Live-Effekte im Editor: Noise-Gate, Bandsättigung, Phaser, Tremolo und Stereo-Breite.
/// Sie wirken sofort beim Abspielen und lassen sich mit „Übernehmen“ fest einrechnen.
/// </summary>
public sealed partial class MainViewModel
{
    private GateProcessor? _liveGate;
    private SaturationProcessor? _liveSaturation;
    private PhaserProcessor? _livePhaser;
    private TremoloProcessor? _liveTremolo;
    private WidenerProcessor? _liveWidener;

    // ───────────── Noise-Gate ─────────────

    [ObservableProperty]
    private bool _isGateEnabled;

    [ObservableProperty]
    private double _gateThreshold = -50;

    [ObservableProperty]
    private double _gateReduction = 40;

    [ObservableProperty]
    private double _gateHold = 60;

    [ObservableProperty]
    private double _gateRelease = 150;

    // ───────────── Bandsättigung ─────────────

    [ObservableProperty]
    private bool _isSaturationEnabled;

    [ObservableProperty]
    private double _saturationDrive = 6;

    [ObservableProperty]
    private double _saturationWarmth = 40;

    [ObservableProperty]
    private double _saturationMix = 100;

    // ───────────── Phaser ─────────────

    [ObservableProperty]
    private bool _isPhaserEnabled;

    [ObservableProperty]
    private double _phaserRate = 0.5;

    [ObservableProperty]
    private double _phaserDepth = 70;

    [ObservableProperty]
    private double _phaserFeedback = 40;

    [ObservableProperty]
    private double _phaserMix = 50;

    // ───────────── Tremolo ─────────────

    [ObservableProperty]
    private bool _isTremoloEnabled;

    [ObservableProperty]
    private double _tremoloRate = 5;

    [ObservableProperty]
    private double _tremoloDepth = 50;

    [ObservableProperty]
    private bool _tremoloStereo;

    // ───────────── Stereo-Breite ─────────────

    [ObservableProperty]
    private bool _isWidenerEnabled;

    [ObservableProperty]
    private double _widenerWidth = 140;

    public bool IsGateAudible => IsGateEnabled && !EffectiveGateSettings().IsNeutral;

    public bool IsSaturationAudible => IsSaturationEnabled && !EffectiveSaturationSettings().IsNeutral;

    public bool IsPhaserAudible => IsPhaserEnabled && !EffectivePhaserSettings().IsNeutral;

    public bool IsTremoloAudible => IsTremoloEnabled && !EffectiveTremoloSettings().IsNeutral;

    public bool IsWidenerAudible => IsWidenerEnabled && IsStereo && !EffectiveWidenerSettings().IsNeutral;

    private bool AreStudioEffectsActive => IsGateAudible || IsSaturationAudible || IsPhaserAudible || IsTremoloAudible || IsWidenerAudible;

    private IEnumerable<string> ActiveStudioEffectNames()
    {
        if (IsGateAudible)
        {
            yield return "Noise-Gate";
        }

        if (IsSaturationAudible)
        {
            yield return "Bandsättigung";
        }

        if (IsPhaserAudible)
        {
            yield return "Phaser";
        }

        if (IsTremoloAudible)
        {
            yield return "Tremolo";
        }

        if (IsWidenerAudible)
        {
            yield return "Stereo-Breite";
        }
    }

    private GateSettings EffectiveGateSettings() =>
        IsGateEnabled ? new GateSettings(GateThreshold, GateReduction, GateHold, GateRelease) : GateSettings.Off;

    private SaturationSettings EffectiveSaturationSettings() =>
        IsSaturationEnabled ? new SaturationSettings(SaturationDrive, SaturationWarmth, SaturationMix) : SaturationSettings.Off;

    private PhaserSettings EffectivePhaserSettings() =>
        IsPhaserEnabled ? new PhaserSettings(PhaserRate, PhaserDepth, PhaserFeedback, PhaserMix) : PhaserSettings.Off;

    private TremoloSettings EffectiveTremoloSettings() =>
        IsTremoloEnabled ? new TremoloSettings(TremoloRate, TremoloDepth, TremoloStereo) : TremoloSettings.Off;

    private WidenerSettings EffectiveWidenerSettings() =>
        IsWidenerEnabled ? new WidenerSettings(WidenerWidth) : WidenerSettings.Off;

    /// <summary>Effekte vor dem Equalizer (Gate) – für die Wiedergabekette.</summary>
    private NAudio.Wave.ISampleProvider WithPreStudioEffects(NAudio.Wave.ISampleProvider source)
    {
        int rate = source.WaveFormat.SampleRate;
        int channels = source.WaveFormat.Channels;
        _liveGate = new GateProcessor(rate, channels, EffectiveGateSettings());
        return new Audio.ProcessingSampleProvider(source, _liveGate.Process);
    }

    /// <summary>Sättigung direkt nach dem Equalizer.</summary>
    private NAudio.Wave.ISampleProvider WithSaturation(NAudio.Wave.ISampleProvider source)
    {
        _liveSaturation = new SaturationProcessor(source.WaveFormat.SampleRate, source.WaveFormat.Channels, EffectiveSaturationSettings());
        return new Audio.ProcessingSampleProvider(source, _liveSaturation.Process);
    }

    /// <summary>Phaser, Tremolo und Stereo-Breite nach Chorus/Flanger, vor Echo und Hall.</summary>
    private NAudio.Wave.ISampleProvider WithModulationStudioEffects(NAudio.Wave.ISampleProvider source)
    {
        int rate = source.WaveFormat.SampleRate;
        int channels = source.WaveFormat.Channels;
        _livePhaser = new PhaserProcessor(rate, channels, EffectivePhaserSettings());
        _liveTremolo = new TremoloProcessor(rate, channels, EffectiveTremoloSettings());
        _liveWidener = new WidenerProcessor(channels, EffectiveWidenerSettings());
        source = new Audio.ProcessingSampleProvider(source, _livePhaser.Process);
        source = new Audio.ProcessingSampleProvider(source, _liveTremolo.Process);
        return new Audio.ProcessingSampleProvider(source, _liveWidener.Process);
    }

    /// <summary>Für den Export: Effekte vor dem EQ, nach dem EQ und nach Chorus/Flanger (wie in der Wiedergabe).</summary>
    private (IAudioEffect[] Before, IAudioEffect[] AfterEq, IAudioEffect[] AfterModulation) StudioEffectsForExport() =>
    (
        IsGateAudible ? [new GateEffect(EffectiveGateSettings())] : [],
        IsSaturationAudible ? [new SaturationEffect(EffectiveSaturationSettings())] : [],
        [
            .. IsPhaserAudible ? [new PhaserEffect(EffectivePhaserSettings())] : Array.Empty<IAudioEffect>(),
            .. IsTremoloAudible ? [new TremoloEffect(EffectiveTremoloSettings())] : Array.Empty<IAudioEffect>(),
            .. IsWidenerAudible ? [new WidenerEffect(EffectiveWidenerSettings())] : Array.Empty<IAudioEffect>(),
        ]);

    private void RefreshStudioEffects()
    {
        _liveGate?.Update(EffectiveGateSettings());
        _liveSaturation?.Update(EffectiveSaturationSettings());
        _livePhaser?.Update(EffectivePhaserSettings());
        _liveTremolo?.Update(EffectiveTremoloSettings());
        _liveWidener?.Update(EffectiveWidenerSettings());
        OnPropertyChanged(nameof(IsGateAudible));
        OnPropertyChanged(nameof(IsSaturationAudible));
        OnPropertyChanged(nameof(IsPhaserAudible));
        OnPropertyChanged(nameof(IsTremoloAudible));
        OnPropertyChanged(nameof(IsWidenerAudible));
        OnPropertyChanged(nameof(EqualizerIndicator));
        ApplyGateCommand.NotifyCanExecuteChanged();
        ApplySaturationCommand.NotifyCanExecuteChanged();
        ApplyPhaserCommand.NotifyCanExecuteChanged();
        ApplyTremoloCommand.NotifyCanExecuteChanged();
        ApplyWidenerCommand.NotifyCanExecuteChanged();
        SaveStudioEffectSettings();
    }

    private void SaveStudioEffectSettings()
    {
        var s = _settings.Settings;
        s.GateEnabled = IsGateEnabled;
        s.GateThresholdDb = GateThreshold;
        s.GateReductionDb = GateReduction;
        s.GateHoldMs = GateHold;
        s.GateReleaseMs = GateRelease;
        s.SaturationEnabled = IsSaturationEnabled;
        s.SaturationDriveDb = SaturationDrive;
        s.SaturationWarmth = SaturationWarmth;
        s.SaturationMix = SaturationMix;
        s.PhaserEnabled = IsPhaserEnabled;
        s.PhaserRate = PhaserRate;
        s.PhaserDepth = PhaserDepth;
        s.PhaserFeedback = PhaserFeedback;
        s.PhaserMix = PhaserMix;
        s.TremoloEnabled = IsTremoloEnabled;
        s.TremoloRate = TremoloRate;
        s.TremoloDepth = TremoloDepth;
        s.TremoloStereo = TremoloStereo;
        s.WidenerEnabled = IsWidenerEnabled;
        s.WidenerWidth = WidenerWidth;
    }

    // „Übernehmen“: fest in die Auswahl (oder die ganze Datei) einrechnen und den Effekt danach ausschalten.

    [RelayCommand(CanExecute = nameof(CanApplyGate))]
    private async Task ApplyGateAsync()
    {
        if (await ApplyEffectAsync(new GateEffect(EffectiveGateSettings())))
        {
            IsGateEnabled = false;
            StatusText = "Noise-Gate in das Audio übernommen und ausgeschaltet.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplySaturation))]
    private async Task ApplySaturationAsync()
    {
        if (await ApplyEffectAsync(new SaturationEffect(EffectiveSaturationSettings())))
        {
            IsSaturationEnabled = false;
            StatusText = "Bandsättigung in das Audio übernommen und ausgeschaltet.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyPhaser))]
    private async Task ApplyPhaserAsync()
    {
        if (await ApplyEffectAsync(new PhaserEffect(EffectivePhaserSettings())))
        {
            IsPhaserEnabled = false;
            StatusText = "Phaser in das Audio übernommen und ausgeschaltet.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyTremolo))]
    private async Task ApplyTremoloAsync()
    {
        if (await ApplyEffectAsync(new TremoloEffect(EffectiveTremoloSettings())))
        {
            IsTremoloEnabled = false;
            StatusText = "Tremolo in das Audio übernommen und ausgeschaltet.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyWidener))]
    private async Task ApplyWidenerAsync()
    {
        // Stereo-Breite wirkt immer auf beide Kanäle zusammen.
        if (await ApplyEffectAsync(new WidenerEffect(EffectiveWidenerSettings()), respectChannelTarget: false))
        {
            IsWidenerEnabled = false;
            StatusText = "Stereo-Breite in das Audio übernommen und ausgeschaltet.";
        }
    }

    private bool CanApplyGate() => CanUseAudio() && IsGateAudible;

    private bool CanApplySaturation() => CanUseAudio() && IsSaturationAudible;

    private bool CanApplyPhaser() => CanUseAudio() && IsPhaserAudible;

    private bool CanApplyTremolo() => CanUseAudio() && IsTremoloAudible;

    private bool CanApplyWidener() => CanUseAudio() && IsWidenerAudible;

    partial void OnIsGateEnabledChanged(bool value) => RefreshStudioEffects();

    partial void OnGateThresholdChanged(double value) => RefreshStudioEffects();

    partial void OnGateReductionChanged(double value) => RefreshStudioEffects();

    partial void OnGateHoldChanged(double value) => RefreshStudioEffects();

    partial void OnGateReleaseChanged(double value) => RefreshStudioEffects();

    partial void OnIsSaturationEnabledChanged(bool value) => RefreshStudioEffects();

    partial void OnSaturationDriveChanged(double value) => RefreshStudioEffects();

    partial void OnSaturationWarmthChanged(double value) => RefreshStudioEffects();

    partial void OnSaturationMixChanged(double value) => RefreshStudioEffects();

    partial void OnIsPhaserEnabledChanged(bool value) => RefreshStudioEffects();

    partial void OnPhaserRateChanged(double value) => RefreshStudioEffects();

    partial void OnPhaserDepthChanged(double value) => RefreshStudioEffects();

    partial void OnPhaserFeedbackChanged(double value) => RefreshStudioEffects();

    partial void OnPhaserMixChanged(double value) => RefreshStudioEffects();

    partial void OnIsTremoloEnabledChanged(bool value) => RefreshStudioEffects();

    partial void OnTremoloRateChanged(double value) => RefreshStudioEffects();

    partial void OnTremoloDepthChanged(double value) => RefreshStudioEffects();

    partial void OnTremoloStereoChanged(bool value) => RefreshStudioEffects();

    partial void OnIsWidenerEnabledChanged(bool value) => RefreshStudioEffects();

    partial void OnWidenerWidthChanged(double value) => RefreshStudioEffects();
}
