using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Audio;
using MusicWorker.App.Helpers;
using MusicWorker.App.Services;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;
using MusicWorker.Core.Mixing;
using NAudio.Wave;

namespace MusicWorker.App.ViewModels;

/// <summary>
/// Steuerlogik des Mehrspur-Mischpults: Spuren und Clips, Kanalzüge, Gruppen, Effekt-Busse,
/// Master, Live-Wiedergabe über die Misch-Engine, Mixdown sowie Projekte speichern/laden.
/// </summary>
public sealed partial class MixerViewModel : ObservableObject, IDisposable
{
    private static readonly string[] Palette =
        ["#4FC3F7", "#F06292", "#81C784", "#FFB74D", "#BA68C8", "#4DB6AC", "#E57373", "#AED581"];

    private static readonly TimeSpan MeterInterval = TimeSpan.FromMilliseconds(33);
    private const double MeterFallPerFrame = 0.025;

    private readonly IDialogService _dialogs;
    private readonly ProjectService _projects;
    private readonly AudioFileService _files;
    private readonly PlaybackService _playback = new();
    private MixEngine? _engine;
    private CancellationTokenSource? _updates;
    private long _playStart;
    private int _nextColor;

    /// <summary>Abtastrate des Projekts (vom ersten Clip übernommen).</summary>
    [ObservableProperty]
    private int _sampleRate = 44100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MasterVolumeText))]
    private double _masterDb;

    [ObservableProperty]
    private double _masterMeterLeft;

    [ObservableProperty]
    private double _masterMeterRight;

    [ObservableProperty]
    private ClipViewModel? _selectedClip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText))]
    private long _playheadFrame;

    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>Wird bei jeder Änderung an Spuren/Clips erhöht – die Spuransicht zeichnet dann neu.</summary>
    [ObservableProperty]
    private int _timelineVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectTitle))]
    private string? _projectPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectTitle))]
    private bool _isModified;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Füge Spuren hinzu: Audiodateien hierher ziehen oder „Spur aus Datei …“.";

    public MixerViewModel(IDialogService dialogs, AudioFileService files, ProjectService projects)
    {
        _dialogs = dialogs;
        _files = files;
        _projects = projects;

        NoGroup = new GroupViewModel("Keine Gruppe", OnMixChanged, isNone: true);
        GroupChoices.Add(NoGroup);
        HallBus = new BusViewModel("Hall-Bus", BusEffect.Reverb, OnMixChanged);
        EchoBus = new BusViewModel("Echo-Bus", BusEffect.Delay, OnMixChanged);
        _playback.PlaybackEnded += OnPlaybackEnded;
        IsModified = false;
    }

    /// <summary>Ein Clip soll im Editor bearbeitet werden (das Hauptfenster übernimmt das).</summary>
    public event EventHandler<ClipViewModel>? OpenClipInEditorRequested;

    /// <summary>Die Mischpult-Wiedergabe startet gleich – andere Wiedergaben sollen anhalten.</summary>
    public event EventHandler? PlaybackStarting;

    public ObservableCollection<TrackViewModel> Tracks { get; } = [];

    public ObservableCollection<GroupViewModel> Groups { get; } = [];

    /// <summary>Auswahlliste für die Gruppe einer Spur (enthält „Keine Gruppe“).</summary>
    public ObservableCollection<GroupViewModel> GroupChoices { get; } = [];

    public GroupViewModel NoGroup { get; }

    public BusViewModel HallBus { get; }

    public BusViewModel EchoBus { get; }


    public bool HasTracks => Tracks.Count > 0;

    public bool HasSelectedClip => SelectedClip is not null;

    public long ProjectLength => Tracks.SelectMany(t => t.Clips).Select(c => c.EndFrame).DefaultIfEmpty(0).Max();

    public string MasterVolumeText => TrackViewModel.FormatDb(MasterDb);

    public string ProjectTitle =>
        (IsModified ? "● " : string.Empty) + (ProjectPath is null ? "Neues Projekt" : Path.GetFileNameWithoutExtension(ProjectPath));

    public string PositionText =>
        $"{TimeFormatter.Format(FrameToTime(PlayheadFrame))} / {TimeFormatter.Format(FrameToTime(ProjectLength))}";

    // ───────────── Spuren & Clips ─────────────

    /// <summary>Fügt eine neue Spur mit einem Clip hinzu (Audio wird bei Bedarf an das Projekt angepasst).</summary>
    public TrackViewModel AddTrack(AudioBuffer audio, string name, long startFrame = 0)
    {
        if (Tracks.Count == 0 && !Tracks.SelectMany(t => t.Clips).Any())
        {
            SampleRate = audio.SampleRate; // erstes Audio bestimmt die Abtastrate
        }

        var track = new TrackViewModel(name, Palette[_nextColor++ % Palette.Length], OnMixChanged) { Group = NoGroup };
        var clip = new ClipViewModel(track, Conform(audio), Math.Max(0, startFrame));
        track.Clips.Add(clip);
        Tracks.Add(track);
        SelectedClip = clip;
        OnStructureChanged();
        StatusText = $"Spur „{name}“ hinzugefügt.";
        return track;
    }

    /// <summary>Lädt Audiodateien als neue Spuren (z. B. per Drag &amp; Drop).</summary>
    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        StopPlayback();
        IsBusy = true;
        try
        {
            foreach (string path in paths)
            {
                StatusText = $"Lade „{Path.GetFileName(path)}“ …";
                try
                {
                    var audio = await Task.Run(() => _files.Load(path));
                    AddTrack(audio, Path.GetFileNameWithoutExtension(path));
                }
                catch (Exception ex)
                {
                    _dialogs.ShowError($"„{Path.GetFileName(path)}“ konnte nicht geladen werden.\n\n{ex.Message}");
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task AddTrackFromFileAsync()
    {
        if (_dialogs.ShowOpenFileDialog(AudioFileService.OpenFilter) is { } path)
        {
            await AddFilesAsync([path]);
        }
    }

    [RelayCommand]
    private void RemoveTrack(TrackViewModel? track)
    {
        if (track is null || !_dialogs.Confirm($"Spur „{track.Name}“ mit allen Clips entfernen?", "Spur entfernen"))
        {
            return;
        }

        StopPlayback();
        if (SelectedClip?.Track == track)
        {
            SelectedClip = null;
        }

        Tracks.Remove(track);
        OnStructureChanged();
        StatusText = $"Spur „{track.Name}“ entfernt.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedClip))]
    private void DeleteSelectedClip()
    {
        if (SelectedClip is not { } clip)
        {
            return;
        }

        StopPlayback();
        clip.Track.Clips.Remove(clip);
        SelectedClip = null;
        OnStructureChanged();
        StatusText = $"Clip aus „{clip.Track.Name}“ gelöscht.";
    }

    [RelayCommand(CanExecute = nameof(HasSelectedClip))]
    private void OpenSelectedClipInEditor()
    {
        if (SelectedClip is { } clip)
        {
            OpenClipInEditor(clip);
        }
    }

    [RelayCommand]
    private void OpenClipInEditor(ClipViewModel? clip)
    {
        if (clip is not null)
        {
            StopPlayback();
            OpenClipInEditorRequested?.Invoke(this, clip);
        }
    }

    [RelayCommand]
    private void SelectClip(ClipViewModel? clip) => SelectedClip = clip;

    [RelayCommand]
    private void MoveClip(ClipMove? move)
    {
        if (move is null)
        {
            return;
        }

        move.Clip.StartFrame = Math.Max(0, move.NewStart);
        OnStructureChanged();
    }

    /// <summary>Ersetzt das Audio eines Clips (z. B. nach der Bearbeitung im Editor).</summary>
    public bool ReplaceClipAudio(ClipViewModel clip, AudioBuffer audio)
    {
        if (!Tracks.Any(t => t.Clips.Contains(clip)))
        {
            return false;
        }

        StopPlayback();
        clip.Audio = Conform(audio);
        OnStructureChanged();
        StatusText = $"Clip in „{clip.Track.Name}“ aktualisiert.";
        return true;
    }

    // ───────────── Gruppen ─────────────

    [RelayCommand]
    private void AddGroup()
    {
        var group = new GroupViewModel($"Gruppe {Groups.Count + 1}", OnMixChanged);
        Groups.Add(group);
        GroupChoices.Add(group);
        OnStructureChanged();
        StatusText = $"„{group.Name}“ angelegt – ordne Spuren im Kanalzug unten zu.";
    }

    [RelayCommand]
    private void RemoveGroup(GroupViewModel? group)
    {
        if (group is null || group.IsNone)
        {
            return;
        }

        foreach (var track in Tracks.Where(t => t.Group == group))
        {
            track.Group = NoGroup;
        }

        Groups.Remove(group);
        GroupChoices.Remove(group);
        OnStructureChanged();
    }

    // ───────────── Wiedergabe ─────────────

    [RelayCommand(CanExecute = nameof(HasTracks))]
    public void Play()
    {
        if (_playback.State == PlaybackState.Paused)
        {
            _playback.Resume();
            IsPlaying = true;
            StartUpdates();
            return;
        }

        PlaybackStarting?.Invoke(this, EventArgs.Empty);
        long start = PlayheadFrame >= ProjectLength ? 0 : PlayheadFrame;
        _playStart = start;
        _engine = new MixEngine(BuildSnapshot());
        _engine.Seek(start);

        try
        {
            _playback.PlayStream(new MixEngineSampleProvider(_engine, () => ProjectLength + TailFrames()), (int)start);
        }
        catch (Exception ex)
        {
            _engine = null;
            _dialogs.ShowError($"Die Wiedergabe konnte nicht gestartet werden.\n\n{ex.Message}");
            return;
        }

        IsPlaying = true;
        StartUpdates();
    }

    [RelayCommand]
    public void Pause()
    {
        if (IsPlaying)
        {
            _playback.Pause();
            IsPlaying = false;
            StopUpdates();
        }
    }

    [RelayCommand]
    public void Stop()
    {
        bool wasActive = _playback.State != PlaybackState.Stopped;
        StopPlayback();
        if (wasActive)
        {
            PlayheadFrame = _playStart;
        }
    }

    [RelayCommand(CanExecute = nameof(HasTracks))]
    public void TogglePlayPause()
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

    /// <summary>Springt an eine Stelle (Positionsgriff, Klick in die Zeitleiste).</summary>
    [RelayCommand]
    public void Seek(long frame)
    {
        frame = Math.Max(0, frame);
        PlayheadFrame = frame;
        if (_engine is not null && _playback.State != PlaybackState.Stopped)
        {
            _engine.Seek(frame);
            _playback.MarkSeek((int)frame);
        }
        else
        {
            _playStart = frame;
        }
    }

    public void SkipBy(TimeSpan offset) => Seek(PlayheadFrame + (long)(offset.TotalSeconds * SampleRate));

    /// <summary>Wiedergabegerät für das Mischpult festlegen.</summary>
    public void SetOutputDevice(int deviceNumber)
    {
        StopPlayback();
        _playback.DeviceNumber = deviceNumber;
    }

    /// <summary>Hält die Mischpult-Wiedergabe an (Position bleibt).</summary>
    public void StopPlayback()
    {
        _playback.Stop();
        _engine = null;
        IsPlaying = false;
        StopUpdates();
        ResetMeters();
    }

    // ───────────── Mixdown ─────────────

    /// <summary>Mischt das ganze Projekt zu einer Stereo-Datei (mit Ausklingen der Effekt-Busse).</summary>
    public AudioBuffer RenderMixdown()
    {
        var mix = MixEngine.Mixdown(BuildSnapshot(), TailFrames() / (double)SampleRate);
        float peak = 0;
        foreach (float sample in mix.Samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        // Übersteuert? Dann auf -1 dB absenken, statt hart zu verzerren.
        return peak > 0.999f ? new NormalizeEffect(-1).Apply(mix) : mix;
    }

    [RelayCommand(CanExecute = nameof(CanEditWithTracks))]
    private async Task ExportMixdownAsync()
    {
        string name = ProjectPath is null ? "Mixdown" : Path.GetFileNameWithoutExtension(ProjectPath) + " (Mixdown)";
        if (_dialogs.ShowSaveFileDialog(AudioFileService.ExportFilter, name + ".wav", "Mixdown exportieren") is not { } path)
        {
            return;
        }

        StopPlayback();
        IsBusy = true;
        StatusText = "Mixdown wird berechnet …";
        try
        {
            await Task.Run(() => _files.Export(RenderMixdown(), path));
            StatusText = $"Mixdown exportiert nach „{Path.GetFileName(path)}“.";
        }
        catch (Exception ex)
        {
            StatusText = "Mixdown fehlgeschlagen.";
            _dialogs.ShowError($"Der Mixdown konnte nicht gespeichert werden.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ───────────── Projekte ─────────────

    /// <summary>Fragt nach, falls das Projekt ungespeicherte Änderungen hat.</summary>
    public bool ConfirmDiscardChanges() =>
        !IsModified || !HasTracks || _dialogs.Confirm(
            "Das Mischpult-Projekt hat ungespeicherte Änderungen. Möchtest du sie verwerfen?",
            "Ungespeichertes Projekt");

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void NewProject()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        StopPlayback();
        ClearProject();
        StatusText = "Neues, leeres Projekt.";
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task OpenProjectAsync()
    {
        if (!ConfirmDiscardChanges() || _dialogs.ShowOpenFileDialog(ProjectService.Filter) is not { } path)
        {
            return;
        }

        StopPlayback();
        IsBusy = true;
        StatusText = $"Öffne Projekt „{Path.GetFileNameWithoutExtension(path)}“ …";
        var missing = new List<string>();
        try
        {
            var project = await Task.Run(() => _projects.Read(path));
            ClearProject();
            SampleRate = project.SampleRate > 0 ? project.SampleRate : 44100;
            MasterDb = project.MasterDb;

            foreach (var g in project.Groups)
            {
                var group = new GroupViewModel(g.Name, OnMixChanged) { VolumeDb = g.VolumeDb, Mute = g.Mute };
                Groups.Add(group);
                GroupChoices.Add(group);
            }

            ApplyBus(HallBus, project.Buses.ElementAtOrDefault(0));
            ApplyBus(EchoBus, project.Buses.ElementAtOrDefault(1));

            foreach (var t in project.Tracks)
            {
                var track = new TrackViewModel(t.Name, Palette[_nextColor++ % Palette.Length], OnMixChanged)
                {
                    VolumeDb = t.VolumeDb,
                    Pan = t.Pan,
                    Mute = t.Mute,
                    Solo = t.Solo,
                    SendHall = t.SendHall,
                    SendEcho = t.SendEcho,
                    Group = t.GroupIndex >= 0 && t.GroupIndex < Groups.Count ? Groups[t.GroupIndex] : NoGroup,
                };

                foreach (var c in t.Clips)
                {
                    if (!_projects.ClipExists(path, c.File))
                    {
                        missing.Add($"{t.Name}: {c.File}");
                        continue;
                    }

                    var audio = Conform(await Task.Run(() => _projects.LoadClip(path, c.File)));
                    track.Clips.Add(new ClipViewModel(track, audio, c.Start)
                    {
                        SavedFileName = c.File,
                        SavedAudio = audio,
                        SavedProjectPath = path,
                    });
                }

                Tracks.Add(track);
            }

            ProjectPath = path;
            OnStructureChanged();
            IsModified = false;
            StatusText = $"Projekt „{Path.GetFileNameWithoutExtension(path)}“ geöffnet – {Tracks.Count} Spuren.";
        }
        catch (Exception ex)
        {
            StatusText = "Öffnen fehlgeschlagen.";
            _dialogs.ShowError($"Das Projekt konnte nicht geöffnet werden.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }

        if (missing.Count > 0)
        {
            _dialogs.ShowError("Folgende Audiodateien des Projekts wurden nicht gefunden:\n\n" + string.Join("\n", missing));
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditWithTracks))]
    private async Task SaveProjectAsync()
    {
        if (ProjectPath is null)
        {
            await SaveProjectAsAsync();
        }
        else
        {
            await SaveToAsync(ProjectPath);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditWithTracks))]
    private async Task SaveProjectAsAsync()
    {
        string suggested = ProjectPath is null ? "Mein Projekt.mwproj" : Path.GetFileName(ProjectPath);
        if (_dialogs.ShowSaveFileDialog(ProjectService.Filter, suggested, "Projekt speichern") is { } path)
        {
            if (!path.EndsWith(".mwproj", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.ChangeExtension(path, ".mwproj");
            }

            await SaveToAsync(path);
        }
    }

    public void Dispose()
    {
        StopPlayback();
        _playback.PlaybackEnded -= OnPlaybackEnded;
        _playback.Dispose();
    }

    // ───────────── Hilfsmethoden ─────────────

    private bool CanEdit() => !IsBusy;

    private bool CanEditWithTracks() => !IsBusy && HasTracks;

    private TimeSpan FrameToTime(long frame) => TimeSpan.FromSeconds((double)frame / SampleRate);

    /// <summary>Wie lange die Effekt-Busse nach dem Projektende nachklingen sollen.</summary>
    private long TailFrames()
    {
        double seconds = 0;
        if (HallBus.ReturnDb > MixMath.SilenceDb && Tracks.Any(t => t.SendHall > 0))
        {
            seconds = Math.Max(seconds, HallBus.TailSeconds);
        }

        if (EchoBus.ReturnDb > MixMath.SilenceDb && Tracks.Any(t => t.SendEcho > 0))
        {
            seconds = Math.Max(seconds, EchoBus.TailSeconds);
        }

        return (long)(seconds * SampleRate);
    }

    /// <summary>Erzeugt den aktuellen Zustand für die Misch-Engine.</summary>
    public MixSnapshot BuildSnapshot()
    {
        var tracks = Tracks.Select(t => t.ToMixTrack(t.Group is null || t.Group.IsNone ? -1 : Groups.IndexOf(t.Group))).ToArray();
        return new MixSnapshot(
            SampleRate,
            tracks,
            Groups.Select(g => g.ToMixGroup()).ToArray(),
            [HallBus.ToMixBus(), EchoBus.ToMixBus()],
            MasterDb);
    }

    /// <summary>Passt Audio an das Projekt an: höchstens Stereo, Abtastrate des Projekts.</summary>
    private AudioBuffer Conform(AudioBuffer audio)
    {
        if (audio.Channels > 2)
        {
            audio = audio.ExtractChannels([0, 1]);
        }

        if (audio.SampleRate != SampleRate)
        {
            var resampled = Resampler.ChangeSpeed(audio, audio.SampleRate / (double)SampleRate);
            audio = new AudioBuffer(resampled.Samples.ToArray(), SampleRate, resampled.Channels);
        }

        return audio;
    }

    /// <summary>Fader, Panorama usw. geändert → sofort hörbar, Projekt als geändert markieren.</summary>
    private void OnMixChanged()
    {
        IsModified = true;
        _engine?.Update(BuildSnapshot());
    }

    /// <summary>Spuren oder Clips geändert → zusätzlich Spuransicht neu zeichnen und Befehle aktualisieren.</summary>
    private void OnStructureChanged()
    {
        OnMixChanged();
        TimelineVersion++;
        OnPropertyChanged(nameof(HasTracks));
        OnPropertyChanged(nameof(PositionText));
        RefreshCommands();
    }

    private void ClearProject()
    {
        Tracks.Clear();
        foreach (var group in Groups.ToArray())
        {
            GroupChoices.Remove(group);
        }

        Groups.Clear();
        SelectedClip = null;
        SampleRate = 44100;
        MasterDb = 0;
        PlayheadFrame = 0;
        _playStart = 0;
        _nextColor = 0;
        ProjectPath = null;
        OnStructureChanged();
        IsModified = false;
    }

    private static void ApplyBus(BusViewModel bus, ProjectBus? data)
    {
        if (data is null)
        {
            return;
        }

        bus.ReturnDb = data.ReturnDb;
        bus.RoomSize = data.RoomSize;
        bus.Damping = data.Damping;
        bus.TimeLeft = data.TimeLeft;
        bus.TimeRight = data.TimeRight;
        bus.Feedback = data.Feedback;
        bus.PingPong = data.PingPong;
    }

    private static ProjectBus ToProjectBus(BusViewModel bus) => new()
    {
        Name = bus.Name,
        ReturnDb = bus.ReturnDb,
        RoomSize = bus.RoomSize,
        Damping = bus.Damping,
        TimeLeft = bus.TimeLeft,
        TimeRight = bus.TimeRight,
        Feedback = bus.Feedback,
        PingPong = bus.PingPong,
    };

    private async Task SaveToAsync(string path)
    {
        StopPlayback();
        IsBusy = true;
        StatusText = $"Speichere Projekt „{Path.GetFileNameWithoutExtension(path)}“ …";

        var project = new ProjectFile
        {
            SampleRate = SampleRate,
            MasterDb = MasterDb,
            Groups = Groups.Select(g => new ProjectGroup { Name = g.Name, VolumeDb = g.VolumeDb, Mute = g.Mute }).ToList(),
            Buses = [ToProjectBus(HallBus), ToProjectBus(EchoBus)],
        };

        // Nur neues oder verändertes Audio wird geschrieben; Unverändertes behält seine Datei.
        var toWrite = new List<(ClipViewModel Clip, string File)>();
        var used = new List<string>();
        for (int t = 0; t < Tracks.Count; t++)
        {
            var track = Tracks[t];
            var data = new ProjectTrack
            {
                Name = track.Name,
                VolumeDb = track.VolumeDb,
                Pan = track.Pan,
                Mute = track.Mute,
                Solo = track.Solo,
                SendHall = track.SendHall,
                SendEcho = track.SendEcho,
                GroupIndex = track.Group is null || track.Group.IsNone ? -1 : Groups.IndexOf(track.Group),
            };

            for (int c = 0; c < track.Clips.Count; c++)
            {
                var clip = track.Clips[c];
                bool unchanged = clip.SavedFileName is not null &&
                    ReferenceEquals(clip.SavedAudio, clip.Audio) &&
                    string.Equals(clip.SavedProjectPath, path, StringComparison.OrdinalIgnoreCase) &&
                    _projects.ClipExists(path, clip.SavedFileName);
                string file = unchanged
                    ? clip.SavedFileName!
                    : $"Spur{t + 1:00}-Clip{c + 1:00}-{Guid.NewGuid().ToString("N")[..8]}.wav";
                if (!unchanged)
                {
                    toWrite.Add((clip, file));
                }

                used.Add(file);
                data.Clips.Add(new ProjectClip { File = file, Start = clip.StartFrame });
            }

            project.Tracks.Add(data);
        }

        try
        {
            await Task.Run(() =>
            {
                foreach (var (clip, file) in toWrite)
                {
                    _projects.SaveClip(path, file, clip.Audio);
                }

                _projects.Write(path, project);
                _projects.RemoveUnusedAudio(path, used);
            });

            foreach (var track in Tracks)
            {
                foreach (var clip in track.Clips)
                {
                    var written = toWrite.FirstOrDefault(w => w.Clip == clip);
                    if (written.Clip is not null)
                    {
                        clip.SavedFileName = written.File;
                    }

                    clip.SavedAudio = clip.Audio;
                    clip.SavedProjectPath = path;
                }
            }

            ProjectPath = path;
            IsModified = false;
            StatusText = $"Projekt gespeichert: „{Path.GetFileName(path)}“ (Audio im Ordner „{Path.GetFileName(ProjectService.AudioFolderFor(path))}“).";
        }
        catch (Exception ex)
        {
            StatusText = "Speichern fehlgeschlagen.";
            _dialogs.ShowError($"Das Projekt konnte nicht gespeichert werden.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async void StartUpdates()
    {
        StopUpdates();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        _updates = cts;

        using var timer = new PeriodicTimer(MeterInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token) && !token.IsCancellationRequested)
            {
                PlayheadFrame = _playback.CurrentFrame;
                UpdateMeters();
            }
        }
        catch (OperationCanceledException)
        {
            // beendet
        }
        finally
        {
            if (ReferenceEquals(_updates, cts))
            {
                _updates = null;
            }
        }
    }

    private void StopUpdates()
    {
        _updates?.Cancel();
        _updates = null;
    }

    /// <summary>Pegelanzeigen: sofort steigen, langsam fallen; Skala -60 … 0 dBFS.</summary>
    private void UpdateMeters()
    {
        if (_engine is not { } engine)
        {
            return;
        }

        for (int i = 0; i < Tracks.Count; i++)
        {
            Tracks[i].Meter = Math.Max(ToMeter(engine.GetTrackPeak(i)), Tracks[i].Meter - MeterFallPerFrame);
        }

        MasterMeterLeft = Math.Max(ToMeter(engine.GetMasterPeak(0)), MasterMeterLeft - MeterFallPerFrame);
        MasterMeterRight = Math.Max(ToMeter(engine.GetMasterPeak(1)), MasterMeterRight - MeterFallPerFrame);
    }

    private void ResetMeters()
    {
        foreach (var track in Tracks)
        {
            track.Meter = 0;
        }

        MasterMeterLeft = MasterMeterRight = 0;
    }

    private static double ToMeter(float peak) =>
        peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);

    private void OnPlaybackEnded(object? sender, StoppedEventArgs e)
    {
        IsPlaying = false;
        _engine = null;
        StopUpdates();
        ResetMeters();
        PlayheadFrame = _playStart;
        if (e.Exception is not null)
        {
            _dialogs.ShowError($"Bei der Wiedergabe ist ein Fehler aufgetreten.\n\n{e.Exception.Message}");
        }
    }

    partial void OnSelectedClipChanged(ClipViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedClip));
        DeleteSelectedClipCommand.NotifyCanExecuteChanged();
        OpenSelectedClipInEditorCommand.NotifyCanExecuteChanged();
        TimelineVersion++;
    }

    partial void OnMasterDbChanged(double value) => OnMixChanged();

    partial void OnIsBusyChanged(bool value) => RefreshCommands();

    partial void OnIsPlayingChanged(bool value) => PauseCommand.NotifyCanExecuteChanged();

    private void RefreshCommands()
    {
        IRelayCommand[] commands =
        [
            AddTrackFromFileCommand, DeleteSelectedClipCommand, OpenSelectedClipInEditorCommand,
            PlayCommand, TogglePlayPauseCommand, ExportMixdownCommand, NewProjectCommand,
            OpenProjectCommand, SaveProjectCommand, SaveProjectAsCommand,
        ];
        foreach (var command in commands)
        {
            command.NotifyCanExecuteChanged();
        }
    }
}
