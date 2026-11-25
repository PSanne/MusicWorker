using System.Globalization;
using System.IO;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Editing;
using MusicWorker.Core.Persistence;

namespace MusicWorker.App.ViewModels;

/// <summary>
/// Automatische Sicherung (alle zwei Minuten) und Wiederherstellung nach einem Absturz.
/// Gesichert wird in einen eigenen Ordner – die Dateien des Nutzers werden nie ungefragt überschrieben.
/// </summary>
public sealed partial class MainViewModel
{
    private AudioBuffer? _autosavedBuffer;
    private IReadOnlyList<Marker>? _autosavedMarkers;
    private string? _autosavedFile;

    /// <summary>Automatische Sicherung ein/aus (Menü „Datei“).</summary>
    public bool IsAutosaveEnabled
    {
        get => _settings.Settings.AutosaveEnabled;
        set
        {
            if (_settings.Settings.AutosaveEnabled != value)
            {
                _settings.Settings.AutosaveEnabled = value;
                _settings.Save();
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Sichert Editor und Mischpult, sofern es ungespeicherte Änderungen gibt.</summary>
    public async Task AutosaveAsync(RecoverySession session)
    {
        if (!IsAutosaveEnabled)
        {
            session.ClearEditor();
            session.ClearMixer();
            _autosavedBuffer = null;
            return;
        }

        await AutosaveEditorAsync(session.EditorFolder);
        await Mixer.AutosaveAsync(session.MixerFolder);
    }

    private async Task AutosaveEditorAsync(string folder)
    {
        if (IsBusy)
        {
            return;
        }

        if (!IsModified || !HasAudio)
        {
            if (_autosavedBuffer is not null)
            {
                RecoveryStore.Delete(folder);
                _autosavedBuffer = null;
                _autosavedFile = null;
            }

            return;
        }

        var buffer = Buffer;
        var markers = _editor.Markers;
        if (ReferenceEquals(buffer, _autosavedBuffer) && ReferenceEquals(markers, _autosavedMarkers))
        {
            return; // seit der letzten Sicherung nichts geändert
        }

        bool sameAudio = ReferenceEquals(buffer, _autosavedBuffer) && _autosavedFile is not null;
        string audioFile = sameAudio ? _autosavedFile! : $"Audio-{Guid.NewGuid().ToString("N")[..10]}.wav";
        var info = new EditorRecovery
        {
            OriginalPath = FilePath,
            DisplayName = FileName,
            AudioFile = audioFile,
            Markers = markers.Select(m => new RecoveryMarker { Name = m.Name, Frame = m.Frame }).ToList(),
        };

        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(folder);
                if (!sameAudio)
                {
                    _files.SaveLossless(buffer, Path.Combine(folder, audioFile));
                }

                info.Write(folder);
                foreach (string old in Directory.EnumerateFiles(folder, "Audio-*.wav"))
                {
                    if (!string.Equals(Path.GetFileName(old), audioFile, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(old);
                    }
                }
            });
            _autosavedBuffer = buffer;
            _autosavedMarkers = markers;
            _autosavedFile = audioFile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _autosavedBuffer = null; // nächstes Mal erneut versuchen
        }
    }

    /// <summary>
    /// Beim Start: Gibt es Arbeit aus einer abgestürzten Sitzung? Dann fragen und auf Wunsch wiederherstellen.
    /// Danach werden alle verwaisten Sicherungen entfernt.
    /// </summary>
    public async Task OfferRecoveryAsync(IReadOnlyList<AbandonedSession> sessions)
    {
        if (sessions.Count == 0)
        {
            return;
        }

        var latest = sessions[0];
        var parts = new List<string>();
        var editorInfo = latest.HasEditor ? EditorRecovery.Read(Path.Combine(latest.Folder, RecoveryStore.EditorFolderName)) : null;
        if (editorInfo is not null)
        {
            parts.Add($"• Editor: „{editorInfo.DisplayName}“");
        }

        if (latest.HasMixer)
        {
            parts.Add("• Mischpult-Projekt");
        }

        if (parts.Count > 0 && _dialogs.Confirm(
                "MusicWorker wurde beim letzten Mal nicht ordnungsgemäß beendet.\n\n" +
                $"Automatisch gesichert am {latest.SavedAt.ToString("g", CultureInfo.CurrentCulture)}:\n" +
                string.Join("\n", parts) +
                "\n\nMöchtest du diese Arbeit wiederherstellen?\n\n" +
                "Ja = wiederherstellen (danach bitte speichern bzw. exportieren)\nNein = Sicherung verwerfen",
                "Wiederherstellung"))
        {
            if (editorInfo is not null)
            {
                await RestoreEditorAsync(Path.Combine(latest.Folder, RecoveryStore.EditorFolderName), editorInfo);
            }

            if (latest.HasMixer)
            {
                await Mixer.RestoreAutosaveAsync(Path.Combine(latest.Folder, RecoveryStore.MixerFolderName));
                SelectedMainTab = editorInfo is null ? 1 : SelectedMainTab;
            }
        }

        foreach (var session in sessions)
        {
            RecoveryStore.Delete(session.Folder);
        }
    }

    private async Task RestoreEditorAsync(string folder, EditorRecovery info)
    {
        string audioPath = Path.Combine(folder, info.AudioFile);
        if (!File.Exists(audioPath))
        {
            _dialogs.ShowError("Die gesicherte Audiodatei des Editors wurde nicht gefunden.");
            return;
        }

        try
        {
            IsBusy = true;
            var buffer = await Task.Run(() => _files.Load(audioPath));
            _editor.Load(buffer, info.Markers.Select(m => new Marker(m.Name, m.Frame)).ToArray());
            _editor.MarkUnsaved();
            FilePath = info.OriginalPath;
            DocumentName = info.OriginalPath is null ? info.DisplayName : null;
            LinkedClip = null;
            PlayheadFrame = 0;
            StatusText = $"„{FileName}“ aus der automatischen Sicherung wiederhergestellt – bitte exportieren, um das Ergebnis zu behalten.";
            DocumentLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Die Sicherung konnte nicht geöffnet werden.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
