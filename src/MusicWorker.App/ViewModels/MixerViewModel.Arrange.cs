using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Helpers;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Notes;
using MusicWorker.Core.Recording;

namespace MusicWorker.App.ViewModels;

/// <summary>
/// Arbeiten im Mischpult: mehrere Clips wählen und gemeinsam verschieben, Kopieren/Einfügen/Duplizieren,
/// Ein-/Ausblenden mit Griffen am Clip, Spuren umsortieren und Spurhöhe.
/// </summary>
public sealed partial class MixerViewModel
{
    private readonly List<ClipViewModel> _selection = [];
    private IReadOnlyList<ClipboardClip>? _clipboard;

    /// <summary>Kopierter Clip mit Lage relativ zum frühesten kopierten Clip.</summary>
    private sealed record ClipboardClip(
        TrackViewModel Track, int TrackIndex, long Offset, AudioBuffer Audio, IReadOnlyList<Note>? Notes, long LengthTicks,
        IReadOnlyList<AudioBuffer>? Takes, IReadOnlyList<CompSegment>? Comp, long FadeIn, long FadeOut, long Length);

    /// <summary>Höhe aller Spuren (Regler in der Werkzeugleiste); einzelne Spuren lassen sich per Rechtsklick ändern.</summary>
    [ObservableProperty]
    private double _trackHeight = TrackViewModel.DefaultHeight;

    /// <summary>Alle gewählten Clips (Strg+Klick oder Rahmen ziehen); <see cref="SelectedClip"/> ist der zuletzt gewählte.</summary>
    public IReadOnlyList<ClipViewModel> SelectedClips =>
        _selection.Where(c => c.Track.Clips.Contains(c) && Tracks.Contains(c.Track)).ToArray();

    public bool HasClipboard => _clipboard is { Count: > 0 };

    partial void OnTrackHeightChanged(double value)
    {
        double height = Math.Clamp(value, TrackViewModel.MinHeight, TrackViewModel.MaxHeight);
        foreach (var track in Tracks)
        {
            track.Height = height;
        }

        TimelineVersion++;
    }

    /// <summary>Hält Auswahl und „gewählten Clip“ zusammen: Wird ein anderer Clip direkt gewählt, gilt nur noch er.</summary>
    private void SyncSelection(ClipViewModel? value)
    {
        if (value is null)
        {
            _selection.Clear();
        }
        else if (!_selection.Contains(value))
        {
            _selection.Clear();
            _selection.Add(value);
        }

        OnPropertyChanged(nameof(SelectedClips));
        CopyClipsCommand.NotifyCanExecuteChanged();
        CutClipsCommand.NotifyCanExecuteChanged();
        DuplicateClipsCommand.NotifyCanExecuteChanged();
        ResetFadesCommand.NotifyCanExecuteChanged();
    }

    private void SetSelection(IEnumerable<ClipViewModel> clips, ClipViewModel? primary = null)
    {
        _selection.Clear();
        _selection.AddRange(clips.Distinct());
        var target = primary ?? _selection.LastOrDefault();
        if (ReferenceEquals(SelectedClip, target))
        {
            SyncSelection(target);
        }
        else
        {
            SelectedClip = target;
        }

        TimelineVersion++;
    }

    /// <summary>Strg+Klick: Clip zur Auswahl hinzufügen bzw. aus ihr entfernen.</summary>
    [RelayCommand]
    private void ToggleClipSelection(ClipViewModel? clip)
    {
        if (clip is null)
        {
            return;
        }

        var clips = SelectedClips.ToList();
        if (!clips.Remove(clip))
        {
            clips.Add(clip);
        }

        SetSelection(clips, clips.Contains(clip) ? clip : clips.LastOrDefault());
        StatusText = clips.Count > 1 ? $"{clips.Count} Clips gewählt – ziehen verschiebt alle, Strg+C kopiert, Entf löscht." : StatusText;
    }

