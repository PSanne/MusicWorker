using MusicWorker.Core.Recording;
using MusicWorker.Core.Notes;
using MusicWorker.Core.Synthesis;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Audio;
using MusicWorker.App.Helpers;
using MusicWorker.App.Services;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Editing;
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
    private TrackViewModel? _automationTrack;
    private readonly RecordingService _recorder = new();
    private RecordingSession? _session;
    private CancellationTokenSource? _levelLoop;
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

    // Aufnahme
    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isMetronomeEnabled;

    /// <summary>Vor der Aufnahme einen Takt einzählen.</summary>
    [ObservableProperty]
    private bool _isCountInEnabled = true;

    /// <summary>Punch-In: nur im Loop-Bereich aufnehmen (der Rest bleibt unangetastet).</summary>
    [ObservableProperty]
    private bool _isPunchEnabled;

    /// <summary>Eingangspegel 0 … 1 (-60 … 0 dBFS).</summary>
    [ObservableProperty]
    private double _inputLevel;

    [ObservableProperty]
    private IReadOnlyList<InputDevice> _inputDevices = [];

    [ObservableProperty]
    private InputDevice? _selectedInputDevice;

    /// <summary>Beginn der laufenden Aufnahme (für die rote Markierung in der Spuransicht), -1 = keine.</summary>
    [ObservableProperty]
    private long _recordingStartFrame = -1;

    /// <summary>Hüllkurven-Modus: Die Maus bearbeitet in der Spuransicht Hüllkurven statt Clips.</summary>
    [ObservableProperty]
    private bool _isEnvelopeMode;

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
        NoTrack = new TrackViewModel("Keine", "#9E9E9E", static () => { });
        DuckChoices.Add(NoTrack);
        Tracks.CollectionChanged += OnTracksCollectionChanged;
        _recorder.Failed += (_, message) => StatusText = $"Aufnahme-Fehler: {message}";
        RefreshInputDevices();
        HallBus = new BusViewModel("Hall-Bus", BusEffect.Reverb, OnMixChanged);
        EchoBus = new BusViewModel("Echo-Bus", BusEffect.Delay, OnMixChanged);
        _playback.PlaybackEnded += OnPlaybackEnded;
        IsModified = false;
    }

    /// <summary>Ein Clip soll im Editor bearbeitet werden (das Hauptfenster übernimmt das).</summary>
    public event EventHandler<ClipViewModel>? OpenClipInEditorRequested;

    /// <summary>Ein Notenclip soll im Noten-Editor bearbeitet werden.</summary>
    public event EventHandler<ClipViewModel>? OpenNoteClipRequested;

    /// <summary>Die Mischpult-Wiedergabe startet gleich – andere Wiedergaben sollen anhalten.</summary>
    public event EventHandler? PlaybackStarting;

    public ObservableCollection<TrackViewModel> Tracks { get; } = [];

    public ObservableCollection<GroupViewModel> Groups { get; } = [];

    /// <summary>Auswahlliste „Absenken durch“ (enthält „Keine“ und alle Spuren).</summary>
    public ObservableCollection<TrackViewModel> DuckChoices { get; } = [];

    /// <summary>Platzhalter „Keine“ in <see cref="DuckChoices"/>.</summary>
    public TrackViewModel NoTrack { get; }

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

    /// <summary>Latenzausgleich für Aufnahmen in Millisekunden (Verzögerung von Aus- und Eingang).</summary>
    public double RecordingLatencyMs { get; set; } = 60;

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

        var track = CreateTrack(name, Palette[_nextColor++ % Palette.Length]);
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
        if (clip is null)
        {
            return;
        }

        if (clip.IsNoteClip)
        {
            SelectedClip = clip;
            OpenNoteClipRequested?.Invoke(this, clip);
            return;
        }

        if (clip.HasTakes)
        {
            SelectedClip = clip;
            EditTakes();
            return;
        }

        StopPlayback();
        OpenClipInEditorRequested?.Invoke(this, clip);
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
        if (move.TargetTrack is { } target && !ReferenceEquals(target, clip.Track) && Tracks.Contains(target) &&
            target.IsNoteTrack == clip.IsNoteClip) // Notenclips nur auf Notenspuren, Audio nur auf Audiospuren
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
        clip.Takes = null; // im Editor bearbeitet: die Takes werden zur bearbeiteten Fassung
        clip.Comp = null;
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
        Flatten(clip);
        ClipViewModel right;
        if (clip.Notes is { } notes)
        {
            long tick = Math.Clamp(FramesToTicks(at - clip.StartFrame), 1, Math.Max(1, clip.LengthTicks - 1));
            var (leftNotes, rightNotes) = NoteTools.Split(notes, tick);
            right = CreateNoteClip(clip.Track, rightNotes, Math.Max(1, clip.LengthTicks - tick), clip.StartFrame + TicksToFrames(tick));
            UpdateNoteClip(clip, leftNotes, tick);
        }
        else
        {
            int cut = (int)(at - clip.StartFrame);
            right = new ClipViewModel(clip.Track, clip.Audio.Slice(cut, clip.Audio.FrameCount - cut), at);
            clip.Audio = clip.Audio.Slice(0, cut);
        }

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

        if (clip.IsNoteClip != next.IsNoteClip)
        {
            StatusText = "Ein Notenclip und ein Audioclip lassen sich nicht verbinden.";
            return;
        }

        StopPlayback();
        RecordUndo();
        Flatten(clip);
        Flatten(next);
        if (clip.Notes is { } notes && next.Notes is { } nextNotes)
        {
            long offset = FramesToTicks(next.StartFrame - clip.StartFrame);
            UpdateNoteClip(clip, NoteTools.Merge(notes, nextNotes, offset), Math.Max(clip.LengthTicks, offset + next.LengthTicks));
        }
        else
        {
            var (audio, start) = ClipTools.Merge([new MixClip(clip.Audio, clip.StartFrame), new MixClip(next.Audio, next.StartFrame)]);
            clip.Audio = Conform(audio);
            clip.StartFrame = start;
        }

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

        if (clip.Notes is { } notes)
        {
            TrimNoteClip(clip, notes);
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
        Flatten(clip);
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
            long next = i + 1 < ordered.Length ? ordered[i + 1].LengthFrames : long.MaxValue;
            long fade = clip.IsNoteClip || (i + 1 < ordered.Length && ordered[i + 1].IsNoteClip)
                ? 0 // Notenclips werden nicht übergeblendet, sondern lückenlos aneinandergelegt
                : Math.Min(overlap, Math.Min(clip.LengthFrames, next) / 2); // höchstens die halbe Clip-Länge
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
            await EnsureNotesRenderedAsync();
            var snapshot = BuildSnapshot() with { MasterDb = 0, MasterInserts = null, MasterLimiter = false };
            var mix = await Task.Run(() => MixEngine.Mixdown(snapshot, TailFrames() / (double)SampleRate));
            RecordUndo();
            foreach (var track in sources)
            {
                track.Mute = true;
            }

            var bounced = CreateTrack("Mischung", Palette[_nextColor++ % Palette.Length]);
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

    // ───────────── Aufnahme ─────────────

    /// <summary>Aufnahme starten bzw. beenden (Knopf ● oder Strg+R).</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    public void ToggleRecord()
    {
        if (IsRecording)
        {
            FinishRecording();
        }
        else
        {
            StartRecording();
        }
    }

    [RelayCommand]
    private void RefreshInputDevices()
    {
        string? current = SelectedInputDevice?.Name;
        InputDevices = RecordingService.Devices();
        SelectedInputDevice = InputDevices.FirstOrDefault(d => d.Name == current) ?? InputDevices.FirstOrDefault();
    }

    /// <summary>Wählt den gespeicherten Eingang (falls vorhanden).</summary>
    public void SelectInputByName(string? name)
    {
        if (name is not null && InputDevices.FirstOrDefault(d => d.Name == name) is { } device)
        {
            SelectedInputDevice = device;
        }
    }

    /// <summary>Takes &amp; Comping für den gewählten Clip (auch per Doppelklick auf einen Clip mit Takes).</summary>
    [RelayCommand]
    private void EditTakes()
    {
        if (SelectedClip is not { Takes: { Count: > 0 } takes, Comp: { } comp } clip)
        {
            StatusText = "Der gewählte Clip hat keine Takes. Takes entstehen, wenn man über einen vorhandenen Clip noch einmal aufnimmt.";
            return;
        }

        StopPlayback();
        if (_dialogs.EditTakes(new TakesRequest(clip.Track.Name, takes, comp, _playback.DeviceNumber)) is not { } result)
        {
            return;
        }

        RecordUndo();
        if (result.Flatten || result.Takes.Count < 2)
        {
            clip.Audio = TakeTools.Compose(result.Takes, result.Comp);
            clip.Takes = null;
            clip.Comp = null;
            StatusText = "Takes zu einem Clip zusammengefügt.";
        }
        else
        {
            SetTakes(clip, result.Takes, result.Comp);
            StatusText = $"Zusammenstellung übernommen ({result.Comp.Count} Abschnitte aus {result.Takes.Count} Takes).";
        }

        OnStructureChanged();
    }

    /// <summary>Vom Noten-Editor: Taste gedrückt (während einer Aufnahme auf einer Notenspur).</summary>
    public void RecordNoteOn(int pitch, int velocity)
    {
        if (_session is { NoteTrack: not null } session && _provider is { } provider)
        {
            session.Open[pitch] = (provider.MapPlayedToTimeline(_playback.PlayedFrames), velocity);
        }
    }

    public void RecordNoteOff(int pitch)
    {
        if (_session is { NoteTrack: not null } session && _provider is { } provider && session.Open.Remove(pitch, out var on))
        {
            session.Notes.Add((pitch, on.Frame, provider.MapPlayedToTimeline(_playback.PlayedFrames), on.Velocity));
        }
    }

    /// <summary>Nimmt gerade eine Notenspur auf (dann gehören gespielte Töne in die Aufnahme statt in die Schritt-Eingabe).</summary>
    public bool IsRecordingNotes => _session?.NoteTrack is not null;

    private void StartRecording()
    {
        var armed = Tracks.Where(t => t.IsArmed).ToArray();
        var audioTrack = armed.FirstOrDefault(t => !t.IsNoteTrack);
        var noteTrack = armed.FirstOrDefault(t => t.IsNoteTrack);
        bool created = false;
        StopPlayback();
        RecordUndo(); // die ganze Aufnahme ist ein Rückgängig-Schritt
        if (audioTrack is null && noteTrack is null)
        {
            audioTrack = CreateTrack($"Aufnahme {Tracks.Count(t => t.Name.StartsWith("Aufnahme", StringComparison.Ordinal)) + 1}", Palette[_nextColor++ % Palette.Length]);
            Tracks.Add(audioTrack);
            audioTrack.IsArmed = true;
            created = true;
            OnStructureChanged();
        }

        if (audioTrack is not null)
        {
            string? problem = SelectedInputDevice is null
                ? "Es wurde kein Aufnahmegerät (Mikrofon oder Eingang) gefunden."
                : null;
            if (SelectedInputDevice is { } device)
            {
                _recorder.DeviceNumber = device.Number;
                if (_recorder.Open(SampleRate, device.Channels) is { } error)
                {
                    problem = $"Der Eingang „{device.Name}“ konnte nicht geöffnet werden.\n\n{error}";
                }
            }

            if (problem is not null)
            {
                if (created)
                {
                    Tracks.Remove(audioTrack);
                    OnStructureChanged();
                }

                _dialogs.ShowError(problem);
                return;
            }
        }

        bool punch = IsPunchEnabled && LoopEnd > LoopStart;
        long recordFrom = punch ? LoopStart : PlayheadFrame;
        long playFrom = punch ? Math.Max(0, LoopStart - TicksToFrames(MusicalTime.TicksPerBar(BeatsPerBar))) : PlayheadFrame; // ein Takt Vorlauf
        long countIn = IsCountInEnabled ? TicksToFrames(MusicalTime.TicksPerBar(BeatsPerBar)) : 0;
        _session = new RecordingSession(recordFrom, punch ? LoopEnd : long.MaxValue, audioTrack, noteTrack, created);
        IsRecording = true;
        RecordingStartFrame = recordFrom;
        if (!StartPlayback(playFrom, countIn, audioTrack is null ? null : _recorder.StartCapture))
        {
            _session = null;
            IsRecording = false;
            RecordingStartFrame = -1;
            return;
        }

        StartLevelLoop();
        StatusText = punch
            ? $"Punch-In-Aufnahme: aufgenommen wird nur von {TimeFormatter.Format(FrameToTime(LoopStart))} bis {TimeFormatter.Format(FrameToTime(LoopEnd))}. Stopp mit Leertaste oder Esc."
            : "Aufnahme läuft … Stopp mit Leertaste oder Esc.";
    }

    private void FinishRecording()
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        var provider = _provider;
        var recorded = session.AudioTrack is not null && _recorder.IsCapturing ? _recorder.StopCapture() : null;
        long stop = provider is null ? PlayheadFrame : provider.MapPlayedToTimeline(_playback.PlayedFrames);
        IsRecording = false;
        RecordingStartFrame = -1;
        StopPlayback();
        UpdateInputMonitoring();

        int takes = 0;
        if (recorded is not null && provider is not null && session.AudioTrack is { } audioTrack)
        {
            long latency = (long)(RecordingLatencyMs / 1000 * SampleRate);
            var runs = RecordingMapper.Limit(RecordingMapper.Map(recorded.FrameCount, provider.Jumps, latency), session.From, session.To)
                .Where(r => r.Length >= SampleRate / 10) // Schnipsel unter 0,1 s ignorieren
                .ToArray();
            foreach (var run in runs)
            {
                AddRecording(audioTrack, Conform(recorded.Slice((int)run.SourceStart, (int)run.Length)), run.TimelineStart);
                takes++;
            }

            if (takes == 0 && session.CreatedTrack)
            {
                Tracks.Remove(audioTrack);
            }
        }

        int notes = 0;
        if (session.NoteTrack is { } noteTrack)
        {
            foreach (var (pitch, on) in session.Open)
            {
                session.Notes.Add((pitch, on.Frame, stop, on.Velocity));
            }

            notes = AddNoteRecording(noteTrack, session.Notes.Where(n => n.Start >= session.From && n.Start < session.To).ToArray());
        }

        OnStructureChanged();
        PlayheadFrame = session.From;
        StatusText = (takes, notes) switch
        {
            (0, 0) => "Aufnahme beendet – es wurde nichts aufgenommen.",
            (_, 0) => takes == 1 ? $"Aufnahme in „{session.AudioTrack!.Name}“ übernommen." : $"{takes} Durchgänge als Takes in „{session.AudioTrack!.Name}“ übernommen (Doppelklick auf den Clip öffnet das Comping).",
            (0, _) => $"{notes} Noten in „{session.NoteTrack!.Name}“ aufgenommen.",
            _ => $"Aufnahme übernommen: {takes} Audio-Durchgang/-Durchgänge und {notes} Noten.",
        };
    }

    /// <summary>Legt eine Aufnahme auf die Spur: über einem vorhandenen Clip als neuer Take, sonst als neuer Clip.</summary>
    private void AddRecording(TrackViewModel track, AudioBuffer audio, long start)
    {
        long end = start + audio.FrameCount;
        var target = track.Clips
            .Where(c => !c.IsNoteClip && c.StartFrame < end && c.EndFrame > start)
            .OrderByDescending(c => Math.Min(c.EndFrame, end) - Math.Max(c.StartFrame, start))
            .FirstOrDefault();
        if (target is null)
        {
            var clip = new ClipViewModel(track, audio, start);
            track.Clips.Add(clip);
            SelectedClip = clip;
            return;
        }

        var takes = target.Takes ?? [target.Audio];
        var comp = target.Comp ?? TakeTools.ClearComp(target.Audio.FrameCount, 0);
        var (newTakes, newStart, newComp) = TakeTools.AddTake(takes, target.StartFrame, comp, audio, start);
        target.StartFrame = newStart;
        SetTakes(target, newTakes, newComp);
        SelectedClip = target;
    }

    /// <summary>Aufgenommene Noten (Frames) in einen Notenclip übernehmen – in einen vorhandenen (dazu gemischt) oder einen neuen.</summary>
    private int AddNoteRecording(TrackViewModel track, IReadOnlyList<(int Pitch, long Start, long End, int Velocity)> notes)
    {
        if (notes.Count == 0)
        {
            return 0;
        }

        long first = notes.Min(n => n.Start);
        var clip = track.Clips.FirstOrDefault(c => c.IsNoteClip && c.StartFrame <= first && c.EndFrame > first);
        long clipStart = clip?.StartFrame ?? BarStart(first);
        var converted = notes.Select(n => new Note(
            n.Pitch,
            Math.Max(0, FramesToTicks(n.Start - clipStart)),
            Math.Max(MusicalTime.TicksPerQuarter / 16, FramesToTicks(n.End - n.Start)),
            n.Velocity)).ToArray();
        long bar = MusicalTime.TicksPerBar(BeatsPerBar);
        long end = (NoteTools.EndOf(converted) + bar - 1) / bar * bar;
        if (clip is null)
        {
            track.Clips.Add(CreateNoteClip(track, NoteTools.Sorted(converted), Math.Max(bar, end), clipStart));
        }
        else
        {
            UpdateNoteClip(clip, NoteTools.Sorted(clip.Notes!.Concat(converted)), Math.Max(clip.LengthTicks, end));
        }

        return notes.Count;
    }

    private void SetTakes(ClipViewModel clip, IReadOnlyList<AudioBuffer> takes, IReadOnlyList<CompSegment> comp)
    {
        clip.Takes = takes;
        clip.Comp = comp;
        clip.Audio = TakeTools.Compose(takes, comp);
    }

    /// <summary>Takes zur aktuellen Zusammenstellung zusammenfügen (vor Teilen, Verbinden und Zuschneiden).</summary>
    private static void Flatten(ClipViewModel clip)
    {
        clip.Takes = null;
        clip.Comp = null;
    }

    /// <summary>Eingang offen halten, solange eine Audiospur scharfgeschaltet ist (für die Pegelanzeige).</summary>
    private void UpdateInputMonitoring()
    {
        if (IsRecording)
        {
            return;
        }

        if (Tracks.Any(t => t.IsArmed && !t.IsNoteTrack) && SelectedInputDevice is { } device)
        {
            _recorder.DeviceNumber = device.Number;
            if (_recorder.Open(SampleRate, device.Channels) is { } error)
            {
                StatusText = $"Eingang „{device.Name}“ lässt sich nicht öffnen: {error}";
                return;
            }

            StartLevelLoop();
        }
        else
        {
            _levelLoop?.Cancel();
            _recorder.Close();
            InputLevel = 0;
        }
    }

    private async void StartLevelLoop()
    {
        _levelLoop?.Cancel();
        using var cts = new CancellationTokenSource();
        _levelLoop = cts;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token) && _recorder.IsOpen)
            {
                float peak = _recorder.TakePeak();
                double level = peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);
                InputLevel = Math.Max(level, InputLevel * 0.85);
            }
        }
        catch (OperationCanceledException)
        {
            // beendet
        }
    }

    partial void OnSelectedInputDeviceChanged(InputDevice? value)
    {
        _recorder.Close();
        UpdateInputMonitoring();
    }

    /// <summary>Eine laufende Aufnahme.</summary>
    private sealed class RecordingSession(long from, long to, TrackViewModel? audioTrack, TrackViewModel? noteTrack, bool createdTrack)
    {
        public long From { get; } = from;

        public long To { get; } = to;

        public TrackViewModel? AudioTrack { get; } = audioTrack;

        public TrackViewModel? NoteTrack { get; } = noteTrack;

        public bool CreatedTrack { get; } = createdTrack;

        public Dictionary<int, (long Frame, int Velocity)> Open { get; } = [];

        public List<(int Pitch, long Start, long End, int Velocity)> Notes { get; } = [];
    }

    // ───────────── Notenspuren ─────────────

    /// <summary>Neue Notenspur (Klavier) mit einem leeren Clip von 4 Takten an der Abspielposition.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddNoteTrack()
    {
        StopPlayback();
        RecordUndo();
        var track = CreateTrack($"Noten {Tracks.Count(t => t.IsNoteTrack) + 1}", Palette[_nextColor++ % Palette.Length]);
        track.Instrument = new InstrumentSpec(BuiltInInstrument.Piano);
        var clip = CreateNoteClip(track, [], MusicalTime.TicksPerBar(BeatsPerBar) * 4, BarStart(PlayheadFrame));
        track.Clips.Add(clip);
        Tracks.Add(track);
        SelectedClip = clip;
        OnStructureChanged();
        StatusText = "Notenspur angelegt – im Noten-Editor Noten zeichnen oder auf der Tastatur spielen.";
        OpenNoteClipRequested?.Invoke(this, clip);
    }

    /// <summary>Doppelklick auf eine freie Stelle einer Notenspur: neuer leerer Notenclip (4 Takte).</summary>
    [RelayCommand]
    private void CreateClipAt(LanePosition? position)
    {
        if (position is null || !position.Track.IsNoteTrack || !Tracks.Contains(position.Track))
        {
            return;
        }

        RecordUndo();
        var clip = CreateNoteClip(position.Track, [], MusicalTime.TicksPerBar(BeatsPerBar) * 4, BarStart(position.Frame));
        position.Track.Clips.Add(clip);
        SelectedClip = clip;
        OnStructureChanged();
        OpenNoteClipRequested?.Invoke(this, clip);
    }

    /// <summary>Erzeugt einen Notenclip und berechnet seinen Klang im Hintergrund.</summary>
    public ClipViewModel CreateNoteClip(TrackViewModel track, IReadOnlyList<Note> notes, long lengthTicks, long startFrame)
    {
        var clip = new ClipViewModel(track, notes, lengthTicks, Math.Max(0, startFrame), TicksToFrames(lengthTicks), SampleRate);
        RequestRender(clip, immediate: true);
        return clip;
    }

    /// <summary>Noten bzw. Länge eines Notenclips ändern (vom Noten-Editor) – der Klang wird kurz danach neu berechnet.</summary>
    public void UpdateNoteClip(ClipViewModel clip, IReadOnlyList<Note> notes, long lengthTicks)
    {
        clip.Notes = notes;
        clip.LengthTicks = Math.Max(1, lengthTicks);
        clip.NoteLengthFrames = TicksToFrames(clip.LengthTicks);
        IsModified = true;
        TimelineVersion++;
        OnPropertyChanged(nameof(PositionText));
        RequestRender(clip);
    }

    /// <summary>Für die Undo-Aufzeichnung aus dem Noten-Editor.</summary>
    public void RecordNoteUndo() => RecordUndo();

    public long TicksToFrames(long ticks) => MusicalTime.TicksToFrames(ticks, Bpm, SampleRate);

    public long FramesToTicks(long frames) => MusicalTime.FramesToTicks(frames, Bpm, SampleRate);

    /// <summary>Anfang des Takts, in dem <paramref name="frame"/> liegt.</summary>
    public long BarStart(long frame)
    {
        long bar = MusicalTime.TicksPerBar(BeatsPerBar);
        return TicksToFrames(FramesToTicks(Math.Max(0, frame)) / bar * bar);
    }

    /// <summary>Wartet, bis alle Notenclips berechnet sind (vor Mixdown und Export).</summary>
    public async Task EnsureNotesRenderedAsync()
    {
        foreach (var clip in Tracks.SelectMany(t => t.Clips).Where(c => c.NeedsRender).ToArray())
        {
            int version = clip.RenderVersion;
            var (notes, spec, bpm, rate, length) = (clip.Notes!, clip.Track.Instrument ?? new InstrumentSpec(), Bpm, SampleRate, clip.NoteLengthFrames);
            var audio = await Task.Run(() => NoteRenderer.Render(notes, spec, bpm, rate, length));
            if (clip.RenderVersion == version)
            {
                clip.Audio = audio;
                clip.RenderedVersion = version;
            }
        }

        _engine?.Update(BuildSnapshot());
    }

    private void RequestRender(ClipViewModel clip, bool immediate = false)
    {
        if (clip.IsNoteClip)
        {
            _ = RenderAsync(clip, ++clip.RenderVersion, immediate);
        }
    }

    private async Task RenderAsync(ClipViewModel clip, int version, bool immediate)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(150); // beim Ziehen nicht jede Zwischenstellung berechnen
            }

            if (clip.RenderVersion != version || clip.Notes is not { } notes)
            {
                return;
            }

            var spec = clip.Track.Instrument ?? new InstrumentSpec();
            double bpm = Bpm;
            int rate = SampleRate;
            long length = clip.NoteLengthFrames;
            var audio = await Task.Run(() => NoteRenderer.Render(notes, spec, bpm, rate, length));
            if (clip.RenderVersion != version)
            {
                return;
            }

            clip.Audio = audio;
            clip.RenderedVersion = version;
            _engine?.Update(BuildSnapshot());
            TimelineVersion++;
        }
        catch (Exception ex)
        {
            StatusText = $"Die Noten konnten nicht in Klang umgerechnet werden: {ex.Message}";
        }
    }

    /// <summary>Notenclips neu berechnen (Tempo, Abtastrate oder Instrument geändert).</summary>
    private void RenderNoteClips(TrackViewModel? only = null)
    {
        foreach (var clip in Tracks.Where(t => only is null || ReferenceEquals(t, only)).SelectMany(t => t.Clips).Where(c => c.IsNoteClip))
        {
            clip.NoteLengthFrames = TicksToFrames(clip.LengthTicks);
            RequestRender(clip, immediate: true);
        }

        TimelineVersion++;
    }

    /// <summary>Auto-Zuschnitt für Notenclips: Clip beginnt am Schlag der ersten Note und endet nach der letzten.</summary>
    private void TrimNoteClip(ClipViewModel clip, IReadOnlyList<Note> notes)
    {
        if (notes.Count == 0)
        {
            StatusText = "Der Notenclip enthält keine Noten – nichts zum Zuschneiden.";
            return;
        }

        long beat = MusicalTime.TicksPerQuarter;
        long first = notes.Min(n => n.Start) / beat * beat;
        long last = (NoteTools.EndOf(notes) + beat - 1) / beat * beat;
        if (first == 0 && last == clip.LengthTicks)
        {
            StatusText = "Der Notenclip ist schon genau so lang wie seine Noten.";
            return;
        }

        StopPlayback();
        RecordUndo();
        clip.StartFrame += TicksToFrames(first);
        UpdateNoteClip(clip, NoteTools.Move(notes, -first), last - first);
        OnStructureChanged();
        StatusText = "Auto-Zuschnitt: Der Notenclip reicht jetzt genau von der ersten bis zur letzten Note.";
    }

    /// <summary>
    /// Stimmaufteilung: Jede Stimme eines Notenclips wird eine eigene Notenspur (gleiches Instrument, gleiche
    /// Position). Die ursprüngliche Spur wird stummgeschaltet – so bleibt alles mit Strg+Z umkehrbar.
    /// </summary>
    public void SplitVoicesToTracks(ClipViewModel clip, IReadOnlyList<Note> notes, int voices)
    {
        if (!clip.IsNoteClip || !Tracks.Contains(clip.Track))
        {
            return;
        }

        StopPlayback();
        RecordUndo();
        var source = clip.Track;
        int insertAt = Tracks.IndexOf(source) + 1;
        int created = 0;
        for (int voice = 0; voice < voices; voice++)
        {
            var voiceNotes = notes.Where(n => n.Voice == voice).Select(n => n with { Voice = 0 }).ToArray();
            if (voiceNotes.Length == 0)
            {
                continue;
            }

            var track = CreateTrack($"{source.Name} – Stimme {voice + 1}", Palette[_nextColor++ % Palette.Length]);
            track.Apply(source);
            track.Mute = false;
            track.Clips.Add(CreateNoteClip(track, voiceNotes, clip.LengthTicks, clip.StartFrame));
            Tracks.Insert(insertAt + created++, track);
        }

        source.Mute = true;
        OnStructureChanged();
        StatusText = $"Noten auf {created} Spuren verteilt – die ursprüngliche Spur „{source.Name}“ ist stummgeschaltet.";
    }

    // ───────────── MIDI ─────────────

    /// <summary>MIDI-Datei als Notenspuren importieren (Klänge nach General MIDI zugeordnet).</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ImportMidi()
    {
        if (_dialogs.ShowOpenFileDialog("MIDI-Dateien (*.mid;*.midi)|*.mid;*.midi|Alle Dateien (*.*)|*.*") is not { } path)
        {
            return;
        }

        MidiSong song;
        try
        {
            song = MidiFile.Read(path);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Die MIDI-Datei konnte nicht gelesen werden.\n\n{ex.Message}");
            return;
        }

        var tracks = song.Tracks.Where(t => t.Notes.Count > 0).ToArray();
        if (tracks.Length == 0)
        {
            StatusText = "Die MIDI-Datei enthält keine Noten.";
            return;
        }

        StopPlayback();
        RecordUndo();
        if (Tracks.Count == 0)
        {
            Bpm = song.Bpm; // leeres Projekt übernimmt Tempo und Taktart
            BeatsPerBar = song.BeatsPerBar;
        }

        long bar = MusicalTime.TicksPerBar(BeatsPerBar);
        long start = BarStart(PlayheadFrame);
        foreach (var data in tracks)
        {
            var track = CreateTrack(data.Name, Palette[_nextColor++ % Palette.Length]);
            track.Instrument = new InstrumentSpec(Instruments.FromGeneralMidi(data.Program, data.Channel));
            long length = Math.Max(bar, (NoteTools.EndOf(data.Notes) + bar - 1) / bar * bar);
            track.Clips.Add(CreateNoteClip(track, data.Notes, length, start));
            Tracks.Add(track);
        }

        OnStructureChanged();
        StatusText = $"MIDI „{Path.GetFileName(path)}“ importiert: {tracks.Length} Notenspur(en), {tracks.Sum(t => t.Notes.Count)} Noten.";
    }

    /// <summary>Alle Notenspuren als MIDI-Datei exportieren.</summary>
    [RelayCommand(CanExecute = nameof(CanEditWithTracks))]
    private void ExportMidi()
    {
        var noteTracks = Tracks.Where(t => t.IsNoteTrack && t.Clips.Any(c => c.Notes is { Count: > 0 })).ToArray();
        if (noteTracks.Length == 0)
        {
            StatusText = "Es gibt keine Notenspur mit Noten zum Exportieren.";
            return;
        }

        string name = ProjectPath is null ? "Noten" : Path.GetFileNameWithoutExtension(ProjectPath);
        if (_dialogs.ShowSaveFileDialog("MIDI-Datei (*.mid)|*.mid", name + ".mid", "Als MIDI exportieren") is not { } path)
        {
            return;
        }

        int channel = 0;
        var data = noteTracks.Select(track =>
        {
            int ch = 9; // Schlagzeug liegt nach General MIDI auf Kanal 10
            if (!track.Instrument!.IsDrums)
            {
                ch = channel++ % 15;
                if (ch >= 9)
                {
                    ch++;
                }
            }

            var notes = track.Clips.Where(c => c.Notes is not null)
                .SelectMany(c => NoteTools.Move(c.Notes!.Where(n => n.Start < c.LengthTicks), FramesToTicks(c.StartFrame)));
            int program = track.Instrument.IsSoundFont ? track.Instrument.Preset : Instruments.ToGeneralMidi(track.Instrument.BuiltIn);
            return new MidiTrackData(track.Name, ch, program, NoteTools.Sorted(notes));
        }).ToArray();

        try
        {
            MidiFile.Write(path, new MidiSong(Bpm, BeatsPerBar, data));
            StatusText = $"{data.Length} Notenspur(en) als MIDI exportiert nach „{Path.GetFileName(path)}“.";
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Die MIDI-Datei konnte nicht geschrieben werden.\n\n{ex.Message}");
        }
    }

    // ───────────── Hüllkurven ─────────────

    /// <summary>Punkt hinzufügen, verschieben oder löschen (aus der Spuransicht im Hüllkurven-Modus).</summary>
    [RelayCommand]
    private void EditAutomation(AutomationEdit? edit)
    {
        if (edit is null || !Tracks.Contains(edit.Track))
        {
            return;
        }

        var track = edit.Track;
        var target = track.ShownAutomation;
        var lane = track.LaneFor(target);
        long frame = Math.Max(0, edit.Frame);
        if (IsSnapEnabled)
        {
            frame = (long)Math.Round(Math.Round(frame / BeatFrames) * BeatFrames);
        }

        switch (edit.Kind)
        {
            case AutomationEditKind.Add:
                RecordUndo();
                (lane, edit.ResultIndex) = lane.WithPoint(frame, edit.Value);
                break;
            case AutomationEditKind.Move:
                if (edit.DragStarted)
                {
                    RecordUndo();
                }

                lane = lane.WithMovedPoint(edit.Index, frame, edit.Value);
                edit.ResultIndex = edit.Index;
                break;
            default:
                RecordUndo();
                lane = lane.WithoutPoint(edit.Index);
                break;
        }

        _automationTrack = track;
        track.SetLane(lane);
        TimelineVersion++;
        ClearAutomationCommand.NotifyCanExecuteChanged();
        StatusText = edit.Kind == AutomationEditKind.Remove
            ? $"Hüllkurven-Punkt gelöscht ({Automation.RangeOf(target).Name}, „{track.Name}“)."
            : $"{Automation.RangeOf(target).Name} „{track.Name}“ bei {TimeFormatter.Format(FrameToTime(frame))}: {Automation.Format(target, lane.Points[Math.Clamp(edit.ResultIndex, 0, lane.Points.Count - 1)].Value)}";
    }

    /// <summary>Löscht die gezeigte Hüllkurve der zuletzt bearbeiteten (bzw. gewählten) Spur.</summary>
    [RelayCommand]
    private void ClearAutomation()
    {
        var track = _automationTrack is { } last && Tracks.Contains(last) ? last : SelectedClip?.Track;
        if (track is null)
        {
            StatusText = "Erst einen Clip wählen oder einen Hüllkurven-Punkt setzen – dann lässt sich die Kurve dieser Spur löschen.";
            return;
        }

        var target = track.ShownAutomation;
        if (track.LaneFor(target).IsEmpty)
        {
            StatusText = $"Die Spur „{track.Name}“ hat keine Hüllkurve „{Automation.RangeOf(target).Name}“.";
            return;
        }

        RecordUndo();
        track.SetLane(new AutomationLane(target, []));
        TimelineVersion++;
        StatusText = $"Hüllkurve „{Automation.RangeOf(target).Name}“ von „{track.Name}“ gelöscht – es gilt wieder der Regler.";
    }

    partial void OnIsEnvelopeModeChanged(bool value)
    {
        TimelineVersion++;
        if (value)
        {
            StatusText = "Hüllkurven: Klick setzt einen Punkt, Ziehen verschiebt ihn, Doppelklick oder Rechtsklick löscht ihn. Links in der Spurliste wählst du, welche Kurve gezeigt wird.";
        }
    }

    /// <summary>Neue Spur mit Standardeinstellungen.</summary>
    private TrackViewModel CreateTrack(string name, string color) =>
        new(name, color, OnMixChanged)
        {
            Group = NoGroup,
            DuckSource = NoTrack,
            Redraw = () => TimelineVersion++,
            ArmedChanged = _ =>
            {
                TimelineVersion++;
                UpdateInputMonitoring();
            },
            InstrumentChanged = track =>
            {
                IsModified = true;
                OnPropertyChanged(nameof(Tracks));
                RenderNoteClips(track);
            },
        };

    /// <summary>Index der Spur, die <paramref name="track"/> absenkt (-1 = keine).</summary>
    private int DuckIndex(TrackViewModel track) =>
        track.DuckSource is { } source && !ReferenceEquals(source, NoTrack) && !ReferenceEquals(source, track)
            ? Tracks.IndexOf(source)
            : -1;

    /// <summary>Hält die Auswahlliste „Absenken durch“ aktuell.</summary>
    private void OnTracksCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        foreach (var removed in DuckChoices.Skip(1).Where(t => !Tracks.Contains(t)).ToArray())
        {
            DuckChoices.Remove(removed);
            foreach (var track in Tracks.Where(t => ReferenceEquals(t.DuckSource, removed)))
            {
                track.DuckSource = NoTrack;
            }
        }

        foreach (var added in Tracks.Where(t => !DuckChoices.Contains(t)))
        {
            DuckChoices.Add(added);
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

        long start = PlayheadFrame >= ProjectLength ? 0 : PlayheadFrame;
        if (IsLooping && LoopEnd > LoopStart && (start < LoopStart || start >= LoopEnd))
        {
            start = LoopStart; // Loop aktiv: im Loop-Bereich beginnen
        }

        StartPlayback(start, 0);
    }

    /// <summary>Startet die Wiedergabe ab <paramref name="start"/> (optional mit Vorzähler).</summary>
    private bool StartPlayback(long start, long countInFrames, Action? started = null)
    {
        PlaybackStarting?.Invoke(this, EventArgs.Empty);
        _playStart = start;
        _engine = new MixEngine(BuildSnapshot());
        _engine.Seek(start);
        _provider = new MixEngineSampleProvider(
            _engine,
            () => IsRecording ? long.MaxValue : ProjectLength + TailFrames(), // beim Aufnehmen über das Projektende hinaus weiterlaufen
            () => (IsLooping, LoopStart, LoopEnd),
            () => (IsMetronomeEnabled, Bpm, BeatsPerBar),
            countInFrames);

        try
        {
            started?.Invoke(); // z. B. Mitschnitt starten – direkt vor der Ausgabe, damit beides gleichzeitig beginnt
            _playback.PlayStream(WithLoudnessMeter(_provider), (int)start);
        }
        catch (Exception ex)
        {
            _engine = null;
            _provider = null;
            _dialogs.ShowError($"Die Wiedergabe konnte nicht gestartet werden.\n\n{ex.Message}");
            return false;
        }

        IsPlaying = true;
        StartUpdates();
        return true;
    }

    [RelayCommand]
    public void Pause()
    {
        if (IsRecording)
        {
            FinishRecording();
            return;
        }

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
        if (IsRecording)
        {
            FinishRecording();
            PlayheadFrame = _playStart;
            return;
        }

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

        await OpenProjectFileAsync(path);
    }

    /// <summary>Öffnet ein Projekt (.mwproj) – auch per Doppelklick im Explorer.</summary>
    public async Task OpenProjectFileAsync(string path)
    {
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
            Markers = MarkerList.Sorted(project.Markers.Where(m => m.Frame >= 0).Select(m => new Marker(m.Name, m.Frame)));

            var duckSources = new List<(TrackViewModel Track, int Source)>();
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
                var track = CreateTrack(t.Name, Palette[_nextColor++ % Palette.Length]);
                track.Apply(new TrackViewModel(t.Name, track.Color, static () => { })
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
                    DuckAmount = t.DuckAmount,
                    DuckThreshold = t.DuckThreshold,
                    ShownAutomation = Enum.TryParse<AutomationTarget>(t.ShownAutomation, out var shown) ? shown : AutomationTarget.Volume,
                    Automation = t.Automation
                        .Where(a => Enum.TryParse<AutomationTarget>(a.Target, out _))
                        .Select(a => new AutomationLane(
                            Enum.Parse<AutomationTarget>(a.Target),
                            a.Frames.Zip(a.Values, (f, v) => new AutomationPoint(f, v)).OrderBy(p => p.Frame).ToArray()))
                        .Where(l => !l.IsEmpty)
                        .ToArray(),
                    Instrument = t.Instrument is null ? null : new InstrumentSpec(
                        Enum.TryParse<BuiltInInstrument>(t.Instrument, out var builtIn) ? builtIn : BuiltInInstrument.Piano,
                        t.SoundFont, t.Bank, t.Preset, t.PresetName),
                });
                duckSources.Add((track, t.DuckSource));

                foreach (var c in t.Clips)
                {
                    if (c.Notes is { } projectNotes)
                    {
                        var notes = NoteTools.Sorted(projectNotes.Where(n => n.Length >= 4).Select(n =>
                            new Note((int)n[0], n[1], Math.Max(1, n[2]), (int)n[3], n.Length > 4 ? (int)n[4] : 0)));
                        track.Clips.Add(new ClipViewModel(track, notes, Math.Max(1, c.LengthTicks), c.Start, 0, SampleRate));
                        continue;
                    }

                    if (c.Takes is { Count: > 0 } takeFiles)
                    {
                        var present = takeFiles.Where(f => _projects.ClipExists(path, f)).ToArray();
                        if (present.Length == 0)
                        {
                            missing.Add($"{t.Name}: {string.Join(", ", takeFiles)}");
                            continue;
                        }

                        var takeAudio = new List<AudioBuffer>();
                        foreach (var file in present)
                        {
                            takeAudio.Add(Conform(await Task.Run(() => _projects.LoadClip(path, file))));
                        }

                        long takeLength = TakeTools.LengthOf(takeAudio);
                        var takeComp = TakeTools.Normalize(
                            (c.Comp ?? []).Where(x => x.Length >= 3).Select(x => new CompSegment(x[0], x[1], (int)Math.Clamp(x[2], 0, takeAudio.Count - 1))),
                            takeLength);
                        var takeClip = new ClipViewModel(track, TakeTools.Compose(takeAudio, takeComp), c.Start)
                        {
                            Takes = takeAudio,
                            Comp = takeComp,
                            SavedTakeFiles = present.Length == takeFiles.Count ? takeFiles.ToArray() : null,
                            SavedTakes = present.Length == takeFiles.Count ? takeAudio.ToArray() : null,
                            SavedTakesProjectPath = path,
                        };
                        track.Clips.Add(takeClip);
                        continue;
                    }

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

            foreach (var (track, source) in duckSources)
            {
                track.DuckSource = source >= 0 && source < Tracks.Count ? Tracks[source] : NoTrack;
            }

            RenderNoteClips();

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
        _levelLoop?.Cancel();
        _recorder.Dispose();
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

        long noteTail = Tracks.SelectMany(t => t.Clips)
            .Where(c => c.IsNoteClip)
            .Select(c => c.StartFrame + c.Audio.FrameCount - ProjectLength) // Ausklang der Instrumente
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max((long)(seconds * SampleRate), noteTail);
    }

    /// <summary>Erzeugt den aktuellen Zustand für die Misch-Engine.</summary>
    public MixSnapshot BuildSnapshot()
    {
        var tracks = Tracks.Select(t => t.ToMixTrack(t.Group is null || t.Group.IsNone ? -1 : Groups.IndexOf(t.Group), DuckIndex(t))).ToArray();
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
        Bpm = 120;
        BeatsPerBar = 4;
        Markers = MarkerList.Empty;
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

        var project = CreateProjectHeader();

        // Nur neues oder verändertes Audio wird geschrieben; Unverändertes behält seine Datei.
        var toWrite = new List<(ClipViewModel Clip, string File)>();
        var takesToWrite = new List<(ClipViewModel Clip, int Index, string File, AudioBuffer Audio)>();
        var used = new List<string>();
        for (int t = 0; t < Tracks.Count; t++)
        {
            var track = Tracks[t];
            var data = CreateProjectTrack(track);

            for (int c = 0; c < track.Clips.Count; c++)
            {
                var clip = track.Clips[c];
                if (clip.Notes is { } clipNotes)
                {
                    data.Clips.Add(new ProjectClip
                    {
                        Start = clip.StartFrame,
                        LengthTicks = clip.LengthTicks,
                        Notes = clipNotes.Select(n => new long[] { n.Pitch, n.Start, n.Length, n.Velocity, n.Voice }).ToList(),
                    });
                    continue;
                }

                if (clip.Takes is { } takes && clip.Comp is { } comp)
                {
                    var files = new List<string>();
                    for (int k = 0; k < takes.Count; k++)
                    {
                        string? saved = clip.SavedTakeFiles is { } names && k < names.Length ? names[k] : null;
                        bool same = saved is not null && clip.SavedTakes is { } audios && k < audios.Length &&
                            ReferenceEquals(audios[k], takes[k]) &&
                            string.Equals(clip.SavedTakesProjectPath, path, StringComparison.OrdinalIgnoreCase) &&
                            _projects.ClipExists(path, saved);
                        string takeFile = same ? saved! : $"Spur{t + 1:00}-Clip{c + 1:00}-Take{k + 1}-{Guid.NewGuid().ToString("N")[..8]}.wav";
                        if (!same)
                        {
                            takesToWrite.Add((clip, k, takeFile, takes[k]));
                        }

                        files.Add(takeFile);
                        used.Add(takeFile);
                    }

                    data.Clips.Add(new ProjectClip
                    {
                        Start = clip.StartFrame,
                        Takes = files,
                        Comp = comp.Select(x => new long[] { x.Start, x.End, x.Take }).ToList(),
                    });
                    continue;
                }

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

                foreach (var (_, _, file, audio) in takesToWrite)
                {
                    _projects.SaveClip(path, file, audio);
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
                    if (clip.Takes is { } takes)
                    {
                        var names = new string?[takes.Count];
                        for (int k = 0; k < takes.Count; k++)
                        {
                            names[k] = takesToWrite.FirstOrDefault(w => w.Clip == clip && w.Index == k).File
                                ?? (clip.SavedTakeFiles is { } old && k < old.Length ? old[k] : null);
                        }

                        clip.SavedTakeFiles = names;
                        clip.SavedTakes = takes.ToArray();
                        clip.SavedTakesProjectPath = path;
                    }
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

    /// <summary>Projektdaten ohne Spuren (für Speichern und automatische Sicherung).</summary>
    private ProjectFile CreateProjectHeader() => new()
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
        Markers = Markers.Select(m => new ProjectMarker { Name = m.Name, Frame = m.Frame }).ToList(),
        Groups = Groups.Select(g => new ProjectGroup { Name = g.Name, VolumeDb = g.VolumeDb, Mute = g.Mute }).ToList(),
        Buses = [ToProjectBus(HallBus), ToProjectBus(EchoBus)],
    };

    /// <summary>Einstellungen einer Spur ohne Clips.</summary>
    private ProjectTrack CreateProjectTrack(TrackViewModel track) => new()
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
        DuckSource = DuckIndex(track),
        DuckAmount = track.DuckAmount,
        DuckThreshold = track.DuckThreshold,
        ShownAutomation = track.ShownAutomation.ToString(),
        Instrument = track.Instrument?.BuiltIn.ToString(),
        SoundFont = track.Instrument?.SoundFontPath,
        Bank = track.Instrument?.Bank ?? 0,
        Preset = track.Instrument?.Preset ?? 0,
        PresetName = track.Instrument?.PresetName,
        Automation = track.Automation.Select(l => new ProjectAutomation
        {
            Target = l.Target.ToString(),
            Frames = l.Points.Select(p => p.Frame).ToList(),
            Values = l.Points.Select(p => p.Value).ToList(),
        }).ToList(),
    };

    // ───────────── Rückgängig: Zustand sichern und wiederherstellen ─────────────

    private sealed record ClipState(
        AudioBuffer Audio, long Start, string? SavedFile, AudioBuffer? SavedAudio, string? SavedPath,
        IReadOnlyList<Note>? Notes = null, long LengthTicks = 0,
        IReadOnlyList<AudioBuffer>? Takes = null, IReadOnlyList<CompSegment>? Comp = null);

    private sealed record TrackState(
        string Name, string Color, double VolumeDb, double Pan, bool Mute, bool Solo, double SendHall, double SendEcho,
        int GroupIndex, double EqLow, double EqMid, double EqHigh, double HighPassHz, double Compression, ClipState[] Clips,
        IReadOnlyList<AutomationLane> Automation, AutomationTarget ShownAutomation, int DuckSource, double DuckAmount, double DuckThreshold,
        InstrumentSpec? Instrument);

    private sealed record GroupState(string Name, double VolumeDb, bool Mute);

    private sealed record MixerState(TrackState[] Tracks, GroupState[] Groups, int SelectedTrack, int SelectedClip, int NextColor, IReadOnlyList<Marker> Markers);

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
                t.Clips.Select(c => new ClipState(c.Audio, c.StartFrame, c.SavedFileName, c.SavedAudio, c.SavedProjectPath, c.Notes, c.LengthTicks, c.Takes, c.Comp)).ToArray(),
                t.Automation, t.ShownAutomation, DuckIndex(t), t.DuckAmount, t.DuckThreshold, t.Instrument))
                .ToArray(),
            Groups.Select(g => new GroupState(g.Name, g.VolumeDb, g.Mute)).ToArray(),
            selectedTrack,
            selectedClip,
            _nextColor,
            Markers);
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
            var track = CreateTrack(t.Name, t.Color);
            track.Apply(new TrackViewModel(t.Name, t.Color, static () => { })
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
                Automation = t.Automation,
                ShownAutomation = t.ShownAutomation,
                DuckAmount = t.DuckAmount,
                DuckThreshold = t.DuckThreshold,
                Instrument = t.Instrument,
            });
            foreach (var c in t.Clips)
            {
                if (c.Notes is { } notes)
                {
                    var noteClip = new ClipViewModel(track, notes, c.LengthTicks, c.Start, TicksToFrames(c.LengthTicks), SampleRate) { Audio = c.Audio };
                    track.Clips.Add(noteClip);
                    RequestRender(noteClip, immediate: true);
                    continue;
                }

                track.Clips.Add(new ClipViewModel(track, c.Audio, c.Start)
                {
                    SavedFileName = c.SavedFile,
                    SavedAudio = c.SavedAudio,
                    SavedProjectPath = c.SavedPath,
                    Takes = c.Takes,
                    Comp = c.Comp,
                });
            }

            Tracks.Add(track);
        }

        for (int i = 0; i < state.Tracks.Length; i++)
        {
            int source = state.Tracks[i].DuckSource;
            Tracks[i].DuckSource = source >= 0 && source < Tracks.Count ? Tracks[source] : NoTrack;
        }

        _automationTrack = null;
        _nextColor = state.NextColor;
        Markers = state.Markers;
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

    partial void OnSampleRateChanged(int value)
    {
        RenderNoteClips();
        OnPropertyChanged(nameof(MarkerItems));
    }

    partial void OnBpmChanged(double value)
    {
        IsModified = true;
        RenderNoteClips();
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
                    PlayheadFrame = Math.Max(0, provider.MapPlayedToTimeline(_playback.PlayedFrames)); // im Vorzähler nicht negativ
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
        UpdateLoudnessText();
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
        if (IsRecording)
        {
            FinishRecording();
        }

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
            AddNoteTrackCommand, ImportMidiCommand, ExportMidiCommand, ToggleRecordCommand,
            LoopSelectedClipCommand,
            PlayCommand, TogglePlayPauseCommand, ExportMixdownCommand, ExportStemsCommand, NewProjectCommand,
            OpenProjectCommand, SaveProjectCommand, SaveProjectAsCommand,
        ];
        foreach (var command in commands)
        {
            command.NotifyCanExecuteChanged();
        }
    }
}
