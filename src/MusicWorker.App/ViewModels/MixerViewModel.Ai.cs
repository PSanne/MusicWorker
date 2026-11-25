using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Services;

namespace MusicWorker.App.ViewModels;

/// <summary>KI im Mischpult: Gesang und Begleitung eines Clips auf zwei neue Spuren trennen.</summary>
public partial class MixerViewModel
{
    [RelayCommand]
    private async Task SeparateClipVocals()
    {
        if (SelectedClip is not { IsNoteClip: false } clip)
        {
            StatusText = "Bitte zuerst einen Audioclip (z. B. einen Song) anklicken, dessen Gesang getrennt werden soll.";
            return;
        }

        if (!await AiModels.EnsureAsync(VocalSeparation.Default, _dialogs, text => StatusText = text))
        {
            return;
        }

        StopPlayback();
        IsBusy = true;
        var audio = clip.Audio;
        var track = clip.Track;
        StatusText = "Die KI trennt Gesang und Begleitung …";
        try
        {
            var progress = new Progress<double>(p => StatusText = string.Format(CultureInfo.CurrentCulture, "Die KI trennt Gesang und Begleitung … {0:0} %", p * 100));
            var (vocals, accompaniment) = await Task.Run(() => VocalSeparation.Default.Separate(audio, progress));
            RecordUndo();
            var vocalTrack = AddTrackWithoutUndo(vocals, $"{track.Name} – Gesang", clip.StartFrame);
            var accompanimentTrack = AddTrackWithoutUndo(accompaniment, $"{track.Name} – Begleitung", clip.StartFrame);
            vocalTrack.VolumeDb = accompanimentTrack.VolumeDb = track.VolumeDb;
            vocalTrack.Pan = accompanimentTrack.Pan = track.Pan;
            track.Mute = true; // Original stummschalten, damit nichts doppelt klingt
            OnStructureChanged();
            StatusText = $"Gesang und Begleitung von „{track.Name}“ liegen jetzt auf zwei neuen Spuren (das Original ist stummgeschaltet).";
        }
        catch (Exception ex)
        {
            StatusText = "Die Trennung ist fehlgeschlagen.";
            _dialogs.ShowError($"Gesang und Begleitung konnten nicht getrennt werden.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Neue Spur mit einem Clip, ohne eigenen Rückgängig-Schritt (für Aktionen, die mehrere Spuren anlegen).</summary>
    private TrackViewModel AddTrackWithoutUndo(MusicWorker.Core.Audio.AudioBuffer audio, string name, long startFrame)
    {
        var track = CreateTrack(name, Palette[_nextColor++ % Palette.Length]);
        track.Clips.Add(new ClipViewModel(track, Conform(audio), Math.Max(0, startFrame)));
        Tracks.Add(track);
        return track;
    }
}
