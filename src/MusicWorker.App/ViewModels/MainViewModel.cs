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

            if (IsReverbActive)
            {
                parts.Add("Hall");
            }

            return parts.Count == 0 ? string.Empty : string.Join(" + ", parts) + " aktiv";
        }
    }

    /// <summary>True, wenn der Hall eingeschaltet ist und tatsächlich etwas hinzufügt.</summary>
    private bool IsReverbActive => IsReverbEnabled && !EffectiveReverbSettings().IsNeutral;

    /// <summary>Mindestens ein Live-Effekt verändert gerade den Klang.</summary>
    private bool AreLiveEffectsActive => IsEqualizerActive || IsReverbActive;

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

    public string FileName => FilePath is null ? "Unbenannt" : Path.GetFileName(FilePath);

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
        string suggestedName = Path.GetFileNameWithoutExtension(FileName) + ".wav";
        string? path = _dialogs.ShowSaveFileDialog(AudioFileService.ExportFilter, suggestedName);
        if (path is null)
        {
            return;
        }

        // Live-Effekte einrechnen? (Sie verändern sonst nur die Wiedergabe, nicht die Datei.)
        var liveEffects = AskIncludeLiveEffects(
            $"{LiveEffectsName} ist eingeschaltet, aber noch nicht in das Audio übernommen.\n\n" +
            "Soll das in die exportierte Datei eingerechnet werden?\n\n" +
            "Ja = mit Effekten exportieren\nNein = ohne Effekte exportieren");

        StopPlayback();
        IsBusy = true;
        StatusText = $"Exportiere „{Path.GetFileName(path)}“ …";
        var buffer = Buffer;
        try
        {
            await Task.Run(() => _files.Export(ApplyAll(liveEffects, buffer), path));
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

    /// <summary>Speichert linken und rechten Kanal als zwei Mono-Dateien (…_L und …_R).</summary>
    [RelayCommand(CanExecute = nameof(CanUseStereo))]
    private async Task ExportChannelsAsync()
    {
        string suggestedName = Path.GetFileNameWithoutExtension(FileName) + ".wav";
        string? basePath = _dialogs.ShowSaveFileDialog(
            AudioFileService.ExportFilter, suggestedName, "Kanäle als Mono-Dateien exportieren – Basisname wählen");
        if (basePath is null)
        {
            return;
        }

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
            $"{LiveEffectsName} ist eingeschaltet. Soll das in die exportierten Dateien eingerechnet werden?");

        StopPlayback();
        IsBusy = true;
        StatusText = "Exportiere linken und rechten Kanal …";
        var buffer = Buffer;
        try
        {
            await Task.Run(() =>
            {
                var source = ApplyAll(liveEffects, buffer);
                _files.Export(source.GetChannel(0), leftPath);
                _files.Export(source.GetChannel(1), rightPath);
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

    /// <summary>Wird beim Schließen des Fensters gefragt.</summary>
    public bool ConfirmClose() => ConfirmDiscardChanges();

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

        var selection = Selection;
        int start = selection.Start;
        int end = selection.IsEmpty ? Buffer.FrameCount : selection.End;
        if (start >= end)
        {
            start = 0; // Cursor steht am Ende → von vorn beginnen
        }

        try
        {
            _playback.Play(Buffer, start, end, effects: AttachLiveEqualizer);
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

    [RelayCommand(CanExecute = nameof(CanChangeTempo))]
    private async Task TempoPitchAsync()
    {
        if (_dialogs.AskTempoAndPitch() is not { } values)
        {
            return;
        }

        var effect = new TempoPitchEffect(values.TempoPercent, values.Semitones);
        if (!effect.IsNeutral)
        {
            await ApplyEffectAsync(effect);
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
    }

    // ───────────── Hilfsmethoden ─────────────

    private bool CanOpen() => !IsBusy;

    private bool CanApplyReverb() => HasAudio && !IsBusy && CreateReverbEffectForTarget() is not null;

    private bool CanApplyEqualizer() => HasAudio && !IsBusy && CreateEqualizerEffectForTarget() is not null;

    private bool CanUseStereo() => IsStereo && !IsBusy;

    /// <summary>Tempo/Tonhöhe ändern die Länge – das geht nur für alle Kanäle gemeinsam.</summary>
    private bool CanChangeTempo() => CanUseAudio() && EditsAllChannels;

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
            var buffer = await Task.Run(() => _files.Load(path));
            _editor.Load(buffer);
            FilePath = path;
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
        var reverb = new ReverbProcessor(source.WaveFormat.SampleRate, source.WaveFormat.Channels, EffectiveReverbSettings());
        _liveReverb = reverb;

        // … → Equalizer → Hall → Spektrum-Abgriff → Soundkarte.
        return new SpectrumTapSampleProvider(
            new ReverbSampleProvider(new EqualizerSampleProvider(_channelMonitor, processor), reverb),
            _spectrumAnalyzers);
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

    /// <summary>Name der aktiven Live-Effekte für Rückfragen, z. B. „Der Live-Equalizer und der Hall“.</summary>
    private string LiveEffectsName => (IsEqualizerActive, IsReverbActive) switch
    {
        (true, true) => "Der Live-Equalizer und der Hall",
        (true, false) => "Der Live-Equalizer",
        _ => "Der Hall",
    };

    /// <summary>Fragt nach, ob aktive Live-Effekte beim Export eingerechnet werden sollen.</summary>
    private IAudioEffect[] AskIncludeLiveEffects(string question)
    {
        if (!AreLiveEffectsActive || !_dialogs.Confirm(question, "Live-Effekte beim Export"))
        {
            return [];
        }

        var effects = new List<IAudioEffect>();
        if (CreateEqualizerEffectForAllChannels() is { } equalizer)
        {
            effects.Add(equalizer);
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
        ApplyReverbCommand.NotifyCanExecuteChanged();
    }

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
        _settings.Save();
    }

    /// <summary>
    /// Berechnet einen Effekt im Hintergrund (die Oberfläche bleibt bedienbar) und übernimmt
    /// das Ergebnis anschließend im UI-Thread als rückgängig machbaren Schritt.
    /// </summary>
    private async Task<bool> ApplyEffectAsync(IAudioEffect effect)
    {
        if (!HasAudio)
        {
            return false;
        }

        if (!EditsAllChannels)
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
        _spectrumAnalyzers = null;
        _channelMonitor = null;
        Spectra = null;
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
        OnPropertyChanged(nameof(UsesPerChannelEqualizer));
        OnPropertyChanged(nameof(CurrentEqualizer));
        OnPropertyChanged(nameof(EqualizerTargetText));
        OnPropertyChanged(nameof(ActiveEqualizerCurve));
        _liveEqualizer?.Update(EffectiveEqualizerSettings());
        _liveReverb?.Update(EffectiveReverbSettings());
        OnPropertyChanged(nameof(ActiveChannels));
        OnPropertyChanged(nameof(MutedChannels));
        OnPropertyChanged(nameof(ChannelTargetText));
        UpdateEqualizerCurve();
        RefreshCommands();
    }

    private void OnEditorSelectionChanged(object? sender, EventArgs e)
    {
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
            KeepLeftChannelCommand, KeepRightChannelCommand,
            PlayCommand, PauseCommand, StopCommand, TogglePlayPauseCommand,
            SelectAllCommand, CutCommand, CopyCommand, PasteCommand, DeleteCommand, TrimCommand,
            InsertSilenceCommand, UndoCommand, RedoCommand,
            ChangeGainCommand, NormalizeCommand, ApplyEqualizerCommand, ApplyReverbCommand, TempoPitchCommand,
            FadeInCommand, FadeOutCommand, ReverseCommand, MuteCommand,
        ];
        foreach (var command in commands)
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private string FormatFrame(int frame) => TimeFormatter.Format(Buffer.FrameToTime(frame));
}