    /// <summary>Rahmen aufziehen: alle Clips im Rahmen wählen.</summary>
    [RelayCommand]
    private void SelectClips(IReadOnlyList<ClipViewModel>? clips)
    {
        SetSelection(clips ?? []);
        if (clips is { Count: > 1 })
        {
            StatusText = $"{clips.Count} Clips gewählt – ziehen verschiebt alle, Strg+C kopiert, Entf löscht.";
        }
    }

    /// <summary>Strg+A im Mischpult.</summary>
    [RelayCommand(CanExecute = nameof(HasTracks))]
    private void SelectAllClips()
    {
        SetSelection(Tracks.SelectMany(t => t.Clips));
        StatusText = $"Alle {SelectedClips.Count} Clips gewählt.";
    }

    /// <summary>Mehrere Clips gemeinsam verschieben (Abstände bleiben erhalten).</summary>
    [RelayCommand]
    private void MoveClips(ClipsMove? move)
    {
        if (move is null)
        {
            return;
        }

        var clips = SelectedClips;
        if (clips.Count <= 1 || !clips.Contains(move.Anchor))
        {
            MoveClip(new ClipMove(move.Anchor, move.NewStart, null, move.DragStarted));
            return;
        }

        if (move.DragStarted)
        {
            RecordUndo();
        }

        long start = Math.Max(0, move.NewStart);
        if (IsSnapEnabled)
        {
            start = SnapToBeat(start);
        }

        long delta = start - move.Anchor.StartFrame;
        delta = Math.Max(delta, -clips.Min(c => c.StartFrame)); // kein Clip vor den Projektanfang
        if (delta == 0)
        {
            return;
        }

        foreach (var clip in clips)
        {
            clip.StartFrame += delta;
        }

        OnStructureChanged();
    }

    /// <summary>Ein-/Ausblenden per Griff oben links bzw. rechts am Clip.</summary>
    [RelayCommand]
    private void SetClipFade(ClipFade? fade)
    {
        if (fade is null || fade.Clip.IsNoteClip)
        {
            return;
        }

        if (fade.DragStarted)
        {
            RecordUndo();
        }

        var clip = fade.Clip;
        long length = clip.LengthFrames;
        if (fade.FadeIn)
        {
            clip.FadeInFrames = Math.Clamp(fade.Frames, 0, Math.Max(0, length - clip.FadeOutFrames));
        }
        else
        {
            clip.FadeOutFrames = Math.Clamp(fade.Frames, 0, Math.Max(0, length - clip.FadeInFrames));
        }

        OnStructureChanged();
        long frames = fade.FadeIn ? clip.FadeInFrames : clip.FadeOutFrames;
        StatusText = $"{(fade.FadeIn ? "Einblenden" : "Ausblenden")}: {TimeFormatter.Format(FrameToTime(frames))}";
    }

