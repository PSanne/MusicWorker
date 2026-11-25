using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Helpers;
using MusicWorker.Core.Editing;

namespace MusicWorker.App.ViewModels;

/// <summary>Marker im Mischpult: werden mit dem Projekt gespeichert und sind rückgängig machbar.</summary>
public sealed partial class MixerViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMarkers), nameof(MarkerItems))]
    private IReadOnlyList<Marker> _markers = MarkerList.Empty;

    public bool HasMarkers => Markers.Count > 0;

    public IReadOnlyList<MarkerMenuItem> MarkerItems => Markers
        .Select((m, i) => new MarkerMenuItem($"{m.Name}  ({TimeFormatter.Format(TimeSpan.FromSeconds((double)m.Frame / SampleRate))})", i, JumpToMarkerCommand))
        .ToArray();

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddMarker()
    {
        var marker = new Marker(MarkerList.NextName(Markers), PlayheadFrame);
        ChangeMarkers(MarkerList.Add(Markers, marker));
        StatusText = $"Marker „{marker.Name}“ gesetzt – Doppelklick auf das gelbe Fähnchen benennt ihn um.";
    }

    [RelayCommand]
    private void NextMarker()
    {
        if (MarkerList.Next(Markers, PlayheadFrame) is { } marker)
        {
            JumpTo(marker);
        }
    }

    [RelayCommand]
    private void PreviousMarker()
    {
        long tolerance = IsPlaying ? SampleRate / 2 : 0;
        if (MarkerList.Previous(Markers, PlayheadFrame - tolerance) is { } marker)
        {
            JumpTo(marker);
        }
    }

    [RelayCommand]
    private void JumpToMarker(int index)
    {
        if (index >= 0 && index < Markers.Count)
        {
            JumpTo(Markers[index]);
        }
    }

    /// <summary>Aktion aus der Spuransicht: springen, verschieben, umbenennen oder löschen.</summary>
    [RelayCommand]
    private void MixerMarker(MarkerAction? action)
    {
        if (action is null || action.Index < 0 || action.Index >= Markers.Count)
        {
            return;
        }

        var marker = Markers[action.Index];
        switch (action.Kind)
        {
            case MarkerActionKind.Jump:
                JumpTo(marker);
                break;
            case MarkerActionKind.Move when !IsBusy:
                ChangeMarkers(MarkerList.ReplaceAt(Markers, action.Index, marker with { Frame = Math.Max(0, action.Frame) }));
                StatusText = "Marker verschoben.";
                break;
            case MarkerActionKind.Rename when !IsBusy:
                if (_dialogs.AskText("Marker umbenennen", "Neuer Name für den Marker (z. B. „Refrain“):", marker.Name) is { } name &&
                    !string.IsNullOrWhiteSpace(name) && name.Trim() != marker.Name)
                {
                    ChangeMarkers(MarkerList.ReplaceAt(Markers, action.Index, marker with { Name = name.Trim() }));
                }

                break;
            case MarkerActionKind.Delete when !IsBusy:
                ChangeMarkers(MarkerList.RemoveAt(Markers, action.Index));
                StatusText = $"Marker „{marker.Name}“ gelöscht.";
                break;
        }
    }

    private void JumpTo(Marker marker)
    {
        Seek(marker.Frame);
        StatusText = $"Marker „{marker.Name}“ – {TimeFormatter.Format(TimeSpan.FromSeconds((double)marker.Frame / SampleRate))}";
    }

    /// <summary>Marker ändern, ohne die Wiedergabe zu unterbrechen (Rückgängig-Schritt inklusive).</summary>
    private void ChangeMarkers(IReadOnlyList<Marker> markers)
    {
        RecordUndo();
        Markers = markers;
        IsModified = true;
    }
}
