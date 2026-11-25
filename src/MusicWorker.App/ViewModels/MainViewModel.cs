using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Audio;
using MusicWorker.App.Helpers;
using MusicWorker.App.Services;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Editing;
using MusicWorker.Core.Effects;
using NAudio.Wave;

namespace MusicWorker.App.ViewModels;

/// <summary>Steuerlogik des Hauptfensters: verbindet Oberfläche, Editor, Wiedergabe und Dateien.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PlayheadInterval = TimeSpan.FromMilliseconds(33);

    private readonly AudioEditor _editor = new();
    private readonly IDialogService _dialogs;
    private readonly AudioFileService _files;
    private readonly PlaybackService _playback;
    private readonly SettingsService _settings;
    private CancellationTokenSource? _playheadUpdates;
    private EqualizerProcessor? _liveEqualizer;
    private ReverbProcessor? _liveReverb;
    private CompressorProcessor? _liveCompressor;
    private DelayProcessor? _liveDelay;
    private FilterProcessor? _liveFilter;
    private DistortionProcessor? _liveDistortion;
    private ChorusProcessor? _liveChorus;
    private FlangerProcessor? _liveFlanger;
    private SpectrumAnalyzer[]? _spectrumAnalyzers;
    private ChannelMonitorSampleProvider? _channelMonitor;

    /// <summary>Spalten der Spektrumanzeige (je 1/6 Oktave über 10 Oktaven).</summary>
    private const int SpectrumBands = 60;

    /// <summary>Wie schnell Balken nach unten fallen (dB pro Bild, bei ~30 Bildern/s ≈ 45 dB/s).</summary>
    private const float SpectrumFallPerFrame = 1.5f;

    private static readonly double[] CurveFrequencies = SpectrumScale.CenterFrequencies(240);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(WindowTitle))]
    private string? _filePath;

    /// <summary>Anzeigename, wenn das Audio keine eigene Datei hat (z. B. „Mixdown“ oder ein Clip).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName), nameof(WindowTitle))]
    private string? _documentName;

    /// <summary>Haupt-Registerkarte: 0 = Editor, 1 = Mischpult, 2 = Noten.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorTab), nameof(IsMixerTab), nameof(IsNotesTab), nameof(UsesMixerTransport))]
    private int _selectedMainTab;

    /// <summary>Clip aus dem Mischpult, der gerade im Editor bearbeitet wird.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinkedClip), nameof(LinkedClipText))]
    private ClipViewModel? _linkedClip;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private int _playheadFrame;

    [ObservableProperty]
    private bool _isEqualizerVisible;

    /// <summary>Live-EQ ein/aus (für den Vorher-/Nachher-Vergleich).</summary>
    [ObservableProperty]
    private bool _isEqualizerEnabled;

    /// <summary>Beim Übernehmen/Exportieren den Pegel bei Bedarf auf -1 dB absenken.</summary>
    [ObservableProperty]
    private bool _equalizerPreventClipping;

    /// <summary>Pro-Kanal-EQ: links und rechts getrennt einstellbar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesPerChannelEqualizer), nameof(CurrentEqualizer), nameof(EqualizerTargetText))]
    private bool _isEqualizerPerChannel;

    /// <summary>Im Pro-Kanal-Modus: Die Regler zeigen den rechten EQ (sonst den linken).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EqualizerEditsLeft), nameof(CurrentEqualizer), nameof(EqualizerTargetText), nameof(ActiveEqualizerCurve))]
    private bool _equalizerEditsRight;

    /// <summary>EQ-Kurve des rechten Kanals (nur im Pro-Kanal-Modus, sonst null).</summary>
    [ObservableProperty]
    private double[]? _equalizerCurveRight;

    /// <summary>Live-Hall ein/aus.</summary>
    [ObservableProperty]
    private bool _isReverbEnabled;

    /// <summary>Hall-Stärke links (bei Mono: für den einzigen Kanal) in %.</summary>
    [ObservableProperty]
    private double _reverbLeft;

    /// <summary>Hall-Stärke rechts in %.</summary>
    [ObservableProperty]
    private double _reverbRight;

    /// <summary>Raumgröße in % (klein = kurzer Nachhall, groß = langer).</summary>
    [ObservableProperty]
    private double _reverbRoomSize = 50;

    /// <summary>Dämpfung in % (hoch = dunklerer Hall).</summary>
    [ObservableProperty]
    private double _reverbDamping = 50;

    // ───────────── Kompressor ─────────────

    [ObservableProperty]
    private bool _isCompressorEnabled;

    [ObservableProperty]
    private double _compressorThreshold = -18;

    [ObservableProperty]
    private double _compressorRatio = 4;

    [ObservableProperty]
    private double _compressorAttack = 10;

    [ObservableProperty]
    private double _compressorRelease = 150;

    [ObservableProperty]
    private double _compressorMakeup;

    [ObservableProperty]
    private double _compressorMixLeft = 100;

    [ObservableProperty]
    private double _compressorMixRight = 100;

    [ObservableProperty]
    private bool _compressorLinked = true;

    /// <summary>Aktuelle Pegelreduktion links in dB (für die Anzeige).</summary>
    [ObservableProperty]
    private double _compressorReductionLeft;

    /// <summary>Aktuelle Pegelreduktion rechts in dB (für die Anzeige).</summary>
    [ObservableProperty]
    private double _compressorReductionRight;

    // ───────────── Echo ─────────────

    [ObservableProperty]
    private bool _isDelayEnabled;

    [ObservableProperty]
    private double _delayTimeLeft = 375;

    [ObservableProperty]
    private double _delayTimeRight = 375;

    [ObservableProperty]
    private double _delayMixLeft = 30;

    [ObservableProperty]
    private double _delayMixRight = 30;

    [ObservableProperty]
    private double _delayFeedback = 35;

    [ObservableProperty]
    private double _delayDamping = 30;

    [ObservableProperty]
    private bool _delayPingPong;

    // ───────────── Filter (Hoch-/Tiefpass) ─────────────

    [ObservableProperty]
    private bool _isFilterEnabled;

    /// <summary>Hochpass als Reglerstellung 0 … 100 % (logarithmisch 20 Hz … 20 kHz; 0 % = aus).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterHighPassText))]
    private double _filterHighPassPosition;

    /// <summary>Tiefpass als Reglerstellung 0 … 100 % (logarithmisch 20 Hz … 20 kHz; 100 % = aus).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterLowPassText))]
    private double _filterLowPassPosition = 100;

    // ───────────── Verzerrung ─────────────

    [ObservableProperty]
    private bool _isDistortionEnabled;

    /// <summary>0 = Overdrive, 1 = Fuzz, 2 = Bitcrusher.</summary>
    [ObservableProperty]
    private int _distortionTypeIndex;

    [ObservableProperty]
    private double _distortionDrive = 50;

    [ObservableProperty]
    private double _distortionTone = 70;

    [ObservableProperty]
    private double _distortionMixLeft = 100;

    [ObservableProperty]
    private double _distortionMixRight = 100;

    // ───────────── Chorus ─────────────

    [ObservableProperty]
    private bool _isChorusEnabled;

    [ObservableProperty]
    private double _chorusRate = 0.8;

    [ObservableProperty]
    private double _chorusDepth = 50;

    [ObservableProperty]
    private double _chorusMixLeft = 70;

    [ObservableProperty]
    private double _chorusMixRight = 70;

    // ───────────── Flanger ─────────────

    [ObservableProperty]
    private bool _isFlangerEnabled;

    [ObservableProperty]
    private double _flangerRate = 0.3;

    [ObservableProperty]
    private double _flangerDepth = 70;

    [ObservableProperty]
    private double _flangerFeedback = 50;

    [ObservableProperty]
    private double _flangerMixLeft = 70;

    [ObservableProperty]
    private double _flangerMixRight = 70;

    // ───────────── Wiedergabe: Schleife & Gerät ─────────────

    /// <summary>Schleife: Auswahl (bzw. ganze Datei) endlos wiederholen.</summary>
    [ObservableProperty]
    private bool _isLooping;

    /// <summary>
    /// Live-Spektrum der Wiedergabe (nach dem EQ) in dBFS – ein Array pro Kanal (Stereo: links, rechts);
    /// null, wenn nichts spielt.
    /// </summary>
    [ObservableProperty]
    private float[][]? _spectra;

    /// <summary>Effekte wirken auf den linken Kanal.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveChannels), nameof(ChannelTargetText))]
    private bool _editLeft = true;

    /// <summary>Effekte wirken auf den rechten Kanal.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveChannels), nameof(ChannelTargetText))]
    private bool _editRight = true;

    /// <summary>Linken Kanal abhören (aus = beim Abspielen stumm).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MutedChannels), nameof(ChannelTargetText))]
    private bool _hearLeft = true;

    /// <summary>Rechten Kanal abhören (aus = beim Abspielen stumm).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MutedChannels), nameof(ChannelTargetText))]
    private bool _hearRight = true;

    /// <summary>Frequenzgang des Live-EQ in dB für die Kurve über dem Spektrum.</summary>
    [ObservableProperty]
    private double[]? _equalizerCurve;

    [ObservableProperty]
    private string _statusText = "Bereit – öffne eine Audiodatei (Strg+O) oder ziehe sie ins Fenster.";

    public MainViewModel(
        IDialogService dialogs, AudioFileService files, PlaybackService playback, SettingsService settings)
    {
        _dialogs = dialogs;
        _files = files;
        _playback = playback;
        _settings = settings;
        RefreshRecentFiles();

        Mixer = new MixerViewModel(dialogs, files, new ProjectService(files)) { Settings = settings };
        Notes = new NoteEditorViewModel(Mixer, dialogs, settings);
        Mixer.OpenClipInEditorRequested += OnOpenClipInEditorRequested;
        Mixer.OpenNoteClipRequested += OnOpenNoteClipRequested;
        Mixer.PlaybackStarting += (_, _) => StopPlayback();
        Mixer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MixerViewModel.HasTracks) or nameof(MixerViewModel.IsBusy))
            {
                OpenMixdownInEditorCommand.NotifyCanExecuteChanged();
            }
        };

        var saved = settings.Settings;
        Equalizer = new EqualizerViewModel(LoadEqualizerSettings(saved.EqualizerGainsDb, saved.EqualizerPreampDb));
        EqualizerRight = new EqualizerViewModel(saved.EqualizerRightGainsDb is null
            ? Equalizer.ToSettings()
            : LoadEqualizerSettings(saved.EqualizerRightGainsDb, saved.EqualizerRightPreampDb));
        Equalizer.SettingsChanged += OnEqualizerSettingsChanged;
        EqualizerRight.SettingsChanged += OnEqualizerSettingsChanged;
        _isEqualizerEnabled = saved.EqualizerEnabled;
        _equalizerPreventClipping = saved.EqualizerPreventClipping;
        _isEqualizerPerChannel = saved.EqualizerPerChannel;
        _isEqualizerVisible = saved.EqualizerVisible;
        _isReverbEnabled = saved.ReverbEnabled;
        _reverbLeft = saved.ReverbLeftPercent;
        _reverbRight = saved.ReverbRightPercent;
        _reverbRoomSize = saved.ReverbRoomSizePercent;
        _reverbDamping = saved.ReverbDampingPercent;
        _isCompressorEnabled = saved.CompressorEnabled;
        _compressorThreshold = saved.CompressorThresholdDb;
        _compressorRatio = saved.CompressorRatio;
        _compressorAttack = saved.CompressorAttackMs;
        _compressorRelease = saved.CompressorReleaseMs;
        _compressorMakeup = saved.CompressorMakeupDb;
        _compressorMixLeft = saved.CompressorMixLeft;
        _compressorMixRight = saved.CompressorMixRight;
        _compressorLinked = saved.CompressorLinked;
        _isDelayEnabled = saved.DelayEnabled;
        _delayTimeLeft = saved.DelayTimeLeftMs;
        _delayTimeRight = saved.DelayTimeRightMs;
        _delayMixLeft = saved.DelayMixLeft;
        _delayMixRight = saved.DelayMixRight;
        _delayFeedback = saved.DelayFeedbackPercent;
        _delayDamping = saved.DelayDampingPercent;
        _delayPingPong = saved.DelayPingPong;
        _isFilterEnabled = saved.FilterEnabled;
        _filterHighPassPosition = saved.FilterHighPassPosition;
        _filterLowPassPosition = saved.FilterLowPassPosition;
        _isDistortionEnabled = saved.DistortionEnabled;
        _distortionTypeIndex = Math.Clamp(saved.DistortionType, 0, 2);
        _distortionDrive = saved.DistortionDrive;
        _distortionTone = saved.DistortionTone;
        _distortionMixLeft = saved.DistortionMixLeft;
        _distortionMixRight = saved.DistortionMixRight;
        _isChorusEnabled = saved.ChorusEnabled;
        _chorusRate = saved.ChorusRate;
        _chorusDepth = saved.ChorusDepth;
        _chorusMixLeft = saved.ChorusMixLeft;
        _chorusMixRight = saved.ChorusMixRight;
        _isFlangerEnabled = saved.FlangerEnabled;
        _flangerRate = saved.FlangerRate;
        _flangerDepth = saved.FlangerDepth;
        _flangerFeedback = saved.FlangerFeedback;
        _flangerMixLeft = saved.FlangerMixLeft;
        _flangerMixRight = saved.FlangerMixRight;
        // Weitere Effekte (Noise-Gate, Sättigung, Phaser, Tremolo, Stereo-Breite)
        _isGateEnabled = saved.GateEnabled;
        _gateThreshold = saved.GateThresholdDb;
        _gateReduction = saved.GateReductionDb;
        _gateHold = saved.GateHoldMs;
        _gateRelease = saved.GateReleaseMs;
        _isSaturationEnabled = saved.SaturationEnabled;
        _saturationDrive = saved.SaturationDriveDb;
        _saturationWarmth = saved.SaturationWarmth;
        _saturationMix = saved.SaturationMix;
        _isPhaserEnabled = saved.PhaserEnabled;
        _phaserRate = saved.PhaserRate;
        _phaserDepth = saved.PhaserDepth;
        _phaserFeedback = saved.PhaserFeedback;
        _phaserMix = saved.PhaserMix;
        _isTremoloEnabled = saved.TremoloEnabled;
        _tremoloRate = saved.TremoloRate;
        _tremoloDepth = saved.TremoloDepth;
        _tremoloStereo = saved.TremoloStereo;
        _isWidenerEnabled = saved.WidenerEnabled;
        _widenerWidth = saved.WidenerWidth;
        RefreshOutputDevices(saved.OutputDeviceName);
        Mixer.SelectAudioDriver(saved.AudioDriverName, saved.AudioBufferMs);
        Mixer.RecordingLatencyMs = saved.RecordingLatencyMs;
        Mixer.SelectInputByName(saved.InputDeviceName);
        Mixer.IsInputMonitoringEnabled = saved.InputMonitoring;
        Mixer.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MixerViewModel.SelectedInputDevice) when Mixer.SelectedInputDevice is { } input:
                    _settings.Settings.InputDeviceName = input.Name;
                    _settings.Save();
                    break;
                case nameof(MixerViewModel.SelectedAudioDriver) or nameof(MixerViewModel.BufferMs):
                    _settings.Settings.AudioDriverName = Mixer.SelectedAudioDriver?.Name;
                    _settings.Settings.AudioBufferMs = Mixer.BufferMs;
                    _settings.Settings.RecordingLatencyMs = Mixer.RecordingLatencyMs;
                    _settings.Save();
                    break;
                case nameof(MixerViewModel.IsInputMonitoringEnabled):
                    _settings.Settings.InputMonitoring = Mixer.IsInputMonitoringEnabled;
                    _settings.Save();
                    break;
            }
        };
        UpdateEqualizerCurve();

        _editor.Changed += OnEditorChanged;
        _editor.SelectionChanged += OnEditorSelectionChanged;
        _playback.PlaybackEnded += OnPlaybackEnded;
    }

    /// <summary>Eine neue Datei wurde geladen (die Ansicht setzt daraufhin den Zoom zurück).</summary>
    public event EventHandler? DocumentLoaded;

    public AudioBuffer Buffer => _editor.Buffer;

    /// <summary>
    /// Der Live-Equalizer: wirkt sofort auf jede Wiedergabe. Im Pro-Kanal-Modus ist das der EQ
    /// für den linken Kanal, sonst der gemeinsame EQ für beide Kanäle.
    /// </summary>
    public EqualizerViewModel Equalizer { get; }

    /// <summary>EQ für den rechten Kanal (nur im Pro-Kanal-Modus wirksam).</summary>
    public EqualizerViewModel EqualizerRight { get; }

    /// <summary>Der EQ, dessen Regler gerade im Panel angezeigt werden.</summary>
    public EqualizerViewModel CurrentEqualizer => UsesPerChannelEqualizer && EqualizerEditsRight ? EqualizerRight : Equalizer;

    /// <summary>Beschriftung über den Reglern: Für welchen Kanal gelten sie gerade?</summary>
    public string EqualizerTargetText => !UsesPerChannelEqualizer
        ? (IsStereo ? "Regler gelten für beide Kanäle" : string.Empty)
        : EqualizerEditsRight ? "Regler gelten für: RECHTS" : "Regler gelten für: LINKS";

    /// <summary>Pro-Kanal-EQ ist eingeschaltet und die Datei ist Stereo.</summary>
    public bool UsesPerChannelEqualizer => IsEqualizerPerChannel && IsStereo;

    /// <summary>Linker EQ wird gerade bearbeitet (für die Auswahlknöpfe L/R im Panel).</summary>
    public bool EqualizerEditsLeft
    {
        get => !EqualizerEditsRight;
        set
        {
            if (value)
            {
                EqualizerEditsRight = false;
            }
        }
    }

    /// <summary>Hinweis in der Statusleiste, solange Live-Effekte (EQ, Hall) den Klang verändern.</summary>
    public string EqualizerIndicator
    {
        get
        {
            var parts = new List<string>();
            if (IsEqualizerActive)
            {
                parts.Add(UsesPerChannelEqualizer ? "EQ (L/R)" : "EQ");
            }

            if (IsFilterActive)
            {
                parts.Add("Filter");
            }

            if (IsDistortionActive)
            {
                parts.Add("Verzerrung");
            }

            if (IsCompressorActive)
            {
                parts.Add("Kompressor");
            }

            if (IsChorusActive)
            {
                parts.Add("Chorus");
            }

            if (IsFlangerActive)
            {
                parts.Add("Flanger");
            }

            if (IsDelayActive)
            {
                parts.Add("Echo");
            }

            if (IsReverbActive)
            {
                parts.Add("Hall");
            }

            parts.AddRange(ActiveStudioEffectNames());
            return parts.Count == 0 ? string.Empty : string.Join(" + ", parts) + " aktiv";
        }
    }

    /// <summary>True, wenn der Hall eingeschaltet ist und tatsächlich etwas hinzufügt.</summary>
    private bool IsReverbActive => IsReverbEnabled && !EffectiveReverbSettings().IsNeutral;

    /// <summary>True, wenn der Kompressor eingeschaltet ist und tatsächlich wirkt.</summary>
    private bool IsCompressorActive => IsCompressorEnabled && !EffectiveCompressorSettings().IsNeutral;

    /// <summary>True, wenn das Echo eingeschaltet ist und tatsächlich wirkt.</summary>
    private bool IsDelayActive => IsDelayEnabled && !EffectiveDelaySettings().IsNeutral;

    /// <summary>Für die grünen Punkte an den Registerkarten.</summary>
    public bool IsReverbAudible => IsReverbActive;

    public bool IsCompressorAudible => IsCompressorActive;

    public bool IsDelayAudible => IsDelayActive;

    private bool IsFilterActive => IsFilterEnabled && !EffectiveFilterSettings().IsNeutral;

    private bool IsDistortionActive => IsDistortionEnabled && !EffectiveDistortionSettings().IsNeutral;

    private bool IsChorusActive => IsChorusEnabled && !EffectiveChorusSettings().IsNeutral;

    private bool IsFlangerActive => IsFlangerEnabled && !EffectiveFlangerSettings().IsNeutral;

    public bool IsFilterAudible => IsFilterActive;

    public bool IsDistortionAudible => IsDistortionActive;

    public bool IsChorusAudible => IsChorusActive;

    public bool IsFlangerAudible => IsFlangerActive;

    public string FilterHighPassText => FilterHighPassPosition <= 0.05 ? "aus" : FormatHz(PositionToHz(FilterHighPassPosition));

    public string FilterLowPassText => FilterLowPassPosition >= 99.95 ? "aus" : FormatHz(PositionToHz(FilterLowPassPosition));

    /// <summary>Auswahl für die Art der Verzerrung.</summary>
    public IReadOnlyList<string> DistortionTypes { get; } = ["Overdrive (warm)", "Fuzz (hart)", "Bitcrusher (Lo-Fi)"];

    /// <summary>Wiedergabegeräte für das Menü „Wiedergabe → Wiedergabegerät“.</summary>
    public ObservableCollection<OutputDeviceItem> OutputDevices { get; } = [];

    /// <summary>Mindestens ein Live-Effekt verändert gerade den Klang.</summary>
    private bool AreLiveEffectsActive => IsEqualizerActive || IsFilterActive || IsDistortionActive || IsCompressorActive
        || IsChorusActive || IsFlangerActive || IsDelayActive || IsReverbActive || AreStudioEffectsActive;

    /// <summary>True, wenn der EQ eingeschaltet ist und den Klang tatsächlich verändert.</summary>
    private bool IsEqualizerActive => IsEqualizerEnabled &&
        (!Equalizer.IsNeutral || (UsesPerChannelEqualizer && !EqualizerRight.IsNeutral));

    /// <summary>Welche EQ-Kurve hervorgehoben wird (0 = links/gemeinsam, 1 = rechts).</summary>
    public int ActiveEqualizerCurve => UsesPerChannelEqualizer && EqualizerEditsRight ? 1 : 0;

    /// <summary>Stereo-Datei geladen – dann sind die Kanal-Werkzeuge verfügbar.</summary>
    public bool IsStereo => HasAudio && Buffer.Channels == 2;

    /// <summary>Für die Wellenform: In welchen Kanälen wird die Auswahl bearbeitet?</summary>
    public bool[]? ActiveChannels => IsStereo ? [EditLeft, EditRight] : null;

    /// <summary>Für die Wellenform: Welche Kanäle sind beim Abhören stumm?</summary>
    public bool[]? MutedChannels => IsStereo ? [!HearLeft, !HearRight] : null;

    /// <summary>Hinweis in der Statusleiste, wenn nicht beide Kanäle bearbeitet oder gehört werden.</summary>
    public string ChannelTargetText
    {
        get
        {
            if (!IsStereo)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (!EditsAllChannels)
            {
                parts.Add(EditLeft ? "Effekte: nur L" : "Effekte: nur R");
            }

            if (!HearLeft || !HearRight)
            {
                parts.Add(!HearLeft && !HearRight ? "beide Kanäle stumm" : HearLeft ? "Hören: nur L" : "Hören: nur R");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>True, wenn Effekte auf alle Kanäle wirken (immer bei Mono).</summary>
    private bool EditsAllChannels => !IsStereo || (EditLeft && EditRight);

    /// <summary>Einträge für „Datei → Zuletzt geöffnet“.</summary>
    public ObservableCollection<RecentFileItem> RecentFiles { get; } = [];

    public bool HasRecentFiles => RecentFiles.Count > 0;

    /// <summary>Auswahl – wird von der Wellenform-Ansicht in beide Richtungen gebunden.</summary>
    public SelectionRange Selection
    {
        get => _editor.Selection;
        set => _editor.Selection = value;
    }

    public bool HasAudio => _editor.HasAudio;

    public bool HasSelection => _editor.HasSelection;

    public bool IsModified => _editor.IsModified;

    public string FileName => FilePath is null ? DocumentName ?? "Unbenannt" : Path.GetFileName(FilePath);

    /// <summary>Das Mehrspur-Mischpult.</summary>
    public MixerViewModel Mixer { get; }

    public bool IsEditorTab => SelectedMainTab == 0;

    public bool IsMixerTab => SelectedMainTab == 1;

    public bool IsNotesTab => SelectedMainTab == 2;

    /// <summary>Mischpult oder Noten-Editor: beide nutzen die Wiedergabe des Mischpults.</summary>
    public bool UsesMixerTransport => SelectedMainTab is 1 or 2;

    /// <summary>Noten-Editor (Piano-Roll, Bildschirmtastatur).</summary>
    public NoteEditorViewModel Notes { get; }

    public bool HasLinkedClip => LinkedClip is not null;

    public string LinkedClipText => LinkedClip is { } clip
        ? $"Du bearbeitest einen Clip der Spur „{clip.Track.Name}“ aus dem Mischpult."
        : string.Empty;

    public string WindowTitle => $"{(IsModified ? "● " : string.Empty)}{FileName} – MusicWorker";

    public string UndoText => _editor.UndoDescription is { } d ? $"_Rückgängig: {d}" : "_Rückgängig";

    public string RedoText => _editor.RedoDescription is { } d ? $"_Wiederholen: {d}" : "_Wiederholen";

    public string PositionText => HasAudio
        ? $"{FormatFrame(PlayheadFrame)} / {TimeFormatter.Format(Buffer.Duration)}"
        : "–";

    public string SelectionText
    {
        get
        {
            if (!HasAudio)
            {
                return string.Empty;
            }

            var s = Selection;
            return s.IsEmpty
                ? $"Cursor: {FormatFrame(s.Start)}"
                : $"Auswahl: {FormatFrame(s.Start)} – {FormatFrame(s.End)} " +
                  $"(Länge {FormatFrame(s.Length)})";
        }
    }

    public string FormatText
    {
        get
        {
            if (!HasAudio)
            {
                return string.Empty;
            }

            string channels = Buffer.Channels switch
            {
                1 => "Mono",
                2 => "Stereo",
                var n => $"{n} Kanäle",
            };
            return string.Format(CultureInfo.CurrentCulture, "{0:N0} Hz · {1}", Buffer.SampleRate, channels);
        }
    }

    // ───────────── Datei ─────────────

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenAsync()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        string? path = _dialogs.ShowOpenFileDialog(AudioFileService.OpenFilter);
        if (path is not null)
        {
            await LoadAsync(path);
        }
    }

    /// <summary>Öffnet eine Datei direkt (z. B. per Drag &amp; Drop oder Kommandozeile).</summary>
    /// <summary>Datei aus der Kommandozeile bzw. per Doppelklick: Projekte ins Mischpult, Audio in den Editor.</summary>
    public async Task OpenPathAsync(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".mwproj", StringComparison.OrdinalIgnoreCase))
        {
            SelectedMainTab = 1;
            await Mixer.OpenProjectFileAsync(path);
        }
        else
        {
            await OpenFileAsync(path);
        }
    }

    public async Task OpenFileAsync(string path)
    {
        if (CanOpen() && ConfirmDiscardChanges())
        {
            await LoadAsync(path);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (!File.Exists(path))
        {
            _dialogs.ShowError($"Die Datei wurde nicht gefunden und wird aus der Liste entfernt:\n\n{path}");
            _settings.RemoveRecentFile(path);
            RefreshRecentFiles();
            return;
        }

        await OpenFileAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task ExportAsync()
    {
        if (_dialogs.ShowExportDialog(Path.GetFileNameWithoutExtension(FileName), "Exportieren", PreferredExportFormat) is not { } target)
        {
            return;
        }

        RememberExportFormat(target.Format);
        string path = target.Path;

        // Live-Effekte einrechnen? (Sie verändern sonst nur die Wiedergabe, nicht die Datei.)
        var liveEffects = AskIncludeLiveEffects(
            $"Folgende Live-Effekte sind eingeschaltet, aber noch nicht in das Audio übernommen: {LiveEffectsName}.\n\n" +
            "Sollen sie in die exportierte Datei eingerechnet werden?\n\n" +
            "Ja = mit Effekten exportieren\nNein = ohne Effekte exportieren");

        StopPlayback();
        IsBusy = true;
        StatusText = $"Exportiere „{Path.GetFileName(path)}“ …";
        var buffer = Buffer;
        try
        {
            var markers = _editor.Markers;
            await Task.Run(() =>
            {
                _files.Export(ApplyAll(liveEffects, buffer), path, target.Format);
                if (AudioFileService.SupportsMarkers(path))
                {
                    WavMarkers.Append(path, markers); // Marker bleiben in der WAV-Datei erhalten
                }
            });
            _editor.MarkSaved();
            FilePath = path;
            StatusText = $"Exportiert nach „{Path.GetFileName(path)}“.";
        }
        catch (Exception ex)
        {
            StatusText = "Export fehlgeschlagen.";
            _dialogs.ShowError($"Die Datei konnte nicht gespeichert werden.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Zuletzt gewähltes Exportformat (gilt auch für das Mischpult).</summary>
    public ExportFormat PreferredExportFormat =>
        Enum.TryParse<ExportFormat>(_settings.Settings.LastExportFormat, out var format) ? format : ExportFormat.Wav16;

    public void RememberExportFormat(ExportFormat format)
    {
        _settings.Settings.LastExportFormat = format.ToString();
        _settings.Save();
    }

    /// <summary>Speichert linken und rechten Kanal als zwei Mono-Dateien (…_L und …_R).</summary>
    [RelayCommand(CanExecute = nameof(CanUseStereo))]
    private async Task ExportChannelsAsync()
    {
        if (_dialogs.ShowExportDialog(Path.GetFileNameWithoutExtension(FileName), "Kanäle als Mono-Dateien exportieren – Basisname wählen", PreferredExportFormat) is not { } target)
        {
            return;
        }

        RememberExportFormat(target.Format);
        string basePath = target.Path;

        string directory = Path.GetDirectoryName(basePath) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(basePath);
        string extension = Path.GetExtension(basePath);
        string leftPath = Path.Combine(directory, $"{name}_L{extension}");
        string rightPath = Path.Combine(directory, $"{name}_R{extension}");

        if ((File.Exists(leftPath) || File.Exists(rightPath)) && !_dialogs.Confirm(
                $"Mindestens eine dieser Dateien existiert bereits und wird überschrieben:\n\n{leftPath}\n{rightPath}",
                "Dateien überschreiben?"))
        {
            return;
        }

        var liveEffects = AskIncludeLiveEffects(
            $"Folgende Live-Effekte sind eingeschaltet: {LiveEffectsName}. Sollen sie in die exportierten Dateien eingerechnet werden?");

        StopPlayback();
        IsBusy = true;
        StatusText = "Exportiere linken und rechten Kanal …";
        var buffer = Buffer;
        try
        {
            await Task.Run(() =>
            {
                var source = ApplyAll(liveEffects, buffer);
                _files.Export(source.GetChannel(0), leftPath, target.Format);
                _files.Export(source.GetChannel(1), rightPath, target.Format);
            });
            StatusText = $"Exportiert: „{Path.GetFileName(leftPath)}“ und „{Path.GetFileName(rightPath)}“.";
        }
        catch (Exception ex)
        {
            StatusText = "Export fehlgeschlagen.";
            _dialogs.ShowError($"Die Dateien konnten nicht gespeichert werden.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseStereo))]
    private void KeepLeftChannel() => Edit(() => _editor.KeepChannel(0));

    [RelayCommand(CanExecute = nameof(CanUseStereo))]
    private void KeepRightChannel() => Edit(() => _editor.KeepChannel(1));

    /// <summary>Wird beim Schließen des Fensters gefragt (Editor und Mischpult-Projekt).</summary>
    public bool ConfirmClose() => ConfirmDiscardChanges() && Mixer.ConfirmDiscardChanges();

    // ───────────── Verbindung zum Mischpult ─────────────

    /// <summary>Das Editor-Audio als neue Spur ins Mischpult übernehmen.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void AddEditorAudioAsTrack()
    {
        StopPlayback();
        string name = Path.GetFileNameWithoutExtension(FileName);
        Mixer.AddTrack(Buffer, string.IsNullOrWhiteSpace(name) ? "Spur" : name);
        SelectedMainTab = 1;
    }

    /// <summary>Die Änderungen am Clip zurück in seine Mischpult-Spur schreiben.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyToLinkedClip))]
    private void ApplyToLinkedClip()
    {
        if (LinkedClip is not { } clip)
        {
            return;
        }

        StopPlayback();
        if (!Mixer.ReplaceClipAudio(clip, Buffer))
        {
            _dialogs.ShowError("Der Clip ist im Mischpult nicht mehr vorhanden (Spur oder Clip wurde gelöscht).");
            LinkedClip = null;
            return;
        }

        _editor.MarkSaved();
        StatusText = $"Clip in die Spur „{clip.Track.Name}“ übernommen.";
        SelectedMainTab = 1;
    }

    private bool CanApplyToLinkedClip() => HasLinkedClip && CanUseAudio();

    /// <summary>Den Mixdown des Mischpults im Editor öffnen (z. B. zum Nachbearbeiten).</summary>
    [RelayCommand(CanExecute = nameof(CanOpenMixdown))]
    private async Task OpenMixdownInEditorAsync()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        StopPlayback();
        Mixer.StopPlayback();
        IsBusy = true;
        StatusText = "Mixdown wird berechnet …";
        try
        {
            var mix = await Task.Run(Mixer.RenderMixdown);
            _editor.Load(mix);
            FilePath = null;
            DocumentName = "Mixdown";
            LinkedClip = null;
            PlayheadFrame = 0;
            SelectedMainTab = 0;
            DocumentLoaded?.Invoke(this, EventArgs.Empty);
            StatusText = $"Mixdown im Editor geöffnet – Länge {TimeFormatter.Format(mix.Duration, false)}.";
        }
        catch (Exception ex)
        {
            StatusText = "Mixdown fehlgeschlagen.";
            _dialogs.ShowError($"Der Mixdown konnte nicht berechnet werden.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanOpenMixdown() => !IsBusy && Mixer.HasTracks && !Mixer.IsBusy;

    /// <summary>Notenclip im Noten-Editor öffnen und zur Registerkarte „Noten“ wechseln.</summary>
    private void OnOpenNoteClipRequested(object? sender, ClipViewModel clip)
    {
        Notes.Open(clip);
        SelectedMainTab = 2;
        Mixer.StatusText = "Noten-Editor: Klick setzt eine Note, Ziehen verschiebt, Doppelklick löscht. Die Computertastatur (Y–M, Q–U) spielt die Bildschirmtastatur.";
    }

    private void OnOpenClipInEditorRequested(object? sender, ClipViewModel clip)
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        StopPlayback();
        _editor.Load(clip.Audio);
        FilePath = null;
        DocumentName = $"Clip – {clip.Track.Name}";
        LinkedClip = clip;
        PlayheadFrame = 0;
        SelectedMainTab = 0;
        DocumentLoaded?.Invoke(this, EventArgs.Empty);
        StatusText = "Clip im Editor geöffnet – nach dem Bearbeiten „In Spur übernehmen“ klicken.";
    }

    /// <summary>Dateien aus Drag &amp; Drop: im Mischpult als Spuren, im Editor als Datei öffnen.</summary>
    public async Task HandleDroppedFilesAsync(IReadOnlyList<string> files)
    {
        if (UsesMixerTransport)
        {
            await Mixer.AddFilesAsync(files);
        }
        else if (files.Count > 0)
        {
            await OpenFileAsync(files[0]);
        }
    }

    // ───────────── Transport (Tasten gelten für die sichtbare Ansicht) ─────────────

    [RelayCommand]
    private void TransportToggle()
    {
        if (UsesMixerTransport)
        {
            Mixer.TogglePlayPause();
        }
        else if (CanUseAudio())
        {
            TogglePlayPause();
        }
    }

    [RelayCommand]
    private void TransportStop()
    {
        if (UsesMixerTransport)
        {
            Mixer.Stop();
        }
        else
        {
            StopPlayback();
        }
    }

    [RelayCommand]
    private void TransportSkipForward()
    {
        if (UsesMixerTransport)
        {
            Mixer.SkipBy(TimeSpan.FromSeconds(5));
        }
        else if (CanUseAudio())
        {
            SkipForward();
        }
    }

    [RelayCommand]
    private void TransportSkipBack()
    {
        if (UsesMixerTransport)
        {
            Mixer.SkipBy(TimeSpan.FromSeconds(-5));
        }
        else if (CanUseAudio())
        {
            SkipBack();
        }
    }

    /// <summary>Strg+R: Aufnahme im Mischpult starten/beenden (im Editor-Reiter wird zuerst ins Mischpult gewechselt).</summary>
    [RelayCommand]
    private void RecordKey()
    {
        if (!UsesMixerTransport && !Mixer.IsRecording)
        {
            SelectedMainTab = 1;
        }

        if (Mixer.ToggleRecordCommand.CanExecute(null))
        {
            Mixer.ToggleRecordCommand.Execute(null);
        }
    }

    /// <summary>Latenzausgleich: um wie viel Aufnahmen nach vorne geschoben werden.</summary>
    [RelayCommand]
    private void SetRecordingLatency()
    {
        var request = new ValueRequest("Latenzausgleich", "Um wie viele Millisekunden sollen Aufnahmen nach vorne geschoben werden?",
            "Aus- und Eingang der Soundkarte verzögern etwas. Liegen Aufnahmen hörbar zu spät, den Wert erhöhen; liegen sie zu früh, verringern. " +
            "Typisch sind 30 – 120 ms. Tipp: Metronom einschalten, im Takt klatschen und am Raster prüfen.",
            Minimum: 0, Maximum: 500, Value: Mixer.RecordingLatencyMs, SmallChange: 5, Unit: "ms");
        if (_dialogs.AskValue(request) is { } value)
        {
            Mixer.RecordingLatencyMs = value;
            _settings.Settings.RecordingLatencyMs = value;
            _settings.Save();
            StatusText = $"Latenzausgleich: {value:0} ms";
        }
    }

    /// <summary>Strg+S: im Mischpult das Projekt speichern, im Editor exportieren.</summary>
    [RelayCommand]
    private async Task SaveKeyAsync()
    {
        if (UsesMixerTransport)
        {
            if (Mixer.SaveProjectCommand.CanExecute(null))
            {
                await Mixer.SaveProjectCommand.ExecuteAsync(null);
            }
        }
        else if (CanUseAudio())
        {
            await ExportAsync();
        }
    }

    /// <summary>Entf: im Mischpult den gewählten Clip löschen, im Editor die Auswahl.</summary>
    [RelayCommand]
    private void DeleteKey()
    {
        if (IsNotesTab)
        {
            Notes.DeleteSelected();
        }
        else if (IsMixerTab)
        {
            if (Mixer.DeleteSelectedClipCommand.CanExecute(null))
            {
                Mixer.DeleteSelectedClipCommand.Execute(null);
            }
        }
        else if (CanUseSelection())
        {
            Delete();
        }
    }

    partial void OnSelectedMainTabChanged(int value)
    {
        // Beim Wechseln der Ansicht hält alles an – so spielt nie etwas „unsichtbar“ weiter.
        // Zwischen Mischpult und Noten-Editor läuft die Wiedergabe weiter (beide gehören zum Projekt).
        StopPlayback();
        if (value == 0)
        {
            Mixer.StopPlayback();
        }

        if (value != 2)
        {
            Notes.ReleaseAllKeys();
        }
    }

    partial void OnLinkedClipChanged(ClipViewModel? value) => ApplyToLinkedClipCommand.NotifyCanExecuteChanged();

    // ───────────── Wiedergabe ─────────────

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void Play()
    {
        if (_playback.State == PlaybackState.Paused)
        {
            _playback.Resume();
            IsPlaying = true;
            StartPlayheadUpdates();
            return;
        }

        Mixer.StopPlayback(); // nie Editor und Mischpult gleichzeitig

        var selection = Selection;
        int start = selection.Start;
        int end = selection.IsEmpty ? Buffer.FrameCount : selection.End;
        if (start >= end)
        {
            start = 0; // Cursor steht am Ende → von vorn beginnen
        }

        // Schleife: Auswahl endlos – ohne Auswahl ab dem Cursor, danach immer wieder ab dem Anfang.
        int loopStart = selection.IsEmpty ? 0 : start;

        try
        {
            _playback.Play(Buffer, start, end, IsLooping, AttachLiveEqualizer, loopStart);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Die Wiedergabe konnte nicht gestartet werden.\n\n{ex.Message}");
            return;
        }

        IsPlaying = true;
        StartPlayheadUpdates();
    }

    [RelayCommand(CanExecute = nameof(IsPlaying))]
    private void Pause()
    {
        _playback.Pause();
        IsPlaying = false;
        StopPlayheadUpdates();
        PlayheadFrame = _playback.CurrentFrame;
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void Stop() => StopPlayback();

    /// <summary>
    /// Springt an eine Stelle (Positionsgriff in der Zeitleiste): Während Wiedergabe oder Pause läuft es
    /// dort weiter, im Stopp wird der Cursor dorthin gesetzt.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void Seek(int frame)
    {
        frame = Math.Clamp(frame, 0, Buffer.FrameCount);
        if (_playback.State != PlaybackState.Stopped)
        {
            _playback.Seek(frame);
            PlayheadFrame = frame;
            return;
        }

        _editor.Selection = SelectionRange.Caret(frame);
        PlayheadFrame = frame;
    }

    /// <summary>5 Sekunden vorspulen.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void SkipForward() => SkipBy(TimeSpan.FromSeconds(5));

    /// <summary>5 Sekunden zurückspulen.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void SkipBack() => SkipBy(TimeSpan.FromSeconds(-5));

    private void SkipBy(TimeSpan offset)
    {
        int current = _playback.State != PlaybackState.Stopped ? _playback.CurrentFrame : Selection.Start;
        Seek(current + (int)(offset.TotalSeconds * Buffer.SampleRate));
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void TogglePlayPause()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    // ───────────── Bearbeiten ─────────────

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void SelectAll() => _editor.SelectAll();

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void Cut() => Edit(_editor.Cut);

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void Copy()
    {
        _editor.Copy();
        StatusText = $"Kopiert ({FormatFrame(_editor.Clipboard!.FrameCount)}).";
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste() => Edit(_editor.Paste);

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void Delete() => Edit(_editor.Delete);

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void Trim() => Edit(_editor.Trim);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        string? description = _editor.UndoDescription;
        StopPlayback();
        _editor.Undo();
        StatusText = $"Rückgängig: {description}";
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        string? description = _editor.RedoDescription;
        StopPlayback();
        _editor.Redo();
        StatusText = $"Wiederholt: {description}";
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void InsertSilence()
    {
        var request = new ValueRequest(
            "Stille einfügen", "Dauer der Stille:", "Die Stille wird am Cursor bzw. am Anfang der Auswahl eingefügt.",
            Minimum: 0.1, Maximum: 60, Value: 1, SmallChange: 0.1, Unit: "Sekunden");
        if (_dialogs.AskValue(request) is { } seconds && seconds > 0)
        {
            Edit(() => _editor.InsertSilence(TimeSpan.FromSeconds(seconds)));
        }
    }

    // ───────────── Effekte ─────────────

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task ChangeGainAsync()
    {
        var request = new ValueRequest(
            "Lautstärke ändern", "Verstärkung:",
            "Positive Werte machen lauter, negative leiser. +6 dB ≈ doppelte Amplitude. " +
            "Ohne Auswahl wird die ganze Datei geändert.",
            Minimum: -24, Maximum: 24, Value: 0, SmallChange: 0.5, Unit: "dB");
        if (_dialogs.AskValue(request) is { } decibels && decibels != 0)
        {
            await ApplyEffectAsync(new GainEffect(decibels));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private Task NormalizeAsync() => ApplyEffectAsync(new NormalizeEffect());

    [RelayCommand]
    private void ToggleEqualizer() => IsEqualizerVisible = !IsEqualizerVisible;

    /// <summary>
    /// Rechnet den Live-EQ fest in das Audio ein (Auswahl oder ganze Datei; je nach „Bearbeiten L/R“
    /// nur in einen Kanal) und stellt die übernommenen Regler danach auf neutral – sonst würde der EQ
    /// beim Abspielen ein zweites Mal wirken.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyEqualizer))]
    private async Task ApplyEqualizerAsync()
    {
        var effect = CreateEqualizerEffectForTarget();
        if (effect is null || !await ApplyEffectAsync(effect))
        {
            return;
        }

        bool left = !IsStereo || EditLeft;
        bool right = IsStereo && EditRight;

        if (!UsesPerChannelEqualizer && left && (right || !IsStereo))
        {
            // Gemeinsamer EQ für alle Kanäle übernommen.
            Equalizer.LoadSettings(EqualizerSettings.Neutral);
            StatusText = "Equalizer in das Audio übernommen – die Regler stehen wieder auf 0 dB.";
            return;
        }

        if (!UsesPerChannelEqualizer)
        {
            // Gemeinsamer EQ, aber nur in einen Kanal eingerechnet: Für den anderen Kanal soll er
            // weiter hörbar bleiben → auf Pro-Kanal-EQ umschalten und nur die übernommene Seite nullen.
            EqualizerRight.LoadSettings(Equalizer.ToSettings());
            IsEqualizerPerChannel = true;
        }

        if (left)
        {
            Equalizer.LoadSettings(EqualizerSettings.Neutral);
        }

        if (right)
        {
            EqualizerRight.LoadSettings(EqualizerSettings.Neutral);
        }

        StatusText = left && right
            ? "Equalizer (L/R) in das Audio übernommen – die Regler stehen wieder auf 0 dB."
            : $"Equalizer in den {(left ? "linken" : "rechten")} Kanal übernommen – dessen Regler stehen wieder auf 0 dB.";
    }

    /// <summary>
    /// Rechnet den Live-Hall fest in das Audio ein (je nach „Bearbeiten L/R“ nur in einen Kanal) und
    /// dreht die übernommenen Stärke-Regler danach auf 0. Bei der ganzen Datei wird hinten Platz
    /// zum Ausklingen angehängt.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyReverb))]
    private async Task ApplyReverbAsync()
    {
        var effect = CreateReverbEffectForTarget();
        if (effect is null || !await ApplyEffectAsync(effect))
        {
            return;
        }

        bool left = !IsStereo || EditLeft;
        bool right = IsStereo && EditRight;
        if (left)
        {
            ReverbLeft = 0;
        }

        if (right)
        {
            ReverbRight = 0;
        }

        StatusText = $"{effect.Name} in das Audio übernommen – der Stärke-Regler steht wieder auf 0.";
    }

    /// <summary>Überträgt die gerade angezeigte EQ-Einstellung auf den anderen Kanal.</summary>
    [RelayCommand]
    private void CopyEqualizerToOtherChannel()
    {
        var (source, target, name) = EqualizerEditsRight
            ? (EqualizerRight, Equalizer, "linken")
            : (Equalizer, EqualizerRight, "rechten");
        target.LoadSettings(source.ToSettings());
        StatusText = $"EQ-Einstellung auf den {name} Kanal kopiert.";
    }

    /// <summary>
    /// Tempo und Tonhöhe – für beide Kanäle gemeinsam oder pro Kanal verschieden. Wird ein Kanal
    /// dabei länger, wird der andere am Ende des Stücks mit Stille aufgefüllt.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task TempoPitchAsync()
    {
        var range = _editor.EffectRange;

        // Ist unter „Bearbeiten“ nur ein Kanal gewählt, gleich im Pro-Kanal-Modus öffnen.
        var options = new TempoPitchOptions(IsStereo, IsStereo && !EditsAllChannels, Buffer.FrameToTime(range.Length));
        if (_dialogs.AskTempoAndPitch(options) is not { } request)
        {
            return;
        }

        var left = new TempoPitchEffect(request.TempoLeft, request.SemitonesLeft);
        if (!request.PerChannel || !IsStereo)
        {
            // Beide Kanäle gemeinsam (Stereo bleibt dabei phasengleich zusammen).
            if (!left.IsNeutral)
            {
                await ApplyEffectAsync(left, respectChannelTarget: false);
            }

            return;
        }

        var right = new TempoPitchEffect(request.TempoRight, request.SemitonesRight);
        if (left.IsNeutral && right.IsNeutral)
        {
            return;
        }

        await ApplyPerChannelAsync(range, left.IsNeutral ? null : left, right.IsNeutral ? null : right);
    }

    /// <summary>Berechnet Tempo/Tonhöhe pro Kanal im Hintergrund und übernimmt das Ergebnis.</summary>
    private async Task ApplyPerChannelAsync(SelectionRange range, TempoPitchEffect? left, TempoPitchEffect? right)
    {
        StopPlayback();
        string description = $"Tempo & Tonhöhe pro Kanal (L: {left?.Name ?? "unverändert"}; R: {right?.Name ?? "unverändert"})";
        var buffer = Buffer;
        bool hadSelection = HasSelection;

        IsBusy = true;
        StatusText = "Tempo & Tonhöhe werden pro Kanal berechnet …";
        try
        {
            var result = await Task.Run(() => PerChannelProcessor.Apply(buffer, range, [left, right]));
            var selectionAfter = hadSelection
                ? new SelectionRange(range.Start, range.Start + result.ProcessedLength)
                : Selection;
            _editor.ApplyBufferResult(description, result.Buffer, selectionAfter);

            int padded = result.PaddingFrames.Max();
            StatusText = padded == 0
                ? $"{description} ✓"
                : $"{description} ✓ – der {(result.PaddingFrames[0] > 0 ? "linke" : "rechte")} Kanal wurde am Ende " +
                  $"um {TimeFormatter.Format(buffer.FrameToTime(padded))} mit Stille verlängert.";
        }
        catch (Exception ex)
        {
            StatusText = "Tempo & Tonhöhe fehlgeschlagen.";
            _dialogs.ShowError($"Tempo und Tonhöhe konnten nicht geändert werden.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private Task FadeInAsync() => ApplyEffectAsync(new FadeEffect(FadeDirection.In));

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private Task FadeOutAsync() => ApplyEffectAsync(new FadeEffect(FadeDirection.Out));

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private Task ReverseAsync() => ApplyEffectAsync(new ReverseEffect());

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private Task MuteAsync() => ApplyEffectAsync(new MuteEffect());

    public void Dispose()
    {
        SaveEqualizerSettings();
        Equalizer.SettingsChanged -= OnEqualizerSettingsChanged;
        EqualizerRight.SettingsChanged -= OnEqualizerSettingsChanged;
        StopPlayback();
        _playback.PlaybackEnded -= OnPlaybackEnded;
        _playback.Dispose();
        Mixer.OpenClipInEditorRequested -= OnOpenClipInEditorRequested;
        Mixer.OpenNoteClipRequested -= OnOpenNoteClipRequested;
        Notes.Dispose();
        Mixer.Dispose();
    }

    // ───────────── Hilfsmethoden ─────────────

    private bool CanOpen() => !IsBusy;

    private bool CanApplyReverb() => HasAudio && !IsBusy && CreateReverbEffectForTarget() is not null;

    private bool CanApplyEqualizer() => HasAudio && !IsBusy && CreateEqualizerEffectForTarget() is not null;

    private bool CanUseStereo() => IsStereo && !IsBusy;


    // ───────────── Tonhöhe & Spektrum ─────────────

    private NoiseProfile? _noiseProfile;
    private (int Key, int Scale, double Speed, double Strength) _autotune = (0, 0, 40, 100);
    private (double From, double To, int Curve) _pitchRamp = (0, -12, 0);
    private (int Preset, double Mix) _voice = (0, 100);
    private (double Reduction, double Sensitivity) _noise = (15, 1.5);

    /// <summary>Spektrogramm unter der Wellenform anzeigen.</summary>
    [ObservableProperty]
    private bool _isSpectrogramVisible;

    /// <summary>Erkennt die Tonhöhe der Auswahl (bzw. der Datei) und zeigt sie in der Statuszeile.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task AnalyzePitchAsync()
    {
        var midi = await DetectPitchAsync();
        StatusText = midi is { } m
            ? $"Erkannte Tonhöhe: {Scales.Describe(m)} ({440 * Math.Pow(2, (m - 69) / 12):0.0} Hz)"
            : "Keine eindeutige Tonhöhe erkannt – für die Erkennung eignen sich einstimmige Stellen (Gesang, Soloinstrument).";
    }

    /// <summary>Tonhöhen-Korrektur: verschiebt die Auswahl um einen festen Wert, vorgeschlagen ist der Weg zum nächsten Halbton.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task CorrectPitchAsync()
    {
        var midi = await DetectPitchAsync();
        double suggestion = midi is { } m ? Math.Round((Math.Round(m) - m) * 100) : 0;
        string detected = midi is { } d ? $"Erkannt: {Scales.Describe(d)}. " : "Keine eindeutige Tonhöhe erkannt. ";
        var request = new ValueRequest("Tonhöhe korrigieren", "Um wie viele Cent soll die Tonhöhe verschoben werden?",
            detected + "Vorgeschlagen ist der Weg zum nächsten Halbton (100 Cent = 1 Halbton). Die Länge bleibt gleich.",
            Minimum: -1200, Maximum: 1200, Value: suggestion, SmallChange: 1, Unit: "Cent");
        if (_dialogs.AskValue(request) is { } cents && Math.Abs(cents) >= 1)
        {
            await ApplyEffectAsync(new PitchShiftEffect(cents / 100));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task AutotuneAsync()
    {
        var request = new ParameterRequest("Autotune",
            "Zieht gesungene oder gespielte Töne auf die nächste Note der Tonart. Wirkt auf die Auswahl (ohne Auswahl auf die ganze Datei) – am besten auf einer einzelnen Stimme.",
            [
                new ChoiceParameter("Tonart (Grundton)", Scales.KeyNames, _autotune.Key),
                new ChoiceParameter("Tonleiter", Scales.All.Select(Scales.NameOf).ToArray(), _autotune.Scale,
                    "„Chromatisch“ korrigiert auf jeden Halbton – passt immer, wenn man die Tonart nicht kennt."),
                new SliderParameter("Geschwindigkeit", 0, 400, _autotune.Speed, 5, "ms",
                    "0 ms = sofort (der bekannte Roboter-Effekt), 30 – 100 ms = natürliche Korrektur."),
                new SliderParameter("Stärke", 0, 100, _autotune.Strength, 5, "%", "100 % = ganz auf die Note, 50 % = halber Weg."),
            ]);
        if (_dialogs.AskParameters(request) is not { } values)
        {
            return;
        }

        _autotune = ((int)values[0], (int)values[1], values[2], values[3]);
        await ApplyEffectAsync(new AutotuneEffect(_autotune.Key, Scales.All[_autotune.Scale], _autotune.Speed, _autotune.Strength));
    }

    /// <summary>Parametrische Tonhöhe: Verlauf von einer Tonhöhe zu einer anderen über die Auswahl.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task PitchRampAsync()
    {
        var request = new ParameterRequest("Tonhöhen-Verlauf",
            "Die Tonhöhe ändert sich über die Auswahl stufenlos – z. B. von 0 nach -12 Halbtönen für einen „Plattenspieler-Stopp“, oder von -3 nach 0 für ein Hineingleiten.",
            [
                new SliderParameter("Am Anfang", -24, 24, _pitchRamp.From, 0.5, "Halbtöne"),
                new SliderParameter("Am Ende", -24, 24, _pitchRamp.To, 0.5, "Halbtöne"),
                new ChoiceParameter("Verlauf", ["Gleichmäßig", "Erst langsam, dann schnell", "Erst schnell, dann langsam"], _pitchRamp.Curve),
            ]);
        if (_dialogs.AskParameters(request) is not { } values)
        {
            return;
        }

        _pitchRamp = (values[0], values[1], (int)values[2]);
        double curve = _pitchRamp.Curve switch { 1 => 2.5, 2 => 0.4, _ => 1 };
        await ApplyEffectAsync(new PitchRampEffect(_pitchRamp.From, _pitchRamp.To, curve));
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task VoiceEffectAsync()
    {
        var request = new ParameterRequest("Stimmeffekte", "Fertige Effekte für Sprache und Gesang.",
            [
                new ChoiceParameter("Effekt", VoiceEffect.All.Select(VoiceEffect.NameOf).ToArray(), _voice.Preset),
                new SliderParameter("Mischung", 0, 100, _voice.Mix, 5, "%", "100 % = nur Effekt, kleinere Werte mischen das Original dazu."),
            ]);
        if (_dialogs.AskParameters(request) is not { } values)
        {
            return;
        }

        _voice = ((int)values[0], values[1]);
        await ApplyEffectAsync(new VoiceEffect(VoiceEffect.All[_voice.Preset], _voice.Mix));
    }

    /// <summary>Rauschprofil aus der Auswahl lernen (eine Stelle, an der nur Rauschen zu hören ist).</summary>
    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private async Task LearnNoiseProfileAsync()
    {
        var range = _editor.EffectRange;
        var slice = Buffer.Slice(range.Start, range.Length);
        try
        {
            _noiseProfile = await Task.Run(() => NoiseProfile.Learn(slice));
            StatusText = $"Rauschprofil gelernt ({TimeFormatter.Format(slice.Duration)}). Jetzt alles (oder einen Bereich) markieren und „Rauschen entfernen …“ wählen.";
            RemoveNoiseCommand.NotifyCanExecuteChanged();
        }
        catch (ArgumentException ex)
        {
            _dialogs.ShowError(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task RemoveNoiseAsync()
    {
        if (_noiseProfile is not { } profile)
        {
            _dialogs.ShowError("Zuerst ein Rauschprofil lernen: eine Stelle markieren, an der nur das Rauschen zu hören ist (z. B. eine Pause), dann „Rauschprofil aus Auswahl lernen“ wählen.");
            return;
        }

        if (profile.SampleRate != Buffer.SampleRate)
        {
            _dialogs.ShowError("Das Rauschprofil stammt aus einer Datei mit anderer Abtastrate. Bitte das Profil in dieser Datei neu lernen.");
            return;
        }

        var request = new ParameterRequest("Rauschen entfernen",
            "Senkt alle Anteile ab, die kaum lauter sind als das gelernte Rauschen. Wirkt auf die Auswahl, ohne Auswahl auf die ganze Datei.",
            [
                new SliderParameter("Absenkung", 3, 40, _noise.Reduction, 1, "dB", "Wie stark das Rauschen leiser wird. 10 – 20 dB klingen meist natürlich."),
                new SliderParameter("Empfindlichkeit", 1, 4, _noise.Sensitivity, 0.1, "×",
                    "Höher = mehr wird als Rauschen behandelt (gründlicher, aber leise Nutzanteile können dumpfer werden)."),
            ]);
        if (_dialogs.AskParameters(request) is not { } values)
        {
            return;
        }

        _noise = (values[0], values[1]);
        await ApplyEffectAsync(new NoiseReductionEffect(profile, _noise.Reduction, _noise.Sensitivity));
    }

    private async Task<double?> DetectPitchAsync()
    {
        var range = _editor.EffectRange;
        var slice = Buffer.Slice(range.Start, range.Length);
        IsBusy = true;
        StatusText = "Tonhöhe wird erkannt …";
        try
        {
            return await Task.Run(() => PitchDetector.Track(slice).MedianMidi());
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUseAudio() => HasAudio && !IsBusy;

    private bool CanUseSelection() => HasSelection && !IsBusy;

    private bool CanPaste() => _editor.CanPaste && !IsBusy;

    private bool CanUndo() => _editor.CanUndo && !IsBusy;

    private bool CanRedo() => _editor.CanRedo && !IsBusy;

    private async Task LoadAsync(string path)
    {
        StopPlayback();
        IsBusy = true;
        StatusText = $"Lade „{Path.GetFileName(path)}“ …";
        try
        {
            var (buffer, markers) = await Task.Run(() => (
                _files.Load(path),
                AudioFileService.SupportsMarkers(path) ? WavMarkers.Read(path) : MarkerList.Empty));
            _editor.Load(buffer, markers);
            FilePath = path;
            DocumentName = null;
            LinkedClip = null;
            PlayheadFrame = 0;
            _settings.AddRecentFile(path);
            RefreshRecentFiles();
            StatusText = $"„{FileName}“ geladen – Länge {TimeFormatter.Format(buffer.Duration, false)}.";
            DocumentLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = "Laden fehlgeschlagen.";
            _dialogs.ShowError($"Die Datei konnte nicht geöffnet werden.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Schaltet den Live-Equalizer zwischen Puffer und Soundkarte.</summary>
    private ISampleProvider AttachLiveEqualizer(ISampleProvider source)
    {
        var processor = new EqualizerProcessor(
            source.WaveFormat.SampleRate, source.WaveFormat.Channels, EffectiveEqualizerSettings());
        _liveEqualizer = processor;

        // Stereo: ein Analysator pro Kanal, sonst einer für die Summe.
        int analyzerCount = source.WaveFormat.Channels == 2 ? 2 : 1;
        _spectrumAnalyzers = Enumerable.Range(0, analyzerCount)
            .Select(_ => new SpectrumAnalyzer(source.WaveFormat.SampleRate))
            .ToArray();

        // Stummschalten gilt nur für Stereo – eine Mono-Datei bleibt immer hörbar.
        _channelMonitor = new ChannelMonitorSampleProvider(source, IsStereo ? [!HearLeft, !HearRight] : []);

        // Reihenfolge: Puffer → Kanal-Stummschaltung → Equalizer → Spektrum-Abgriff → Soundkarte.
        // Das Spektrum zeigt also genau das, was zu hören ist.
        int rate = source.WaveFormat.SampleRate;
        int channels = source.WaveFormat.Channels;
        var filter = new FilterProcessor(rate, channels, EffectiveFilterSettings());
        var distortion = new DistortionProcessor(rate, channels, EffectiveDistortionSettings());
        var compressor = new CompressorProcessor(rate, channels, EffectiveCompressorSettings());
        var chorus = new ChorusProcessor(rate, channels, EffectiveChorusSettings());
        var flanger = new FlangerProcessor(rate, channels, EffectiveFlangerSettings());
        var delay = new DelayProcessor(rate, channels, EffectiveDelaySettings());
        var reverb = new ReverbProcessor(rate, channels, EffectiveReverbSettings());
        _liveFilter = filter;
        _liveDistortion = distortion;
        _liveCompressor = compressor;
        _liveChorus = chorus;
        _liveFlanger = flanger;
        _liveDelay = delay;
        _liveReverb = reverb;

        // … → Filter → Equalizer → Verzerrung → Kompressor → Chorus → Flanger → Echo → Hall → Spektrum → Soundkarte.
        ISampleProvider chain = new ProcessingSampleProvider(WithPreStudioEffects(_channelMonitor), filter.Process);
        chain = new EqualizerSampleProvider(chain, processor);
        chain = WithSaturation(chain);
        chain = new ProcessingSampleProvider(chain, distortion.Process);
        chain = new ProcessingSampleProvider(chain, compressor.Process);
        chain = new ProcessingSampleProvider(chain, chorus.Process);
        chain = new ProcessingSampleProvider(chain, flanger.Process);
        chain = WithModulationStudioEffects(chain);
        chain = new ProcessingSampleProvider(chain, delay.Process);
        chain = new ReverbSampleProvider(chain, reverb);
        return WithLiveLoudness(new SpectrumTapSampleProvider(chain, _spectrumAnalyzers));
    }

    private void UpdateSpectrum()
    {
        if (_spectrumAnalyzers is null)
        {
            return;
        }

        var spectra = new float[_spectrumAnalyzers.Length][];
        for (int c = 0; c < spectra.Length; c++)
        {
            var bands = _spectrumAnalyzers[c].GetBands(SpectrumBands);
            if (Spectra is { } previous && previous.Length == spectra.Length && previous[c].Length == SpectrumBands)
            {
                // Sofort steigen, langsam fallen – so wirkt die Anzeige ruhig und gut ablesbar.
                for (int i = 0; i < bands.Length; i++)
                {
                    bands[i] = Math.Max(bands[i], previous[c][i] - SpectrumFallPerFrame);
                }
            }

            spectra[c] = bands;
        }

        Spectra = spectra;
    }

    private void UpdateEqualizerCurve()
    {
        int sampleRate = HasAudio ? Buffer.SampleRate : 44100;
        var settings = EffectiveEqualizerSettings();
        EqualizerCurve = settings[0].ResponseDb(sampleRate, CurveFrequencies);
        EqualizerCurveRight = settings.Length > 1 ? settings[1].ResponseDb(sampleRate, CurveFrequencies) : null;
    }

    /// <summary>
    /// Was gerade zu hören sein soll: eine Einstellung für alle Kanäle oder (Pro-Kanal-EQ) links und
    /// rechts getrennt; neutral, wenn der EQ ausgeschaltet ist.
    /// </summary>
    private EqualizerSettings[] EffectiveEqualizerSettings()
    {
        if (!IsEqualizerEnabled)
        {
            return [EqualizerSettings.Neutral];
        }

        return UsesPerChannelEqualizer
            ? [Equalizer.ToSettings(), EqualizerRight.ToSettings()]
            : [Equalizer.ToSettings()];
    }

    /// <summary>EQ-Effekt für Export: alle Kanäle, jeder mit seiner Einstellung.</summary>
    private EqualizerEffect? CreateEqualizerEffectForAllChannels() =>
        IsEqualizerActive ? new EqualizerEffect(EffectiveEqualizerSettings(), EqualizerPreventClipping) : null;

    /// <summary>
    /// EQ-Effekt für „In Audio übernehmen“ unter Berücksichtigung von „Bearbeiten L/R“
    /// (wird in <see cref="ApplyEffectAsync"/> bei Bedarf auf einen Kanal beschränkt).
    /// Null, wenn sich nichts ändern würde.
    /// </summary>
    private EqualizerEffect? CreateEqualizerEffectForTarget()
    {
        if (!IsEqualizerEnabled)
        {
            return null;
        }

        if (EditsAllChannels)
        {
            return CreateEqualizerEffectForAllChannels();
        }

        // Nur ein Kanal: dessen Einstellung verwenden (gemeinsamer EQ oder die passende Seite).
        var settings = UsesPerChannelEqualizer && !EditLeft ? EqualizerRight.ToSettings() : Equalizer.ToSettings();
        return settings.IsNeutral ? null : new EqualizerEffect(settings, EqualizerPreventClipping);
    }

    /// <summary>Hall-Einstellung, wie sie gerade zu hören sein soll (aus = kein Hall).</summary>
    private ReverbSettings EffectiveReverbSettings()
    {
        if (!IsReverbEnabled)
        {
            return ReverbSettings.Off;
        }

        double[] amounts = IsStereo ? [ReverbLeft, ReverbRight] : [ReverbLeft];
        return new ReverbSettings(amounts, ReverbRoomSize, ReverbDamping);
    }

    /// <summary>
    /// Hall-Effekt für „In Audio übernehmen“ unter Berücksichtigung von „Bearbeiten L/R“.
    /// Null, wenn sich nichts ändern würde.
    /// </summary>
    private ReverbEffect? CreateReverbEffectForTarget()
    {
        if (!IsReverbEnabled || !HasAudio)
        {
            return null;
        }

        if (EditsAllChannels)
        {
            var settings = EffectiveReverbSettings();
            if (settings.IsNeutral)
            {
                return null;
            }

            // Ganze Datei: hinten Platz lassen, damit der Hall natürlich ausklingt.
            double tail = _editor.HasSelection ? 0 : settings.TailSeconds;
            return new ReverbEffect(settings, tail, EqualizerPreventClipping);
        }

        // Nur ein Kanal (die Länge muss gleich bleiben → kein Ausklingen anhängen).
        int channel = EditLeft ? 0 : 1;
        double amount = channel == 0 ? ReverbLeft : ReverbRight;
        return amount <= 0
            ? null
            : new ReverbEffect(new ReverbSettings([amount], ReverbRoomSize, ReverbDamping), 0,
                EqualizerPreventClipping, channelOffset: channel);
    }

    /// <summary>Namen der aktiven Live-Effekte für Rückfragen, z. B. „Equalizer, Echo, Hall“.</summary>
    private string LiveEffectsName => string.Join(", ", new (bool Active, string Name)[]
    {
        (IsFilterActive, "Filter"),
        (IsEqualizerActive, "Equalizer"),
        (IsDistortionActive, "Verzerrung"),
        (IsCompressorActive, "Kompressor"),
        (IsChorusActive, "Chorus"),
        (IsFlangerActive, "Flanger"),
        (IsDelayActive, "Echo"),
        (IsReverbActive, "Hall"),
    }.Where(e => e.Active).Select(e => e.Name).Concat(ActiveStudioEffectNames()));

    /// <summary>Fragt nach, ob aktive Live-Effekte beim Export eingerechnet werden sollen.</summary>
    private IAudioEffect[] AskIncludeLiveEffects(string question)
    {
        if (!AreLiveEffectsActive || !_dialogs.Confirm(question, "Live-Effekte beim Export"))
        {
            return [];
        }

        var effects = new List<IAudioEffect>();
        var studio = StudioEffectsForExport();
        effects.AddRange(studio.Before);
        if (IsFilterActive)
        {
            effects.Add(new FilterEffect(EffectiveFilterSettings()));
        }

        if (CreateEqualizerEffectForAllChannels() is { } equalizer)
        {
            effects.Add(equalizer);
        }

        effects.AddRange(studio.AfterEq);

        if (IsDistortionActive)
        {
            effects.Add(new DistortionEffect(EffectiveDistortionSettings(), EqualizerPreventClipping));
        }

        if (IsCompressorActive)
        {
            effects.Add(new CompressorEffect(EffectiveCompressorSettings(), EqualizerPreventClipping));
        }

        if (IsChorusActive)
        {
            effects.Add(new ChorusEffect(EffectiveChorusSettings()));
        }

        if (IsFlangerActive)
        {
            effects.Add(new FlangerEffect(EffectiveFlangerSettings(), preventClipping: EqualizerPreventClipping));
        }

        effects.AddRange(studio.AfterModulation);
        if (IsDelayActive)
        {
            var delay = EffectiveDelaySettings();
            effects.Add(new DelayEffect(delay, delay.TailSeconds, EqualizerPreventClipping));
        }

        if (IsReverbActive)
        {
            var reverb = EffectiveReverbSettings();
            effects.Add(new ReverbEffect(reverb, reverb.TailSeconds, EqualizerPreventClipping));
        }

        return effects.ToArray();
    }

    private static AudioBuffer ApplyAll(IEnumerable<IAudioEffect> effects, AudioBuffer buffer)
    {
        foreach (var effect in effects)
        {
            buffer = effect.Apply(buffer);
        }

        return buffer;
    }

    private void RefreshReverb()
    {
        _liveReverb?.Update(EffectiveReverbSettings());
        OnPropertyChanged(nameof(EqualizerIndicator));
        OnPropertyChanged(nameof(IsReverbAudible));
        ApplyReverbCommand.NotifyCanExecuteChanged();
    }

    // ───────────── Kompressor & Echo: Einstellungen, Live-Aktualisierung ─────────────

    /// <summary>Kompressor-Einstellung, wie sie gerade zu hören sein soll (aus = keine Wirkung).</summary>
    private CompressorSettings EffectiveCompressorSettings()
    {
        if (!IsCompressorEnabled)
        {
            return CompressorSettings.Off;
        }

        double[] mix = IsStereo ? [CompressorMixLeft, CompressorMixRight] : [CompressorMixLeft];
        return new CompressorSettings(mix, CompressorThreshold, CompressorRatio, CompressorAttack,
            CompressorRelease, CompressorMakeup, CompressorLinked);
    }

    /// <summary>Echo-Einstellung, wie sie gerade zu hören sein soll (aus = kein Echo).</summary>
    private DelaySettings EffectiveDelaySettings()
    {
        if (!IsDelayEnabled)
        {
            return DelaySettings.Off;
        }

        return IsStereo
            ? new DelaySettings([DelayTimeLeft, DelayTimeRight], [DelayMixLeft, DelayMixRight], DelayFeedback, DelayDamping, DelayPingPong)
            : new DelaySettings([DelayTimeLeft], [DelayMixLeft], DelayFeedback, DelayDamping);
    }

    /// <summary>
    /// Kompressor für „In Audio übernehmen“ unter Berücksichtigung von „Bearbeiten L/R“; null, wenn
    /// sich nichts ändern würde.
    /// </summary>
    private CompressorEffect? CreateCompressorEffectForTarget()
    {
        if (!IsCompressorEnabled || !HasAudio)
        {
            return null;
        }

        if (EditsAllChannels)
        {
            var settings = EffectiveCompressorSettings();
            return settings.IsNeutral ? null : new CompressorEffect(settings, EqualizerPreventClipping);
        }

        double mix = EditLeft ? CompressorMixLeft : CompressorMixRight;
        return mix <= 0
            ? null
            : new CompressorEffect(new CompressorSettings([mix], CompressorThreshold, CompressorRatio,
                CompressorAttack, CompressorRelease, CompressorMakeup, CompressorLinked), EqualizerPreventClipping);
    }

    /// <summary>Echo für „In Audio übernehmen“; bei der ganzen Datei mit Ausklingen am Ende.</summary>
    private DelayEffect? CreateDelayEffectForTarget()
    {
        if (!IsDelayEnabled || !HasAudio)
        {
            return null;
        }

        if (EditsAllChannels)
        {
            var settings = EffectiveDelaySettings();
            if (settings.IsNeutral)
            {
                return null;
            }

            double tail = _editor.HasSelection ? 0 : settings.TailSeconds;
            return new DelayEffect(settings, tail, EqualizerPreventClipping);
        }

        // Nur ein Kanal (Länge bleibt, kein Ping-Pong möglich).
        bool left = EditLeft;
        double mixOne = left ? DelayMixLeft : DelayMixRight;
        return mixOne <= 0
            ? null
            : new DelayEffect(new DelaySettings([left ? DelayTimeLeft : DelayTimeRight], [mixOne], DelayFeedback, DelayDamping),
                0, EqualizerPreventClipping);
    }

    [RelayCommand(CanExecute = nameof(CanApplyCompressor))]
    private async Task ApplyCompressorAsync()
    {
        var effect = CreateCompressorEffectForTarget();
        if (effect is null || !await ApplyEffectAsync(effect))
        {
            return;
        }

        // Fest eingerechnet → Stärke der übernommenen Kanäle auf 0, damit nichts doppelt wirkt.
        if (!IsStereo || EditLeft)
        {
            CompressorMixLeft = 0;
        }

        if (IsStereo && EditRight)
        {
            CompressorMixRight = 0;
        }

        StatusText = $"{effect.Name} in das Audio übernommen – der Stärke-Regler steht wieder auf 0.";
    }

    [RelayCommand(CanExecute = nameof(CanApplyDelay))]
    private async Task ApplyDelayAsync()
    {
        var effect = CreateDelayEffectForTarget();
        if (effect is null || !await ApplyEffectAsync(effect))
        {
            return;
        }

        bool left = !IsStereo || EditLeft;
        bool right = IsStereo && EditRight;
        if (left)
        {
            DelayMixLeft = 0;
        }

        if (right)
        {
            DelayMixRight = 0;
        }

        StatusText = $"{effect.Name} in das Audio übernommen – der Stärke-Regler steht wieder auf 0.";
    }

    private bool CanApplyCompressor() => HasAudio && !IsBusy && CreateCompressorEffectForTarget() is not null;

    private bool CanApplyDelay() => HasAudio && !IsBusy && CreateDelayEffectForTarget() is not null;

    private void RefreshCompressor()
    {
        _liveCompressor?.Update(EffectiveCompressorSettings());
        OnPropertyChanged(nameof(EqualizerIndicator));
        OnPropertyChanged(nameof(IsCompressorAudible));
        ApplyCompressorCommand.NotifyCanExecuteChanged();
        if (!IsCompressorEnabled)
        {
            CompressorReductionLeft = CompressorReductionRight = 0;
        }
    }

    private void RefreshDelay()
    {
        _liveDelay?.Update(EffectiveDelaySettings());
        OnPropertyChanged(nameof(EqualizerIndicator));
        OnPropertyChanged(nameof(IsDelayAudible));
        ApplyDelayCommand.NotifyCanExecuteChanged();
    }


    // ───────────── Filter, Verzerrung, Chorus, Flanger ─────────────

    private static double PositionToHz(double position) =>
        FilterSettings.MinHz * Math.Pow(FilterSettings.MaxHz / FilterSettings.MinHz, Math.Clamp(position, 0, 100) / 100);

    private static string FormatHz(double hz) => hz >= 1000
        ? string.Format(CultureInfo.CurrentCulture, "{0:0.#} kHz", hz / 1000)
        : string.Format(CultureInfo.CurrentCulture, "{0:0} Hz", hz);

    private FilterSettings EffectiveFilterSettings() => IsFilterEnabled
        ? new FilterSettings(
            FilterHighPassPosition <= 0.05 ? FilterSettings.MinHz : PositionToHz(FilterHighPassPosition),
            FilterLowPassPosition >= 99.95 ? FilterSettings.MaxHz : PositionToHz(FilterLowPassPosition))
        : FilterSettings.Off;

    private DistortionSettings EffectiveDistortionSettings() => IsDistortionEnabled
        ? new DistortionSettings(IsStereo ? [DistortionMixLeft, DistortionMixRight] : [DistortionMixLeft],
            (DistortionType)DistortionTypeIndex, DistortionDrive, DistortionTone)
        : DistortionSettings.Off;

    private ModulationSettings EffectiveChorusSettings() => IsChorusEnabled
        ? new ModulationSettings(IsStereo ? [ChorusMixLeft, ChorusMixRight] : [ChorusMixLeft], ChorusRate, ChorusDepth)
        : ModulationSettings.Off;

    private ModulationSettings EffectiveFlangerSettings() => IsFlangerEnabled
        ? new ModulationSettings(IsStereo ? [FlangerMixLeft, FlangerMixRight] : [FlangerMixLeft], FlangerRate, FlangerDepth, FlangerFeedback)
        : ModulationSettings.Off;

    /// <summary>Stärke des Ziel-Kanals bei „Bearbeiten nur L/R“ (bei allen Kanälen: null).</summary>
    private int? SingleTargetChannel => EditsAllChannels ? null : (EditLeft ? 0 : 1);

    private FilterEffect? CreateFilterEffectForTarget()
    {
        var settings = EffectiveFilterSettings();
        return HasAudio && !settings.IsNeutral ? new FilterEffect(settings) : null;
    }

    private DistortionEffect? CreateDistortionEffectForTarget()
    {
        if (!HasAudio || !IsDistortionEnabled)
        {
            return null;
        }

        var all = EffectiveDistortionSettings();
        var settings = SingleTargetChannel is { } c
            ? new DistortionSettings([all.MixFor(c)], all.Type, all.DrivePercent, all.TonePercent)
            : all;
        return settings.IsNeutral ? null : new DistortionEffect(settings, EqualizerPreventClipping);
    }

    private IAudioEffect? CreateModulationEffectForTarget(bool flanger)
    {
        if (!HasAudio || !(flanger ? IsFlangerEnabled : IsChorusEnabled))
        {
            return null;
        }

        var all = flanger ? EffectiveFlangerSettings() : EffectiveChorusSettings();
        int offset = SingleTargetChannel ?? 0;
        var settings = SingleTargetChannel is { } c
            ? new ModulationSettings([all.MixFor(c)], all.RateHz, all.DepthPercent, all.FeedbackPercent)
            : all;
        if (settings.IsNeutral)
        {
            return null;
        }

        return flanger
            ? new FlangerEffect(settings, offset, EqualizerPreventClipping)
            : new ChorusEffect(settings, offset);
    }

    [RelayCommand(CanExecute = nameof(CanApplyFilter))]
    private async Task ApplyFilterAsync()
    {
        if (CreateFilterEffectForTarget() is not { } effect || !await ApplyEffectAsync(effect))
        {
            return;
        }

        if (EditsAllChannels)
        {
            FilterHighPassPosition = 0;
            FilterLowPassPosition = 100;
            StatusText = $"{effect.Name} in das Audio übernommen – der Filter steht wieder auf „aus“.";
        }
        else
        {
            StatusText = $"{effect.Name} in einen Kanal übernommen – live wirkt der Filter weiter auf beide Kanäle.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyDistortion))]
    private async Task ApplyDistortionAsync()
    {
        if (CreateDistortionEffectForTarget() is not { } effect || !await ApplyEffectAsync(effect))
        {
            return;
        }

        ResetMix(v => DistortionMixLeft = v, v => DistortionMixRight = v);
        StatusText = $"{effect.Name} in das Audio übernommen – der Stärke-Regler steht wieder auf 0.";
    }

    [RelayCommand(CanExecute = nameof(CanApplyChorus))]
    private async Task ApplyChorusAsync()
    {
        if (CreateModulationEffectForTarget(flanger: false) is not { } effect || !await ApplyEffectAsync(effect))
        {
            return;
        }

        ResetMix(v => ChorusMixLeft = v, v => ChorusMixRight = v);
        StatusText = "Chorus in das Audio übernommen – der Stärke-Regler steht wieder auf 0.";
    }

    [RelayCommand(CanExecute = nameof(CanApplyFlanger))]
    private async Task ApplyFlangerAsync()
    {
        if (CreateModulationEffectForTarget(flanger: true) is not { } effect || !await ApplyEffectAsync(effect))
        {
            return;
        }

        ResetMix(v => FlangerMixLeft = v, v => FlangerMixRight = v);
        StatusText = "Flanger in das Audio übernommen – der Stärke-Regler steht wieder auf 0.";
    }

    /// <summary>Nach dem Übernehmen die Stärke der betroffenen Kanäle auf 0 setzen.</summary>
    private void ResetMix(Action<double> setLeft, Action<double> setRight)
    {
        if (!IsStereo || EditLeft)
        {
            setLeft(0);
        }

        if (IsStereo && EditRight)
        {
            setRight(0);
        }
    }

    private bool CanApplyFilter() => !IsBusy && CreateFilterEffectForTarget() is not null;

    private bool CanApplyDistortion() => !IsBusy && CreateDistortionEffectForTarget() is not null;

    private bool CanApplyChorus() => !IsBusy && CreateModulationEffectForTarget(flanger: false) is not null;

    private bool CanApplyFlanger() => !IsBusy && CreateModulationEffectForTarget(flanger: true) is not null;

    private void RefreshColorEffectCommands()
    {
        ApplyFilterCommand.NotifyCanExecuteChanged();
        ApplyDistortionCommand.NotifyCanExecuteChanged();
        ApplyChorusCommand.NotifyCanExecuteChanged();
        ApplyFlangerCommand.NotifyCanExecuteChanged();
    }

    private void RefreshColorEffects()
    {
        _liveFilter?.Update(EffectiveFilterSettings());
        _liveDistortion?.Update(EffectiveDistortionSettings());
        _liveChorus?.Update(EffectiveChorusSettings());
        _liveFlanger?.Update(EffectiveFlangerSettings());
        OnPropertyChanged(nameof(EqualizerIndicator));
        OnPropertyChanged(nameof(IsFilterAudible));
        OnPropertyChanged(nameof(IsDistortionAudible));
        OnPropertyChanged(nameof(IsChorusAudible));
        OnPropertyChanged(nameof(IsFlangerAudible));
        RefreshColorEffectCommands();
    }

    partial void OnIsFilterEnabledChanged(bool value) => RefreshColorEffects();

    partial void OnFilterHighPassPositionChanged(double value) => RefreshColorEffects();

    partial void OnFilterLowPassPositionChanged(double value) => RefreshColorEffects();

    partial void OnIsDistortionEnabledChanged(bool value) => RefreshColorEffects();

    partial void OnDistortionTypeIndexChanged(int value) => RefreshColorEffects();

    partial void OnDistortionDriveChanged(double value) => RefreshColorEffects();

    partial void OnDistortionToneChanged(double value) => RefreshColorEffects();

    partial void OnDistortionMixLeftChanged(double value) => RefreshColorEffects();

    partial void OnDistortionMixRightChanged(double value) => RefreshColorEffects();

    partial void OnIsChorusEnabledChanged(bool value) => RefreshColorEffects();

    partial void OnChorusRateChanged(double value) => RefreshColorEffects();

    partial void OnChorusDepthChanged(double value) => RefreshColorEffects();

    partial void OnChorusMixLeftChanged(double value) => RefreshColorEffects();

    partial void OnChorusMixRightChanged(double value) => RefreshColorEffects();

    partial void OnIsFlangerEnabledChanged(bool value) => RefreshColorEffects();

    partial void OnFlangerRateChanged(double value) => RefreshColorEffects();

    partial void OnFlangerDepthChanged(double value) => RefreshColorEffects();

    partial void OnFlangerFeedbackChanged(double value) => RefreshColorEffects();

    partial void OnFlangerMixLeftChanged(double value) => RefreshColorEffects();

    partial void OnFlangerMixRightChanged(double value) => RefreshColorEffects();

    // ───────────── Schleife & Wiedergabegerät ─────────────

    /// <summary>Strg+L bzw. Knopf: Schleife ein/aus (wirkt sofort, auch während der Wiedergabe).</summary>
    [RelayCommand]
    private void ToggleLoop()
    {
        if (UsesMixerTransport)
        {
            Mixer.ToggleLoop();
            return;
        }

        IsLooping = !IsLooping;
    }

    partial void OnIsLoopingChanged(bool value)
    {
        _playback.Loop = value;
        StatusText = value ? "Schleife ein – die Auswahl (bzw. die Datei) wird endlos wiederholt." : "Schleife aus.";
    }

    [RelayCommand]
    private void SelectOutputDevice(OutputDeviceItem? device)
    {
        if (device is null)
        {
            return;
        }

        StopPlayback();
        _playback.DeviceNumber = device.Number;
        Mixer.SetOutputDevice(device.Number);
        Notes.SetOutputDevice(device.Number);
        foreach (var item in OutputDevices)
        {
            item.IsSelected = ReferenceEquals(item, device);
        }

        _settings.Settings.OutputDeviceName = device.Number < 0 ? null : device.Name;
        _settings.Save();
        StatusText = $"Wiedergabegerät: {device.Name}";
    }

    [RelayCommand]
    private void RefreshOutputDevices() =>
        RefreshOutputDevices(OutputDevices.FirstOrDefault(d => d.IsSelected) is { Number: >= 0 } current ? current.Name : null);

    private void RefreshOutputDevices(string? selectedName)
    {
        OutputDevices.Clear();
        foreach (var device in AudioDevices.List())
        {
            OutputDevices.Add(new OutputDeviceItem(device.Number, device.Name, SelectOutputDeviceCommand));
        }

        var selected = OutputDevices.FirstOrDefault(d => d.Number >= 0 && d.Name == selectedName) ?? OutputDevices[0];
        selected.IsSelected = true;
        _playback.DeviceNumber = selected.Number;
        Mixer?.SetOutputDevice(selected.Number);
        Notes?.SetOutputDevice(selected.Number);
    }

    partial void OnIsCompressorEnabledChanged(bool value) => RefreshCompressor();

    partial void OnCompressorThresholdChanged(double value) => RefreshCompressor();

    partial void OnCompressorRatioChanged(double value) => RefreshCompressor();

    partial void OnCompressorAttackChanged(double value) => RefreshCompressor();

    partial void OnCompressorReleaseChanged(double value) => RefreshCompressor();

    partial void OnCompressorMakeupChanged(double value) => RefreshCompressor();

    partial void OnCompressorMixLeftChanged(double value) => RefreshCompressor();

    partial void OnCompressorMixRightChanged(double value) => RefreshCompressor();

    partial void OnCompressorLinkedChanged(bool value) => RefreshCompressor();

    partial void OnIsDelayEnabledChanged(bool value) => RefreshDelay();

    partial void OnDelayTimeLeftChanged(double value) => RefreshDelay();

    partial void OnDelayTimeRightChanged(double value) => RefreshDelay();

    partial void OnDelayMixLeftChanged(double value) => RefreshDelay();

    partial void OnDelayMixRightChanged(double value) => RefreshDelay();

    partial void OnDelayFeedbackChanged(double value) => RefreshDelay();

    partial void OnDelayDampingChanged(double value) => RefreshDelay();

    partial void OnDelayPingPongChanged(bool value) => RefreshDelay();

    partial void OnIsReverbEnabledChanged(bool value) => RefreshReverb();

    partial void OnReverbLeftChanged(double value) => RefreshReverb();

    partial void OnReverbRightChanged(double value) => RefreshReverb();

    partial void OnReverbRoomSizeChanged(double value) => RefreshReverb();

    partial void OnReverbDampingChanged(double value) => RefreshReverb();

    private static EqualizerSettings LoadEqualizerSettings(double[]? gains, double preampDb) =>
        gains is { Length: var n } && n == EqualizerSettings.BandFrequencies.Count
            ? new EqualizerSettings(gains, preampDb)
            : EqualizerSettings.Neutral;

    private void OnEqualizerSettingsChanged(object? sender, EventArgs e) => RefreshEqualizer();

    /// <summary>Überträgt den aktuellen EQ-Zustand sofort in Wiedergabe, Kurven und Anzeigen.</summary>
    private void RefreshEqualizer()
    {
        // Sofort hörbar: der laufende Filter übernimmt die neuen Werte ohne Neustart.
        _liveEqualizer?.Update(EffectiveEqualizerSettings());
        UpdateEqualizerCurve();
        OnPropertyChanged(nameof(EqualizerIndicator));
        ApplyEqualizerCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsEqualizerEnabledChanged(bool value) => RefreshEqualizer();

    partial void OnIsEqualizerPerChannelChanged(bool value)
    {
        if (value && EqualizerRight.IsNeutral && !Equalizer.IsNeutral)
        {
            // Beim Einschalten übernimmt rechts zunächst die bisherige Einstellung – es ändert sich nichts Hörbares.
            EqualizerRight.LoadSettings(Equalizer.ToSettings());
        }

        OnPropertyChanged(nameof(ActiveEqualizerCurve));
        RefreshEqualizer();
    }

    partial void OnEditLeftChanged(bool value)
    {
        if (!value && !EditRight)
        {
            EditRight = true; // mindestens ein Kanal bleibt Ziel der Effekte
        }

        TempoPitchCommand.NotifyCanExecuteChanged();
        ApplyEqualizerCommand.NotifyCanExecuteChanged();
        ApplyReverbCommand.NotifyCanExecuteChanged();
        ApplyCompressorCommand.NotifyCanExecuteChanged();
        ApplyDelayCommand.NotifyCanExecuteChanged();
        RefreshColorEffectCommands();
    }

    partial void OnEditRightChanged(bool value)
    {
        if (!value && !EditLeft)
        {
            EditLeft = true;
        }

        TempoPitchCommand.NotifyCanExecuteChanged();
        ApplyEqualizerCommand.NotifyCanExecuteChanged();
        ApplyReverbCommand.NotifyCanExecuteChanged();
        ApplyCompressorCommand.NotifyCanExecuteChanged();
        ApplyDelayCommand.NotifyCanExecuteChanged();
        RefreshColorEffectCommands();
    }

    partial void OnHearLeftChanged(bool value)
    {
        if (IsStereo)
        {
            _channelMonitor?.SetMuted(0, !value);
        }
    }

    partial void OnHearRightChanged(bool value)
    {
        if (IsStereo)
        {
            _channelMonitor?.SetMuted(1, !value);
        }
    }

    partial void OnIsEqualizerVisibleChanged(bool value)
    {
        _settings.Settings.EqualizerVisible = value;
        _settings.Save();
    }

    private void SaveEqualizerSettings()
    {
        var saved = _settings.Settings;
        var settings = Equalizer.ToSettings();
        var right = EqualizerRight.ToSettings();
        saved.EqualizerGainsDb = settings.GainsDb.ToArray();
        saved.EqualizerPreampDb = settings.PreampDb;
        saved.EqualizerRightGainsDb = right.GainsDb.ToArray();
        saved.EqualizerRightPreampDb = right.PreampDb;
        saved.EqualizerEnabled = IsEqualizerEnabled;
        saved.EqualizerPreventClipping = EqualizerPreventClipping;
        saved.EqualizerPerChannel = IsEqualizerPerChannel;
        saved.ReverbEnabled = IsReverbEnabled;
        saved.ReverbLeftPercent = ReverbLeft;
        saved.ReverbRightPercent = ReverbRight;
        saved.ReverbRoomSizePercent = ReverbRoomSize;
        saved.ReverbDampingPercent = ReverbDamping;
        saved.CompressorEnabled = IsCompressorEnabled;
        saved.CompressorThresholdDb = CompressorThreshold;
        saved.CompressorRatio = CompressorRatio;
        saved.CompressorAttackMs = CompressorAttack;
        saved.CompressorReleaseMs = CompressorRelease;
        saved.CompressorMakeupDb = CompressorMakeup;
        saved.CompressorMixLeft = CompressorMixLeft;
        saved.CompressorMixRight = CompressorMixRight;
        saved.CompressorLinked = CompressorLinked;
        saved.DelayEnabled = IsDelayEnabled;
        saved.DelayTimeLeftMs = DelayTimeLeft;
        saved.DelayTimeRightMs = DelayTimeRight;
        saved.DelayMixLeft = DelayMixLeft;
        saved.DelayMixRight = DelayMixRight;
        saved.DelayFeedbackPercent = DelayFeedback;
        saved.DelayDampingPercent = DelayDamping;
        saved.DelayPingPong = DelayPingPong;
        saved.FilterEnabled = IsFilterEnabled;
        saved.FilterHighPassPosition = FilterHighPassPosition;
        saved.FilterLowPassPosition = FilterLowPassPosition;
        saved.DistortionEnabled = IsDistortionEnabled;
        saved.DistortionType = DistortionTypeIndex;
        saved.DistortionDrive = DistortionDrive;
        saved.DistortionTone = DistortionTone;
        saved.DistortionMixLeft = DistortionMixLeft;
        saved.DistortionMixRight = DistortionMixRight;
        saved.ChorusEnabled = IsChorusEnabled;
        saved.ChorusRate = ChorusRate;
        saved.ChorusDepth = ChorusDepth;
        saved.ChorusMixLeft = ChorusMixLeft;
        saved.ChorusMixRight = ChorusMixRight;
        saved.FlangerEnabled = IsFlangerEnabled;
        saved.FlangerRate = FlangerRate;
        saved.FlangerDepth = FlangerDepth;
        saved.FlangerFeedback = FlangerFeedback;
        saved.FlangerMixLeft = FlangerMixLeft;
        saved.FlangerMixRight = FlangerMixRight;
        _settings.Save();
    }

    /// <summary>
    /// Berechnet einen Effekt im Hintergrund (die Oberfläche bleibt bedienbar) und übernimmt
    /// das Ergebnis anschließend im UI-Thread als rückgängig machbaren Schritt.
    /// </summary>
    private async Task<bool> ApplyEffectAsync(IAudioEffect effect, bool respectChannelTarget = true)
    {
        if (!HasAudio)
        {
            return false;
        }

        if (respectChannelTarget && !EditsAllChannels)
        {
            effect = new ChannelEffect(effect, EditLeft ? [0] : [1]);
        }

        StopPlayback();
        var range = _editor.EffectRange;
        var input = Buffer.Slice(range.Start, range.Length);

        IsBusy = true;
        StatusText = $"{effect.Name} wird angewendet …";
        try
        {
            var output = await Task.Run(() => effect.Apply(input));
            _editor.ApplyEffectResult(effect.Name, range, output);
            StatusText = $"{effect.Name} ✓";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"{effect.Name} fehlgeschlagen.";
            _dialogs.ShowError($"Der Effekt „{effect.Name}“ konnte nicht angewendet werden.\n\n{ex.Message}");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshRecentFiles()
    {
        RecentFiles.Clear();
        int number = 1;
        foreach (string path in _settings.Settings.RecentFiles)
        {
            RecentFiles.Add(new RecentFileItem(number++, path, OpenRecentCommand));
        }

        OnPropertyChanged(nameof(HasRecentFiles));
    }

    private void Edit(Action edit)
    {
        StopPlayback();
        try
        {
            edit();
            StatusText = $"{_editor.UndoDescription} ✓";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _dialogs.ShowError(ex.Message);
        }
    }

    private bool ConfirmDiscardChanges() =>
        !IsModified || _dialogs.Confirm(
            "Es gibt ungespeicherte Änderungen. Möchtest du sie verwerfen?\n\n" +
            "(Tipp: Mit „Exportieren“ sicherst du dein Ergebnis.)",
            "Ungespeicherte Änderungen");

    private void StopPlayback()
    {
        _playback.Stop();
        _liveEqualizer = null;
        _liveReverb = null;
        _liveCompressor = null;
        _liveDelay = null;
        _liveFilter = null;
        _liveDistortion = null;
        _liveChorus = null;
        _liveFlanger = null;
        _liveGate = null;
        _liveSaturation = null;
        _livePhaser = null;
        _liveTremolo = null;
        _liveWidener = null;
        CompressorReductionLeft = CompressorReductionRight = 0;
        _spectrumAnalyzers = null;
        _channelMonitor = null;
        Spectra = null;
        _liveLoudness = null;
        LoudnessText = string.Empty;
        IsPlaying = false;
        StopPlayheadUpdates();
        PlayheadFrame = Selection.Start;
    }

    private async void StartPlayheadUpdates()
    {
        StopPlayheadUpdates();

        // Diese Schleife "besitzt" ihre CancellationTokenSource und gibt sie erst am Ende selbst frei.
        // StopPlayheadUpdates() bricht nur ab – sonst könnte ein bereits fälliger Timer-Schritt
        // danach noch auf die freigegebene Quelle zugreifen (ObjectDisposedException).
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        _playheadUpdates = cts;

        // PeriodicTimer setzt im UI-Thread fort, weil "await" den SynchronizationContext beibehält.
        using var timer = new PeriodicTimer(PlayheadInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token) && !token.IsCancellationRequested)
            {
                PlayheadFrame = _playback.CurrentFrame;
                UpdateSpectrum();
                UpdateLiveLoudness();
                if (_liveCompressor is { } liveCompressor)
                {
                    CompressorReductionLeft = liveCompressor.GetGainReductionDb(0);
                    CompressorReductionRight = liveCompressor.GetGainReductionDb(IsStereo ? 1 : 0);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Wiedergabe beendet – nichts zu tun.
        }
        finally
        {
            if (ReferenceEquals(_playheadUpdates, cts))
            {
                _playheadUpdates = null;
            }
        }
    }

    private void StopPlayheadUpdates()
    {
        _playheadUpdates?.Cancel();
        _playheadUpdates = null;
    }

    private void OnPlaybackEnded(object? sender, StoppedEventArgs e)
    {
        IsPlaying = false;
        _spectrumAnalyzers = null;
        _channelMonitor = null;
        Spectra = null;
        StopPlayheadUpdates();
        PlayheadFrame = Selection.Start;
        if (e.Exception is not null)
        {
            _dialogs.ShowError($"Bei der Wiedergabe ist ein Fehler aufgetreten.\n\n{e.Exception.Message}");
        }
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Buffer));
        OnPropertyChanged(nameof(HasAudio));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(UndoText));
        OnPropertyChanged(nameof(RedoText));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(FormatText));
        OnPropertyChanged(nameof(IsStereo));
        OnPropertyChanged(nameof(IsWidenerAudible));
        OnPropertyChanged(nameof(UsesPerChannelEqualizer));
        OnPropertyChanged(nameof(CurrentEqualizer));
        OnPropertyChanged(nameof(EqualizerTargetText));
        OnPropertyChanged(nameof(ActiveEqualizerCurve));
        _liveEqualizer?.Update(EffectiveEqualizerSettings());
        _liveReverb?.Update(EffectiveReverbSettings());
        _liveCompressor?.Update(EffectiveCompressorSettings());
        _liveDelay?.Update(EffectiveDelaySettings());
        _liveDistortion?.Update(EffectiveDistortionSettings());
        _liveChorus?.Update(EffectiveChorusSettings());
        _liveFlanger?.Update(EffectiveFlangerSettings());
        OnPropertyChanged(nameof(ActiveChannels));
        OnPropertyChanged(nameof(MutedChannels));
        OnPropertyChanged(nameof(ChannelTargetText));
        RefreshMarkerProperties();
        UpdateEqualizerCurve();
        RefreshCommands();
    }

    private void OnEditorSelectionChanged(object? sender, EventArgs e)
    {
        if (!_editor.HasSelection)
        {
            SpectralBand = null;
        }

        OnPropertyChanged(nameof(Selection));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));

        // Neue Position gewählt, während pausiert ist → nächstes „Abspielen“ startet dort.
        if (_playback.State == PlaybackState.Paused)
        {
            StopPlayback();
        }
        else if (!IsPlaying)
        {
            PlayheadFrame = Selection.Start;
        }

        RefreshCommands();
    }

    partial void OnIsBusyChanged(bool value) => RefreshCommands();

    partial void OnIsPlayingChanged(bool value) => PauseCommand.NotifyCanExecuteChanged();

    private void RefreshCommands()
    {
        IRelayCommand[] commands =
        [
            OpenCommand, OpenRecentCommand, ExportCommand, ExportChannelsCommand,
            AddEditorAudioAsTrackCommand, ApplyToLinkedClipCommand, OpenMixdownInEditorCommand,
            KeepLeftChannelCommand, KeepRightChannelCommand,
            PlayCommand, PauseCommand, StopCommand, TogglePlayPauseCommand,
            SeekCommand, SkipForwardCommand, SkipBackCommand,
            SelectAllCommand, CutCommand, CopyCommand, PasteCommand, DeleteCommand, TrimCommand,
            InsertSilenceCommand, UndoCommand, RedoCommand,
            ChangeGainCommand, NormalizeCommand, ApplyEqualizerCommand, ApplyReverbCommand, ApplyCompressorCommand, ApplyDelayCommand, ApplyFilterCommand, ApplyDistortionCommand,
            ApplyChorusCommand, ApplyFlangerCommand, TempoPitchCommand,
            FadeInCommand, FadeOutCommand, ReverseCommand, MuteCommand,
            AnalyzePitchCommand, CorrectPitchCommand, AutotuneCommand, PitchRampCommand, VoiceEffectCommand, DetectTempoAndKeyCommand,
            MeasureLoudnessCommand, NormalizeLoudnessCommand, DeclickCommand, RepairSpectrumCommand,
            LearnNoiseProfileCommand, RemoveNoiseCommand,
        ];
        foreach (var command in commands)
        {
            command.NotifyCanExecuteChanged();
        }

        RefreshMarkerCommands();
    }

    private string FormatFrame(int frame) => TimeFormatter.Format(Buffer.FrameToTime(frame));
}
