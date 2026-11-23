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
    private readonly List<MixerState> _undo = [];
    private readonly Stack<MixerState> _redo = new();
    private MixEngine? _engine;
    private MixEngineSampleProvider? _provider;
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

    // Master-Effekte
    [ObservableProperty]
    private double _masterEqLow;

    [ObservableProperty]
    private double _masterEqMid;

    [ObservableProperty]
    private double _masterEqHigh;

    [ObservableProperty]
    private double _masterCompression;

    /// <summary>Begrenzer auf der Summe (verhindert Übersteuerung).</summary>
    [ObservableProperty]
    private bool _masterLimiter = true;

    // Raster
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BpmText))]
    private double _bpm = 120;

    /// <summary>Schläge pro Takt (Taktart x/4).</summary>
    [ObservableProperty]
    private int _beatsPerBar = 4;

    [ObservableProperty]
    private bool _isSnapEnabled;

    [ObservableProperty]
    private bool _showGrid = true;

    /// <summary>Insert-Regler (EQ, Hochpass, Kompressor) in den Kanalzügen anzeigen.</summary>
    [ObservableProperty]
    private bool _showInserts = true;

    // Loop-Bereich
    [ObservableProperty]
    private bool _isLooping;

    [ObservableProperty]
    private long _loopStart;

    [ObservableProperty]
    private long _loopEnd;

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

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Auswahl für die Taktart.</summary>
    public IReadOnlyList<int> BeatsPerBarChoices { get; } = [2, 3, 4, 5, 6, 7];

    public string BpmText
    {
        get => Bpm.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);
        set
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double bpm))
            {
                Bpm = Math.Clamp(bpm, 20, 300);
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Länge eines Schlags in Frames (für Raster und Einrasten).</summary>
    public double BeatFrames => 60.0 / Math.Max(1, Bpm) * SampleRate;

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
        RecordUndo();
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
        RecordUndo();
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
        RecordUndo();
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

        if (move.DragStarted)
        {
            RecordUndo(); // ein Rückgängig-Schritt pro Ziehvorgang
        }

        long start = Math.Max(0, move.NewStart);
        if (IsSnapEnabled)
        {
            start = (long)Math.Round(Math.Round(start / BeatFrames) * BeatFrames); // auf den nächsten Schlag einrasten
        }

        var clip = move.Clip;
        clip.StartFrame = start;
        if (move.TargetTrack is { } target && !ReferenceEquals(target, clip.Track) && Tracks.Contains(target))
        {
            clip.Track.Clips.Remove(clip);
            clip.Track = target;
            target.Clips.Add(clip);
        }

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
        RecordUndo();
        clip.Audio = Conform(audio);
        OnStructureChanged();
        StatusText = $"Clip in „{clip.Track.Name}“ aktualisiert.";
        return true;
    }

    // ───────────── Schnitt ─────────────

    /// <summary>Trenner: teilt den Clip unter der Abspielposition (bevorzugt den gewählten) in zwei Clips.</summary>
    [RelayCommand(CanExecute = nameof(CanEditWithTracks))]
    private void SplitClip()
    {
        long at = PlayheadFrame;
        var clip = SelectedClip is { } selected && at > selected.StartFrame && at < selected.EndFrame
            ? selected
            : Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => at > c.StartFrame && at < c.EndFrame);
        if (clip is null)
        {
            StatusText = "Zum Teilen die Abspielposition (roter Griff) mitten in einen Clip setzen.";
            return;
        }

        StopPlayback();
        RecordUndo();
        int cut = (int)(at - clip.StartFrame);
        var right = new ClipViewModel(clip.Track, clip.Audio.Slice(cut, clip.Audio.FrameCount - cut), at);
        clip.Audio = clip.Audio.Slice(0, cut);
        clip.Track.Clips.Insert(clip.Track.Clips.IndexOf(clip) + 1, right);
        SelectedClip = right;
        OnStructureChanged();
        StatusText = $"Clip in „{clip.Track.Name}“ bei {TimeFormatter.Format(FrameToTime(at))} geteilt.";
    }

    /// <summary>Clip-Zusammenführung: verbindet den gewählten Clip mit dem nächsten auf derselben Spur.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedClip))]
    private void MergeWithNextClip()
    {
        if (SelectedClip is not { } clip)
        {
            return;
        }

        var next = clip.Track.Clips
            .Where(c => !ReferenceEquals(c, clip) && c.StartFrame >= clip.StartFrame)
            .OrderBy(c => c.StartFrame)
            .FirstOrDefault();
        if (next is null)
        {
            StatusText = "Rechts vom gewählten Clip gibt es auf dieser Spur keinen weiteren Clip.";
            return;
        }

        StopPlayback();
        RecordUndo();
        var (audio, start) = ClipTools.Merge([new MixClip(clip.Audio, clip.StartFrame), new MixClip(next.Audio, next.StartFrame)]);
        clip.Audio = Conform(audio);
        clip.StartFrame = start;
        clip.Track.Clips.Remove(next);
        OnStructureChanged();
        StatusText = "Clips zu einem Clip verbunden (Lücken als Stille, Überlappungen überblendet).";
    }

    /// <summary>Auto-Zuschnitt: entfernt Stille am Anfang und Ende des gewählten Clips.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedClip))]
    private void AutoTrimClip()
    {
        if (SelectedClip is not { } clip)
        {
            return;
        }

        var content = ClipTools.FindContent(clip.Audio);
        if (content.IsEmpty)
        {
            StatusText = "Der Clip ist komplett still – nichts zum Zuschneiden.";
            return;
        }

        if (content.Start == 0 && content.End == clip.Audio.FrameCount)
        {
            StatusText = "Der Clip hat am Anfang und Ende keine Stille.";
            return;
        }

        StopPlayback();
        RecordUndo();
        long removed = clip.Audio.FrameCount - content.Length;
        clip.Audio = clip.Audio.Slice(content.Start, content.Length);
        clip.StartFrame += content.Start; // der hörbare Teil bleibt an seiner Stelle
        OnStructureChanged();
        StatusText = $"Auto-Zuschnitt: {TimeFormatter.Format(FrameToTime(removed))} Stille entfernt.";
    }

    /// <summary>
    /// Aneinanderreihen / Mixtape: legt alle Clips der Spur direkt hintereinander – auf Wunsch mit
    /// Überblendung (die Clips überlappen dann und werden automatisch übergeblendet).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanEditWithTracks))]
    private void ArrangeClips()
    {
        var track = SelectedClip?.Track ?? Tracks.FirstOrDefault();
        if (track is null || track.Clips.Count < 2)
        {
            StatusText = "Zum Aneinanderreihen braucht die (gewählte) Spur mindestens zwei Clips – Clips lassen sich per Maus auf eine Spur ziehen.";
            return;
        }

        var request = new ValueRequest(
            "Clips aneinanderreihen", $"Überblendung zwischen den Clips der Spur „{track.Name}“:",
            "0 = nahtlos aneinander (Trenner-Schnitt). Ab etwa 2 Sekunden entsteht ein Mixtape mit weichen Übergängen.",
            Minimum: 0, Maximum: 10, Value: 0, SmallChange: 0.5, Unit: "Sekunden");
        if (_dialogs.AskValue(request) is not { } seconds)
        {
            return;
        }

        StopPlayback();
        RecordUndo();
        long overlap = (long)(seconds * SampleRate);
        var ordered = track.Clips.OrderBy(c => c.StartFrame).ToArray();
        long position = ordered[0].StartFrame;
        for (int i = 0; i < ordered.Length; i++)
        {
            var clip = ordered[i];
            clip.StartFrame = position;
            long next = i + 1 < ordered.Length ? ordered[i + 1].Audio.FrameCount : long.MaxValue;
            long fade = Math.Min(overlap, Math.Min(clip.Audio.FrameCount, next) / 2); // höchstens die halbe Clip-Länge
            position = Math.Max(position + 1, clip.EndFrame - fade);
        }

        OnStructureChanged();
        StatusText = seconds > 0
            ? $"Mixtape: {ordered.Length} Clips mit je {seconds:0.#} s Überblendung aneinandergereiht."
            : $"{ordered.Length} Clips nahtlos aneinandergereiht.";
    }

    /// <summary>Clip-Mischung: mischt alle hörbaren Spuren zu einer neuen Spur und schaltet die Quellen stumm.</summary>
    [RelayCommand(CanExecute = nameof(CanEditWithTracks))]
    private async Task BounceAsync()
    {
        StopPlayback();
        var sources = Tracks.Where(t => !t.Mute && t.Clips.Count > 0).ToArray();
        if (sources.Length == 0)
        {
            StatusText = "Keine hörbare Spur zum Mischen.";
            return;
        }

        IsBusy = true;
        StatusText = "Spuren werden zusammengemischt …";
        try
        {
            var snapshot = BuildSnapshot() with { MasterDb = 0, MasterInserts = null, MasterLimiter = false };
            var mix = await Task.Run(() => MixEngine.Mixdown(snapshot, TailFrames() / (double)SampleRate));
            RecordUndo();
            foreach (var track in sources)
            {
                track.Mute = true;
            }

            var bounced = new TrackViewModel("Mischung", Palette[_nextColor++ % Palette.Length], OnMixChanged) { Group = NoGroup };
            var clip = new ClipViewModel(bounced, mix, 0);
            bounced.Clips.Add(clip);
            Tracks.Add(bounced);
            SelectedClip = clip;
            OnStructureChanged();
            StatusText = $"{sources.Length} Spuren in die neue Spur „Mischung“ gemischt – die Quellen sind stummgeschaltet (Rückgängig möglich).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ───────────── Rückgängig ─────────────

    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        StopPlayback();
        _redo.Push(Capture());
        var state = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Restore(state);
        StatusText = "Rückgängig gemacht.";
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        StopPlayback();
        _undo.Add(Capture());
        Restore(_redo.Pop());
        StatusText = "Wiederholt.";
    }

    // ───────────── Loop-Bereich ─────────────

    [RelayCommand(CanExecute = nameof(HasSelectedClip))]
    private void LoopSelectedClip()
    {
        if (SelectedClip is { } clip)
        {
            LoopStart = clip.StartFrame;
            LoopEnd = clip.EndFrame;
            IsLooping = true;
            StatusText = "Loop-Bereich = gewählter Clip. Tipp: Umschalt + Ziehen in der Zeitleiste legt einen eigenen Bereich fest.";
        }
    }

    [RelayCommand]
    private void SetLoopRange(LoopRange? range)
    {
        if (range is null || range.End - range.Start < SampleRate / 20)
        {
            return;
        }

        long start = range.Start;
        long end = range.End;
        if (IsSnapEnabled)
        {
            start = (long)(Math.Round(start / BeatFrames) * BeatFrames);
            end = (long)(Math.Round(end / BeatFrames) * BeatFrames);
        }

        LoopStart = Math.Min(start, end);
        LoopEnd = Math.Max(start, end);
        IsLooping = true;
    }

    public void ToggleLoop()
    {
        if (LoopEnd <= LoopStart)
        {
            // Noch kein Bereich: ganzes Projekt
            LoopStart = 0;
            LoopEnd = ProjectLength;
        }

        IsLooping = !IsLooping;
        StatusText = IsLooping ? "Loop ein." : "Loop aus.";
    }

    // ───────────── Gruppen ─────────────

    [RelayCommand]
    private void AddGroup()
    {
        RecordUndo();
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

        RecordUndo();
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
        if (IsLooping && LoopEnd > LoopStart && (start < LoopStart || start >= LoopEnd))
        {
            start = LoopStart; // Loop aktiv: im Loop-Bereich beginnen
        }

        _playStart = start;
        _engine = new MixEngine(BuildSnapshot());
        _engine.Seek(start);
        _provider = new MixEngineSampleProvider(_engine, () => ProjectLength + TailFrames(), () => (IsLooping, LoopStart, LoopEnd));

        try
        {
            _playback.PlayStream(_provider, (int)start);
        }
        catch (Exception ex)
        {
            _engine = null;
            _provider = null;
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
        if (_provider is not null && _playback.State != PlaybackState.Stopped)
        {
            _provider.Seek(frame);
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
        _provider = null;
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
            MasterEqLow = project.MasterEqLow;
            MasterEqMid = project.MasterEqMid;
            MasterEqHigh = project.MasterEqHigh;
            MasterCompression = project.MasterCompression;
            MasterLimiter = project.MasterLimiter;
            Bpm = project.Bpm > 0 ? project.Bpm : 120;
            BeatsPerBar = project.BeatsPerBar > 0 ? project.BeatsPerBar : 4;
            IsSnapEnabled = project.Snap;
            LoopStart = project.LoopStart;
            LoopEnd = project.LoopEnd;

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
                    EqLow = t.EqLow,
                    EqMid = t.EqMid,
                    EqHigh = t.EqHigh,
                    HighPassHz = t.HighPass,
                    Compression = t.Compression,
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
            ClearUndo();
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
            MasterDb,
            new TrackInserts(MasterEqLow, MasterEqMid, MasterEqHigh, 20, MasterCompression) is { IsNeutral: false } master ? master : null,
            MasterLimiter);
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
        MasterEqLow = MasterEqMid = MasterEqHigh = MasterCompression = 0;
        MasterLimiter = true;
        IsLooping = false;
        LoopStart = LoopEnd = 0;
        ClearUndo();
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
            MasterEqLow = MasterEqLow,
            MasterEqMid = MasterEqMid,
            MasterEqHigh = MasterEqHigh,
            MasterCompression = MasterCompression,
            MasterLimiter = MasterLimiter,
            Bpm = Bpm,
            BeatsPerBar = BeatsPerBar,
            Snap = IsSnapEnabled,
            LoopStart = LoopStart,
            LoopEnd = LoopEnd,
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
                EqLow = track.EqLow,
                EqMid = track.EqMid,
                EqHigh = track.EqHigh,
                HighPass = track.HighPassHz,
                Compression = track.Compression,
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

    // ───────────── Rückgängig: Zustand sichern und wiederherstellen ─────────────

    private sealed record ClipState(AudioBuffer Audio, long Start, string? SavedFile, AudioBuffer? SavedAudio, string? SavedPath);

    private sealed record TrackState(
        string Name, string Color, double VolumeDb, double Pan, bool Mute, bool Solo, double SendHall, double SendEcho,
        int GroupIndex, double EqLow, double EqMid, double EqHigh, double HighPassHz, double Compression, ClipState[] Clips);

    private sealed record GroupState(string Name, double VolumeDb, bool Mute);

    private sealed record MixerState(TrackState[] Tracks, GroupState[] Groups, int SelectedTrack, int SelectedClip, int NextColor);

    /// <summary>Merkt sich den aktuellen Zustand für „Rückgängig“ (höchstens 50 Schritte).</summary>
    private void RecordUndo()
    {
        _undo.Add(Capture());
        if (_undo.Count > 50)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        NotifyUndo();
    }

    private void ClearUndo()
    {
        _undo.Clear();
        _redo.Clear();
        NotifyUndo();
    }

    private void NotifyUndo()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private MixerState Capture()
    {
        int selectedTrack = SelectedClip is { } s ? Tracks.IndexOf(s.Track) : -1;
        int selectedClip = SelectedClip is { } c && selectedTrack >= 0 ? Tracks[selectedTrack].Clips.IndexOf(c) : -1;
        return new MixerState(
            Tracks.Select(t => new TrackState(
                t.Name, t.Color, t.VolumeDb, t.Pan, t.Mute, t.Solo, t.SendHall, t.SendEcho,
                t.Group is null || t.Group.IsNone ? -1 : Groups.IndexOf(t.Group),
                t.EqLow, t.EqMid, t.EqHigh, t.HighPassHz, t.Compression,
                t.Clips.Select(c => new ClipState(c.Audio, c.StartFrame, c.SavedFileName, c.SavedAudio, c.SavedProjectPath)).ToArray()))
                .ToArray(),
            Groups.Select(g => new GroupState(g.Name, g.VolumeDb, g.Mute)).ToArray(),
            selectedTrack,
            selectedClip,
            _nextColor);
    }

    private void Restore(MixerState state)
    {
        SelectedClip = null;
        Tracks.Clear();
        foreach (var group in Groups.ToArray())
        {
            GroupChoices.Remove(group);
        }

        Groups.Clear();
        foreach (var g in state.Groups)
        {
            var group = new GroupViewModel(g.Name, OnMixChanged) { VolumeDb = g.VolumeDb, Mute = g.Mute };
            Groups.Add(group);
            GroupChoices.Add(group);
        }

        foreach (var t in state.Tracks)
        {
            var track = new TrackViewModel(t.Name, t.Color, OnMixChanged)
            {
                VolumeDb = t.VolumeDb,
                Pan = t.Pan,
                Mute = t.Mute,
                Solo = t.Solo,
                SendHall = t.SendHall,
                SendEcho = t.SendEcho,
                Group = t.GroupIndex >= 0 && t.GroupIndex < Groups.Count ? Groups[t.GroupIndex] : NoGroup,
                EqLow = t.EqLow,
                EqMid = t.EqMid,
                EqHigh = t.EqHigh,
                HighPassHz = t.HighPassHz,
                Compression = t.Compression,
            };
            foreach (var c in t.Clips)
            {
                track.Clips.Add(new ClipViewModel(track, c.Audio, c.Start)
                {
                    SavedFileName = c.SavedFile,
                    SavedAudio = c.SavedAudio,
                    SavedProjectPath = c.SavedPath,
                });
            }

            Tracks.Add(track);
        }

        _nextColor = state.NextColor;
        if (state.SelectedTrack >= 0 && state.SelectedTrack < Tracks.Count &&
            state.SelectedClip >= 0 && state.SelectedClip < Tracks[state.SelectedTrack].Clips.Count)
        {
            SelectedClip = Tracks[state.SelectedTrack].Clips[state.SelectedClip];
        }

        OnStructureChanged();
        NotifyUndo();
    }

    partial void OnMasterEqLowChanged(double value) => OnMixChanged();

    partial void OnMasterEqMidChanged(double value) => OnMixChanged();

    partial void OnMasterEqHighChanged(double value) => OnMixChanged();

    partial void OnMasterCompressionChanged(double value) => OnMixChanged();

    partial void OnMasterLimiterChanged(bool value) => OnMixChanged();

    partial void OnBpmChanged(double value)
    {
        IsModified = true;
        TimelineVersion++;
    }

    partial void OnBeatsPerBarChanged(int value)
    {
        IsModified = true;
        TimelineVersion++;
    }

    partial void OnIsSnapEnabledChanged(bool value) => TimelineVersion++;

    partial void OnIsLoopingChanged(bool value) => TimelineVersion++;

    partial void OnLoopStartChanged(long value) => TimelineVersion++;

    partial void OnLoopEndChanged(long value) => TimelineVersion++;

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
                if (_provider is { } provider)
                {
                    PlayheadFrame = provider.MapPlayedToTimeline(_playback.PlayedFrames);
                }

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
        _provider = null;
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
        MergeWithNextClipCommand.NotifyCanExecuteChanged();
        AutoTrimClipCommand.NotifyCanExecuteChanged();
        LoopSelectedClipCommand.NotifyCanExecuteChanged();
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
            SplitClipCommand, MergeWithNextClipCommand, AutoTrimClipCommand, ArrangeClipsCommand, BounceCommand,
            LoopSelectedClipCommand,
            PlayCommand, TogglePlayPauseCommand, ExportMixdownCommand, NewProjectCommand,
            OpenProjectCommand, SaveProjectCommand, SaveProjectAsCommand,
        ];
        foreach (var command in commands)
        {
            command.NotifyCanExecuteChanged();
        }
    }
}
