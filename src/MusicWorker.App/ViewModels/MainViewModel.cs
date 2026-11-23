using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Audio;
using MusicWorker.App.Helpers;
using MusicWorker.App.Services;
using MusicWorker.Core.Audio;
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

    public MainViewModel(IDialogService dialogs, AudioFileService files, PlaybackService playback)
    {
        _dialogs = dialogs;
        _files = files;
        _playback = playback;

        _editor.Changed += OnEditorChanged;
        _editor.SelectionChanged += OnEditorSelectionChanged;
        _playback.PlaybackEnded += OnPlaybackEnded;
    }

    /// <summary>Eine neue Datei wurde geladen (die Ansicht setzt daraufhin den Zoom zurück).</summary>
    public event EventHandler? DocumentLoaded;

    public AudioBuffer Buffer => _editor.Buffer;

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

    // ───────────── Effekte ─────────────

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void ChangeGain()
    {
        if (_dialogs.AskGainDecibels() is { } decibels && decibels != 0)
        {
            Edit(() => _editor.ApplyEffect(new GainEffect(decibels)));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void Normalize() => Edit(() => _editor.ApplyEffect(new NormalizeEffect()));

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void FadeIn() => Edit(() => _editor.ApplyEffect(new FadeEffect(FadeDirection.In)));

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void FadeOut() => Edit(() => _editor.ApplyEffect(new FadeEffect(FadeDirection.Out)));

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
            OpenCommand, ExportCommand, PlayCommand, PauseCommand, StopCommand, TogglePlayPauseCommand,
            SelectAllCommand, CutCommand, CopyCommand, PasteCommand, DeleteCommand, TrimCommand,
            UndoCommand, RedoCommand, ChangeGainCommand, NormalizeCommand, FadeInCommand, FadeOutCommand,
        ];
        foreach (var command in commands)
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private string FormatFrame(int frame) => TimeFormatter.Format(Buffer.FrameToTime(frame));
}