    /// <summary>Ein- und Ausblenden der gewählten Clips entfernen.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedClip))]
    private void ResetFades()
    {
        var clips = SelectedClips.Where(c => c.FadeInFrames > 0 || c.FadeOutFrames > 0).ToArray();
        if (clips.Length == 0)
        {
            return;
        }

        RecordUndo();
        foreach (var clip in clips)
        {
            clip.FadeInFrames = clip.FadeOutFrames = 0;
        }

        OnStructureChanged();
        StatusText = "Ein- und Ausblenden entfernt.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedClip))]
    private void CopyClips()
    {
        var clips = SelectedClips;
        if (clips.Count == 0)
        {
            return;
        }

        _clipboard = ToClipboard(clips);
        OnPropertyChanged(nameof(HasClipboard));
        PasteClipsCommand.NotifyCanExecuteChanged();
        StatusText = clips.Count == 1 ? "Clip kopiert – Strg+V fügt ihn an der Abspielposition ein." : $"{clips.Count} Clips kopiert – Strg+V fügt sie an der Abspielposition ein.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedClip))]
    private void CutClips()
    {
        var clips = SelectedClips;
        if (clips.Count == 0)
        {
            return;
        }

        _clipboard = ToClipboard(clips);
        OnPropertyChanged(nameof(HasClipboard));
        PasteClipsCommand.NotifyCanExecuteChanged();
        RemoveClips(clips);
        StatusText = $"{clips.Count} Clip(s) ausgeschnitten.";
    }

    /// <summary>Fügt die kopierten Clips an der Abspielposition ein – jeweils auf ihre ursprüngliche Spur.</summary>
    [RelayCommand(CanExecute = nameof(HasClipboard))]
    private void PasteClips()
    {
        if (_clipboard is not { Count: > 0 } items)
        {
            return;
        }

        long at = PlayheadFrame;
        if (IsSnapEnabled)
        {
            at = SnapToBeat(at);
        }

        var pasted = Paste(items, at);
        if (pasted.Count > 0)
        {
            Seek(pasted.Max(c => c.EndFrame)); // nochmal Strg+V hängt direkt dahinter an
        }
    }

    /// <summary>Strg+D: gewählte Clips direkt hinter sich wiederholen.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedClip))]
    private void DuplicateClips()
    {
        var clips = SelectedClips;
        if (clips.Count == 0)
        {
            return;
        }

        long end = clips.Max(c => c.EndFrame);
        Paste(ToClipboard(clips), end);
    }

    private IReadOnlyList<ClipboardClip> ToClipboard(IReadOnlyList<ClipViewModel> clips)
    {
        long first = clips.Min(c => c.StartFrame);
        return clips.OrderBy(c => c.StartFrame).Select(c => new ClipboardClip(
            c.Track, Tracks.IndexOf(c.Track), c.StartFrame - first, c.Audio, c.Notes, c.LengthTicks, c.Takes, c.Comp,
            c.FadeInFrames, c.FadeOutFrames, c.LengthFrames)).ToArray();
    }

    private List<ClipViewModel> Paste(IReadOnlyList<ClipboardClip> items, long at)
    {
        var pasted = new List<ClipViewModel>();
        int skipped = 0;
        StopPlayback();
        RecordUndo();
        foreach (var item in items)
        {
            var track = Tracks.Contains(item.Track) ? item.Track : Tracks.ElementAtOrDefault(item.TrackIndex);
            if (track is null || track.IsNoteTrack != (item.Notes is not null))
            {
                skipped++;
                continue;
            }

            long start = at + item.Offset;
            ClipViewModel clip;
            if (item.Notes is { } notes)
            {
                clip = CreateNoteClip(track, notes, item.LengthTicks, start);
            }
            else
            {
                clip = new ClipViewModel(track, item.Audio, start)
                {
                    Takes = item.Takes,
                    Comp = item.Comp,
                    FadeInFrames = item.FadeIn,
                    FadeOutFrames = item.FadeOut,
                };
            }

            track.Clips.Add(clip);
            pasted.Add(clip);
        }

        OnStructureChanged();
        SetSelection(pasted);
        StatusText = $"{pasted.Count} Clip(s) eingefügt bei {TimeFormatter.Format(FrameToTime(at))}." +
            (skipped > 0 ? $" {skipped} übersprungen (Spur fehlt oder passt nicht: Noten nur auf Notenspuren)." : string.Empty);
        return pasted;
    }

    private void RemoveClips(IReadOnlyList<ClipViewModel> clips)
    {
        StopPlayback();
        RecordUndo();
        foreach (var clip in clips)
        {
            clip.Track.Clips.Remove(clip);
        }

        SetSelection([]);
        OnStructureChanged();
    }

    /// <summary>Spur um eine Position nach oben (-1) oder unten (+1) verschieben.</summary>
    private void MoveTrackBy(TrackViewModel track, int delta) => MoveTrack(new TrackMove(track, Tracks.IndexOf(track) + delta));

    /// <summary>Spur an eine neue Position setzen (Ziehen in der Spurliste).</summary>
    [RelayCommand]
    private void MoveTrack(TrackMove? move)
    {
        if (move is null || Tracks.IndexOf(move.Track) is not (>= 0 and var from))
        {
            return;
        }

        int to = Math.Clamp(move.NewIndex, 0, Tracks.Count - 1);
        if (to == from)
        {
            return;
        }

        RecordUndo();
        Tracks.Move(from, to);
        OnStructureChanged();
        StatusText = $"Spur „{move.Track.Name}“ an Position {to + 1} verschoben.";
    }
}
