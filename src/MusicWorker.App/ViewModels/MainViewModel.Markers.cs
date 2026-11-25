using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Helpers;
using MusicWorker.Core.Editing;
using NAudio.Wave;

namespace MusicWorker.App.ViewModels;

/// <summary>Marker im Editor (z. B. „Strophe“, „Refrain“): setzen, anspringen, umbenennen, verschieben, löschen.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Marker der Datei – wandern bei Schnitten mit und werden in WAV-Dateien mitgespeichert.</summary>
    public IReadOnlyList<Marker> Markers => _editor.Markers;

    public bool HasMarkers => _editor.Markers.Count > 0;

    /// <summary>Einträge für das Menü „Marker“.</summary>
    public IReadOnlyList<MarkerMenuItem> MarkerItems => _editor.Markers
        .Select((m, i) => new MarkerMenuItem($"{m.Name}  ({FormatFrame((int)Math.Min(m.Frame, int.MaxValue))})", i, JumpToMarkerCommand))
        .ToArray();

    /// <summary>Aktuelle Position: beim Abspielen die Wiedergabe, sonst der Cursor.</summary>
    private int CurrentEditorFrame => _playback.State != PlaybackState.Stopped ? _playback.CurrentFrame : Selection.Start;

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void AddMarker()
    {
        int index = _editor.AddMarker(CurrentEditorFrame);
        StatusText = $"Marker „{_editor.Markers[index].Name}“ gesetzt – Doppelklick auf das gelbe Fähnchen benennt ihn um.";
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void NextMarker()
    {
        if (MarkerList.Next(_editor.Markers, CurrentEditorFrame) is { } marker)
        {
            JumpTo(marker);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void PreviousMarker()
    {
        // Kurz nach einem Marker (z. B. beim Abspielen) zum davor liegenden springen – wie bei einem CD-Spieler.
        long tolerance = IsPlaying ? Buffer.SampleRate / 2 : 0;
        if (MarkerList.Previous(_editor.Markers, CurrentEditorFrame - tolerance) is { } marker)
        {
            JumpTo(marker);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void JumpToMarker(int index)
    {
        if (index >= 0 && index < _editor.Markers.Count)
        {
            JumpTo(_editor.Markers[index]);
        }
    }

    /// <summary>Aktion aus der Wellenform: springen, verschieben, umbenennen oder löschen.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private void EditorMarker(MarkerAction? action)
    {
        if (action is null || action.Index < 0 || action.Index >= _editor.Markers.Count)
        {
            return;
        }

        try
        {
            switch (action.Kind)
            {
                case MarkerActionKind.Jump:
                    JumpTo(_editor.Markers[action.Index]);
                    break;
                case MarkerActionKind.Move:
                    _editor.MoveMarker(action.Index, action.Frame);
                    StatusText = "Marker verschoben.";
                    break;
                case MarkerActionKind.Rename:
                    var marker = _editor.Markers[action.Index];
                    if (_dialogs.AskText("Marker umbenennen", "Neuer Name für den Marker (z. B. „Refrain“):", marker.Name) is { } name)
                    {
                        _editor.RenameMarker(action.Index, name);
                    }

                    break;
                case MarkerActionKind.Delete:
                    string removed = _editor.Markers[action.Index].Name;
                    _editor.RemoveMarker(action.Index);
                    StatusText = $"Marker „{removed}“ gelöscht.";
                    break;
            }
        }
        catch (ArgumentException ex)
        {
            _dialogs.ShowError(ex.Message);
        }
    }

    private void JumpTo(Marker marker)
    {
        Seek((int)Math.Clamp(marker.Frame, 0, Buffer.FrameCount));
        StatusText = $"Marker „{marker.Name}“ – {TimeFormatter.Format(Buffer.FrameToTime((int)Math.Clamp(marker.Frame, 0, Buffer.FrameCount)))}";
    }

    private void RefreshMarkerProperties()
    {
        OnPropertyChanged(nameof(Markers));
        OnPropertyChanged(nameof(HasMarkers));
        OnPropertyChanged(nameof(MarkerItems));
    }

    private void RefreshMarkerCommands()
    {
        AddMarkerCommand.NotifyCanExecuteChanged();
        NextMarkerCommand.NotifyCanExecuteChanged();
        PreviousMarkerCommand.NotifyCanExecuteChanged();
        JumpToMarkerCommand.NotifyCanExecuteChanged();
        EditorMarkerCommand.NotifyCanExecuteChanged();
    }

    // ───────────── Tasten, die je nach Reiter für Editor oder Mischpult gelten ─────────────

    /// <summary>M: Marker an der aktuellen Position setzen.</summary>
    [RelayCommand]
    private void AddMarkerKey() => RunForTab(Mixer.AddMarkerCommand, AddMarkerCommand);

    /// <summary>Bild↓: zum nächsten Marker.</summary>
    [RelayCommand]
    private void NextMarkerKey() => RunForTab(Mixer.NextMarkerCommand, NextMarkerCommand);

    /// <summary>Bild↑: zum vorherigen Marker.</summary>
    [RelayCommand]
    private void PreviousMarkerKey() => RunForTab(Mixer.PreviousMarkerCommand, PreviousMarkerCommand);

    private void RunForTab(System.Windows.Input.ICommand mixerCommand, System.Windows.Input.ICommand editorCommand)
    {
        var command = IsMixerTab ? mixerCommand : IsEditorTab ? editorCommand : null;
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }
}
