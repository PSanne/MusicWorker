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
    private string _statusText = "Bereit – öffne eine Audiodatei (Strg+O) oder ziehe sie ins Fenster.";

    public MainViewModel(
        IDialogService dialogs, AudioFileService files, PlaybackService playback, SettingsService settings)
    {
        _dialogs = dialogs;
        _files = files;
        _playback = playback;
        _settings = settings;
        RefreshRecentFiles();

        _editor.Changed += OnEditorChanged;
        _editor.SelectionChanged += OnEditorSelectionChanged;
        _playback.PlaybackEnded += OnPlaybackEnded;
    }

    /// <summary>Eine neue Datei wurde geladen (die Ansicht setzt daraufhin den Zoom zurück).</summary>
    public event EventHandler? DocumentLoaded;

    public AudioBuffer Buffer => _editor.Buffer;

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

        StopPlayback();
        IsBusy = true;
        StatusText = $"Exportiere „{Path.GetFileName(path)}“ …";
        var buffer = Buffer;
        try
        {
            await Task.Run(() => _files.Export(buffer, path));
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
            _playback.Play(Buffer, start, end);
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

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task EqualizerAsync()
    {
        StopPlayback();

        // Vorhören: die Auswahl – oder ab dem Cursor bis zum Ende – in Schleife.
        var selection = Selection;
        int start = selection.Start < Buffer.FrameCount ? selection.Start : 0;
        int end = selection.IsEmpty ? Buffer.FrameCount : selection.End;
        var preview = new EqualizerPreview(_playback, Buffer, start, end);

        var saved = _settings.Settings;
        var initial = saved.EqualizerGainsDb is { Length: var n } gains && n == EqualizerSettings.BandFrequencies.Count
            ? new EqualizerSettings(gains, saved.EqualizerPreampDb)
            : EqualizerSettings.Neutral;

        using var equalizer = new EqualizerViewModel(initial, saved.EqualizerPreventClipping, preview);
        bool apply = _dialogs.ShowEqualizer(equalizer);
        equalizer.StopPreview();
        if (!apply)
        {
            return;
        }

        var settings = equalizer.ToSettings();
        saved.EqualizerGainsDb = settings.GainsDb.ToArray();
        saved.EqualizerPreampDb = settings.PreampDb;
        saved.EqualizerPreventClipping = equalizer.PreventClipping;
        _settings.Save();

        if (!settings.IsNeutral)
        {
            await ApplyEffectAsync(new EqualizerEffect(settings, equalizer.PreventClipping));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
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
        StopPlayback();
        _playback.PlaybackEnded -= OnPlaybackEnded;
        _playback.Dispose();
    }

    // ───────────── Hilfsmethoden ─────────────

    private bool CanOpen() => !IsBusy;

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

    /// <summary>
    /// Berechnet einen Effekt im Hintergrund (die Oberfläche bleibt bedienbar) und übernimmt
    /// das Ergebnis anschließend im UI-Thread als rückgängig machbaren Schritt.
    /// </summary>
    private async Task ApplyEffectAsync(IAudioEffect effect)
    {
        if (!HasAudio)
        {
            return;
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
        }
        catch (Exception ex)
        {
            StatusText = $"{effect.Name} fehlgeschlagen.";
            _dialogs.ShowError($"Der Effekt „{effect.Name}“ konnte nicht angewendet werden.\n\n{ex.Message}");
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
            OpenCommand, OpenRecentCommand, ExportCommand,
            PlayCommand, PauseCommand, StopCommand, TogglePlayPauseCommand,
            SelectAllCommand, CutCommand, CopyCommand, PasteCommand, DeleteCommand, TrimCommand,
            InsertSilenceCommand, UndoCommand, RedoCommand,
            ChangeGainCommand, NormalizeCommand, EqualizerCommand, TempoPitchCommand,
            FadeInCommand, FadeOutCommand, ReverseCommand, MuteCommand,
        ];
        foreach (var command in commands)
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private string FormatFrame(int frame) => TimeFormatter.Format(Buffer.FrameToTime(frame));
}
