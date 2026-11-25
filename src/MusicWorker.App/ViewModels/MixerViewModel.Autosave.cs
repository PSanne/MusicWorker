using System.IO;
using MusicWorker.App.Services;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Persistence;

namespace MusicWorker.App.ViewModels;

/// <summary>
/// Automatische Sicherung des Mischpults in einen eigenen Ordner (nie über die Projektdatei des Nutzers).
/// Audio wird nur geschrieben, wenn es neu oder verändert ist – Puffer sind unveränderlich, daher genügt
/// der Vergleich der Objekte.
/// </summary>
public sealed partial class MixerViewModel
{
    private const string RecoveryProjectName = "Projekt.mwproj";

    private readonly Dictionary<AudioBuffer, string> _recoveryFiles = new(ReferenceEqualityComparer.Instance);
    private ProjectFile? _lastRecoveryProject;

    /// <summary>Sichert ungespeicherte Änderungen nach <paramref name="folder"/>; ohne Änderungen wird die Sicherung entfernt.</summary>
    public async Task AutosaveAsync(string folder)
    {
        if (IsBusy || IsRecording)
        {
            return;
        }

        if (!IsModified || !HasTracks)
        {
            if (_lastRecoveryProject is not null || Directory.Exists(folder))
            {
                RecoveryStore.Delete(folder);
                _recoveryFiles.Clear();
                _lastRecoveryProject = null;
            }

            return;
        }

        string projectPath = Path.Combine(folder, RecoveryProjectName);
        var project = CreateProjectHeader();
        project.OriginalPath = ProjectPath;
        var toWrite = new List<(string File, AudioBuffer Audio)>();
        var used = new HashSet<AudioBuffer>(ReferenceEqualityComparer.Instance);

        string FileFor(AudioBuffer audio, string prefix)
        {
            used.Add(audio);
            if (!_recoveryFiles.TryGetValue(audio, out var file))
            {
                file = $"{prefix}-{Guid.NewGuid().ToString("N")[..10]}.wav";
                _recoveryFiles[audio] = file;
                toWrite.Add((file, audio));
            }

            return file;
        }

        foreach (var track in Tracks)
        {
            var data = CreateProjectTrack(track);
            foreach (var clip in track.Clips)
            {
                if (clip.Notes is { } notes)
                {
                    data.Clips.Add(new ProjectClip
                    {
                        Start = clip.StartFrame,
                        LengthTicks = clip.LengthTicks,
                        Notes = notes.Select(n => new long[] { n.Pitch, n.Start, n.Length, n.Velocity, n.Voice }).ToList(),
                    });
                }
                else if (clip.Takes is { } takes && clip.Comp is { } comp)
                {
                    data.Clips.Add(new ProjectClip
                    {
                        Start = clip.StartFrame,
                        Takes = takes.Select(take => FileFor(take, "Take")).ToList(),
                        Comp = comp.Select(x => new long[] { x.Start, x.End, x.Take }).ToList(),
                    });
                }
                else
                {
                    data.Clips.Add(new ProjectClip { File = FileFor(clip.Audio, "Clip"), Start = clip.StartFrame });
                }
            }

            project.Tracks.Add(data);
        }

        foreach (var stale in _recoveryFiles.Keys.Where(b => !used.Contains(b)).ToArray())
        {
            _recoveryFiles.Remove(stale);
        }

        var usedFiles = _recoveryFiles.Values.ToArray();
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(folder);
                foreach (var (file, audio) in toWrite)
                {
                    _projects.SaveClip(projectPath, file, audio);
                }

                // Erst wenn alles Audio da ist, die Projektdatei ersetzen – so ist die Sicherung immer vollständig.
                _projects.WriteAtomic(projectPath, project);
                _projects.RemoveUnusedAudio(projectPath, usedFiles);
            });
            _lastRecoveryProject = project;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Beim nächsten Mal alles neu schreiben.
            _recoveryFiles.Clear();
        }
    }

    /// <summary>Öffnet eine automatische Sicherung; das Projekt gilt danach als ungespeichert und behält seinen ursprünglichen Speicherort.</summary>
    public async Task RestoreAutosaveAsync(string folder)
    {
        string projectPath = Path.Combine(folder, RecoveryProjectName);
        if (!File.Exists(projectPath))
        {
            projectPath = Directory.EnumerateFiles(folder, "*.mwproj").FirstOrDefault() ?? projectPath;
        }

        string? original = null;
        try
        {
            original = _projects.Read(projectPath).OriginalPath;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            // Wird beim Öffnen gemeldet.
        }

        await OpenProjectFileAsync(projectPath);
        if (HasTracks)
        {
            ProjectPath = original;
            IsModified = true;
            StatusText = "Mischpult-Projekt aus der automatischen Sicherung wiederhergestellt – bitte speichern.";
        }
    }
}
