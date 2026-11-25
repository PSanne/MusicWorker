using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Mixing;
using MusicWorker.Core.Notes;
using MusicWorker.Core.Recording;
using MusicWorker.Core.Synthesis;

namespace MusicWorker.App.ViewModels;

/// <summary>
/// Ein Clip auf einer Spur: Audio plus Startposition auf der Zeitleiste. Ein Notenclip enthält Noten;
/// sein <see cref="Audio"/> ist dann die (im Hintergrund) berechnete Wiedergabe mit dem Instrument der Spur.
/// </summary>
public sealed partial class ClipViewModel : ObservableObject
{
    [ObservableProperty]
    private AudioBuffer _audio;

    [ObservableProperty]
    private long _startFrame;

    /// <summary>Noten (Ticks relativ zum Clip-Anfang) – nur bei Notenclips, sonst null.</summary>
    [ObservableProperty]
    private IReadOnlyList<Note>? _notes;

    /// <summary>Länge eines Notenclips in Ticks.</summary>
    [ObservableProperty]
    private long _lengthTicks;

    /// <summary>Takes (mehrere Aufnahmen derselben Stelle, alle gleich lang) – null bei normalen Clips.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTakes))]
    private IReadOnlyList<AudioBuffer>? _takes;

    /// <summary>Zusammenstellung: welcher Take wo klingt.</summary>
    [ObservableProperty]
    private IReadOnlyList<CompSegment>? _comp;

    /// <summary>Einblenden am Clip-Anfang (Frames) – mit dem Griff oben links ziehbar.</summary>
    [ObservableProperty]
    private long _fadeInFrames;

    /// <summary>Ausblenden am Clip-Ende (Frames) – mit dem Griff oben rechts ziehbar.</summary>
    [ObservableProperty]
    private long _fadeOutFrames;

    public ClipViewModel(TrackViewModel track, AudioBuffer audio, long startFrame)
    {
        Track = track;
        _audio = audio;
        _startFrame = startFrame;
    }

    /// <summary>Erzeugt einen Notenclip (das Audio wird später berechnet).</summary>
    public ClipViewModel(TrackViewModel track, IReadOnlyList<Note> notes, long lengthTicks, long startFrame, long lengthFrames, int sampleRate)
        : this(track, new AudioBuffer(new float[2], sampleRate, 2), startFrame)
    {
        _notes = notes;
        _lengthTicks = lengthTicks;
        NoteLengthFrames = lengthFrames;
    }

    public TrackViewModel Track { get; set; }

    public bool IsNoteClip => Notes is not null;

    /// <summary>Clip mit mindestens zwei Takes (Comping möglich).</summary>
    public bool HasTakes => Takes is { Count: > 1 };

    // Für schnelles Speichern der Takes
    internal string?[]? SavedTakeFiles { get; set; }

    internal AudioBuffer?[]? SavedTakes { get; set; }

    internal string? SavedTakesProjectPath { get; set; }

    /// <summary>Länge auf der Zeitleiste: bei Notenclips die musikalische Länge (ohne Ausklang).</summary>
    public long LengthFrames => IsNoteClip ? NoteLengthFrames : Audio.FrameCount;

    public long EndFrame => StartFrame + LengthFrames;

    /// <summary>Länge eines Notenclips in Frames (hängt vom Tempo ab; vom Mischpult gesetzt).</summary>
    internal long NoteLengthFrames { get; set; }

    /// <summary>Zähler der Berechnungsaufträge (verhindert, dass ein veraltetes Ergebnis gewinnt).</summary>
    internal int RenderVersion { get; set; }

    /// <summary>Zu welchem Auftrag das aktuelle Audio gehört.</summary>
    internal int RenderedVersion { get; set; } = -1;

    /// <summary>Wo der Clip lag, als sein Klang berechnet wurde (wichtig bei Tempowechseln).</summary>
    internal long RenderedStartFrame { get; set; } = -1;

    internal bool NeedsRender => IsNoteClip && RenderedVersion != RenderVersion;

    // Für schnelles Speichern: Welche Datei gehört zu diesem (unveränderten) Audio?
    internal string? SavedFileName { get; set; }

    internal AudioBuffer? SavedAudio { get; set; }

    internal string? SavedProjectPath { get; set; }
}

/// <summary>Verschiebung eines Clips auf der Zeitleiste (optional auf eine andere Spur).</summary>
/// <param name="Clip">Der verschobene Clip.</param>
/// <param name="NewStart">Neue Startposition in Frames.</param>
/// <param name="TargetTrack">Zielspur (null = bleibt auf seiner Spur).</param>
/// <param name="DragStarted">True beim ersten Schritt eines Ziehvorgangs (für „Rückgängig“).</param>
public sealed record ClipMove(ClipViewModel Clip, long NewStart, TrackViewModel? TargetTrack = null, bool DragStarted = false);

/// <summary>Mehrere gewählte Clips gemeinsam verschieben: <paramref name="Anchor"/> landet bei <paramref name="NewStart"/>, die anderen behalten ihren Abstand.</summary>
public sealed record ClipsMove(ClipViewModel Anchor, long NewStart, bool DragStarted = false);

/// <summary>Ein- oder Ausblendung eines Clips mit dem Griff ändern.</summary>
public sealed record ClipFade(ClipViewModel Clip, bool FadeIn, long Frames, bool DragStarted = false);

/// <summary>Spur per Ziehen an eine neue Stelle setzen.</summary>
public sealed record TrackMove(TrackViewModel Track, int NewIndex);

/// <summary>Eintrag im Farbmenü einer Spur.</summary>
public sealed record TrackColorChoice(string Name, string Color);

/// <summary>Eine Stelle auf einer Spur (z. B. Doppelklick in eine freie Fläche).</summary>
public sealed record LanePosition(TrackViewModel Track, long Frame);

/// <summary>Ein Loop-Bereich auf der Zeitleiste.</summary>
public sealed record LoopRange(long Start, long End);

/// <summary>Art einer Hüllkurven-Bearbeitung.</summary>
public enum AutomationEditKind
{
    Add,
    Move,
    Remove,
}

/// <summary>Bearbeitung eines Hüllkurven-Punkts (von der Spuransicht an das Mischpult).</summary>
public sealed class AutomationEdit(TrackViewModel track, AutomationEditKind kind, int index, long frame, double value, bool dragStarted = false)
{
    public TrackViewModel Track { get; } = track;

    public AutomationEditKind Kind { get; } = kind;

    public int Index { get; } = index;

    public long Frame { get; } = frame;

    public double Value { get; } = value;

    /// <summary>True beim ersten Schritt eines Ziehvorgangs (für „Rückgängig“).</summary>
    public bool DragStarted { get; } = dragStarted;

    /// <summary>Vom Mischpult gesetzt: Index des neuen bzw. verschobenen Punkts.</summary>
    public int ResultIndex { get; set; } = -1;
}

/// <summary>Eintrag der Auswahlliste „welche Hüllkurve wird gezeigt“.</summary>
public sealed record AutomationChoice(AutomationTarget Target, string Name)
{
    public static IReadOnlyList<AutomationChoice> All { get; } =
        Automation.Targets.Select(t => new AutomationChoice(t, Automation.RangeOf(t).Name)).ToArray();
}

/// <summary>Eine Spur mit Kanalzug: Fader, Panorama, Mute/Solo, Sends und Gruppe.</summary>
public sealed partial class TrackViewModel : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText))]
    private double _volumeDb;

    /// <summary>Panorama von -100 (links) bis +100 (rechts).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PanText))]
    private double _pan;

    [ObservableProperty]
    private bool _mute;

    [ObservableProperty]
    private bool _solo;

    /// <summary>Send auf den Hall-Bus in % (nach dem Fader).</summary>
    [ObservableProperty]
    private double _sendHall;

    /// <summary>Send auf den Echo-Bus in % (nach dem Fader).</summary>
    [ObservableProperty]
    private double _sendEcho;

    [ObservableProperty]
    private GroupViewModel? _group;

    /// <summary>Pegelanzeige 0 … 1 (-60 … 0 dBFS).</summary>
    [ObservableProperty]
    private double _meter;

    // Insert-Effekte im Kanalzug

    /// <summary>Tiefen ±12 dB (Kuhschwanz bei 120 Hz).</summary>
    [ObservableProperty]
    private double _eqLow;

    /// <summary>Mitten ±12 dB (Glocke bei 1 kHz).</summary>
    [ObservableProperty]
    private double _eqMid;

    /// <summary>Höhen ±12 dB (Kuhschwanz bei 8 kHz).</summary>
    [ObservableProperty]
    private double _eqHigh;

    /// <summary>Hochpass 20 … 500 Hz (20 = aus).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HighPassText))]
    private double _highPassHz = 20;

    /// <summary>Kompression 0 … 100 %.</summary>
    [ObservableProperty]
    private double _compression;

    /// <summary>Für die Aufnahme scharfgeschaltet.</summary>
    [ObservableProperty]
    private bool _isArmed;

    /// <summary>Farbe der Spur (Clips und Kanalzug), z. B. „#4FC3F7“.</summary>
    [ObservableProperty]
    private string _color;

    /// <summary>Höhe der Spur in der Spuransicht (Pixel).</summary>
    [ObservableProperty]
    private double _height = DefaultHeight;

    /// <summary>Instrument einer Notenspur (null = Audiospur).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoteTrack), nameof(InstrumentName))]
    private InstrumentSpec? _instrument;

    /// <summary>Hüllkurven der Spur (nur nicht leere; unveränderlich, wird bei jeder Bearbeitung ersetzt).</summary>
    [ObservableProperty]
    private IReadOnlyList<AutomationLane> _automation = [];

    /// <summary>Welche Hüllkurve in der Spuransicht gezeigt und bearbeitet wird.</summary>
    [ObservableProperty]
    private AutomationTarget _shownAutomation = AutomationTarget.Volume;

    /// <summary>Auto-Absenkung: Diese Spur wird leiser, wenn die gewählte Spur spielt.</summary>
    [ObservableProperty]
    private TrackViewModel? _duckSource;

    /// <summary>Stärke der Absenkung in dB.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DuckAmountText))]
    private double _duckAmount = 12;

    /// <summary>Ab diesem Pegel der Quellspur wird abgesenkt (dBFS).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DuckThresholdText))]
    private double _duckThreshold = -40;

    public const double DefaultHeight = 76;
    public const double MinHeight = 44;
    public const double MaxHeight = 220;

    public TrackViewModel(string name, string color, Action changed)
    {
        _name = name;
        _color = color;
        _changed = changed;
    }

    public ObservableCollection<ClipViewModel> Clips { get; } = [];

    public string VolumeText => FormatDb(VolumeDb);

    public string PanText => Math.Abs(Pan) < 0.5
        ? "Mitte"
        : Pan < 0
            ? string.Format(CultureInfo.CurrentCulture, "L {0:0}", -Pan)
            : string.Format(CultureInfo.CurrentCulture, "R {0:0}", Pan);

    public string HighPassText => HighPassHz <= 20.5 ? "aus" : string.Format(CultureInfo.CurrentCulture, "{0:0} Hz", HighPassHz);

    public TrackInserts Inserts => new(EqLow, EqMid, EqHigh, HighPassHz, Compression);

    public IReadOnlyList<AutomationChoice> AutomationChoices => AutomationChoice.All;

    public bool IsNoteTrack => Instrument is not null;

    public string InstrumentName => Instrument?.DisplayName ?? string.Empty;

    /// <summary>Wird aufgerufen, wenn das Instrument wechselt (Notenclips neu berechnen).</summary>
    internal Action<TrackViewModel>? InstrumentChanged { get; set; }

    /// <summary>Wird aufgerufen, wenn die Spur scharf- bzw. ausgeschaltet wird.</summary>
    internal Action<TrackViewModel>? ArmedChanged { get; set; }

    public string DuckAmountText => string.Format(CultureInfo.CurrentCulture, "-{0:0} dB", DuckAmount);

    public string DuckThresholdText => string.Format(CultureInfo.CurrentCulture, "{0:0} dB", DuckThreshold);

    /// <summary>Wird aufgerufen, wenn sich nur die Darstellung ändert (z. B. andere Hüllkurve gezeigt).</summary>
    internal Action? Redraw { get; set; }

    /// <summary>Übernimmt alle Einstellungen einer Vorlage (außer Name, Farbe, Clips und Absenk-Quelle).</summary>
    internal void Apply(TrackViewModel template)
    {
        VolumeDb = template.VolumeDb;
        Pan = template.Pan;
        Mute = template.Mute;
        Solo = template.Solo;
        SendHall = template.SendHall;
        SendEcho = template.SendEcho;
        Group = template.Group ?? Group;
        EqLow = template.EqLow;
        EqMid = template.EqMid;
        EqHigh = template.EqHigh;
        HighPassHz = template.HighPassHz;
        Compression = template.Compression;
        Automation = template.Automation;
        ShownAutomation = template.ShownAutomation;
        DuckAmount = template.DuckAmount;
        DuckThreshold = template.DuckThreshold;
        Instrument = template.Instrument;
    }

    /// <summary>Die Hüllkurve für ein Ziel (leer, wenn es keine gibt).</summary>
    public AutomationLane LaneFor(AutomationTarget target) =>
        Automation.FirstOrDefault(l => l.Target == target) ?? new AutomationLane(target, []);

    /// <summary>Ersetzt eine Hüllkurve (leere werden entfernt).</summary>
    public void SetLane(AutomationLane lane) =>
        Automation = Automation.Where(l => l.Target != lane.Target).Append(lane).Where(l => !l.IsEmpty).ToArray();

    /// <summary>Wert des Reglers, der ohne Hüllkurve gilt (in der Einheit der Hüllkurve).</summary>
    public double KnobValue(AutomationTarget target) => target switch
    {
        AutomationTarget.Volume => 0, // Lautstärke-Hüllkurve wirkt zusätzlich zum Fader
        AutomationTarget.Pan => Pan / 100,
        AutomationTarget.SendHall => SendHall / 100,
        AutomationTarget.SendEcho => SendEcho / 100,
        _ => HighPassHz,
    };

    /// <summary>Für die Misch-Engine – überlappende Clips bekommen automatisch eine Überblendung.</summary>
    /// <param name="groupIndex">Index der Gruppe (-1 = keine).</param>
    /// <param name="duckSourceIndex">Index der Spur, die diese Spur absenkt (-1 = keine).</param>
    public MixTrack ToMixTrack(int groupIndex, int duckSourceIndex = -1) => new(
        ClipTools.WithCrossfades(Clips.Where(c => !c.IsNoteClip).Select(c => new MixClip(c.Audio, c.StartFrame, c.FadeInFrames, c.FadeOutFrames)))
            .Concat(Clips.Where(c => c.IsNoteClip).Select(c => new MixClip(c.Audio, c.StartFrame))) // Notenclips klingen ohne Überblendung aus
            .ToArray(),
        VolumeDb,
        Pan / 100,
        Mute,
        Solo,
        [SendHall / 100, SendEcho / 100],
        groupIndex,
        Inserts.IsNeutral ? null : Inserts,
        Automation.Count > 0 ? Automation : null,
        duckSourceIndex >= 0 ? new DuckingSettings(duckSourceIndex, DuckAmount, DuckThreshold) : null);

    internal static string FormatDb(double db) => db <= MixMath.SilenceDb
        ? "-∞ dB"
        : string.Format(CultureInfo.CurrentCulture, "{0:+0.0;-0.0;0.0} dB", db);

    partial void OnNameChanged(string value) => _changed();

    partial void OnColorChanged(string value)
    {
        _changed();
        Redraw?.Invoke();
    }

    partial void OnHeightChanged(double value) => Redraw?.Invoke();

    /// <summary>Auswahl für das Menü „Farbe“ der Spur.</summary>
    public static IReadOnlyList<TrackColorChoice> ColorChoices { get; } =
    [
        new("Hellblau", "#4FC3F7"), new("Blau", "#5C6BC0"), new("Türkis", "#4DB6AC"), new("Grün", "#81C784"),
        new("Gelbgrün", "#D4E157"), new("Gelb", "#FFD54F"), new("Orange", "#FFB74D"), new("Rot", "#E57373"),
        new("Pink", "#F06292"), new("Lila", "#BA68C8"), new("Braun", "#A1887F"), new("Grau", "#90A4AE"),
    ];

    /// <summary>Vom Mischpult gesetzt: Spur um <c>delta</c> Plätze verschieben.</summary>
    internal Action<TrackViewModel, int>? MoveRequested { get; set; }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SetColor(string? color)
    {
        if (!string.IsNullOrEmpty(color))
        {
            Color = color;
        }
    }

    /// <summary>Höhe aus dem Kontextmenü (Parameter als Text, z. B. „120“).</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SetHeight(string? height)
    {
        if (double.TryParse(height, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            Height = Math.Clamp(value, MinHeight, MaxHeight);
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void MoveUp() => MoveRequested?.Invoke(this, -1);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void MoveDown() => MoveRequested?.Invoke(this, 1);

    partial void OnVolumeDbChanged(double value) => _changed();

    partial void OnPanChanged(double value) => _changed();

    partial void OnMuteChanged(bool value) => _changed();

    partial void OnSoloChanged(bool value) => _changed();

    partial void OnSendHallChanged(double value) => _changed();

    partial void OnSendEchoChanged(double value) => _changed();

    partial void OnGroupChanged(GroupViewModel? value) => _changed();

    partial void OnEqLowChanged(double value) => _changed();

    partial void OnEqMidChanged(double value) => _changed();

    partial void OnEqHighChanged(double value) => _changed();

    partial void OnHighPassHzChanged(double value) => _changed();

    partial void OnCompressionChanged(double value) => _changed();

    partial void OnAutomationChanged(IReadOnlyList<AutomationLane> value) => _changed();

    partial void OnInstrumentChanged(InstrumentSpec? value) => InstrumentChanged?.Invoke(this);

    partial void OnIsArmedChanged(bool value) => ArmedChanged?.Invoke(this);

    partial void OnShownAutomationChanged(AutomationTarget value) => Redraw?.Invoke();

    partial void OnDuckSourceChanged(TrackViewModel? value) => _changed();

    partial void OnDuckAmountChanged(double value) => _changed();

    partial void OnDuckThresholdChanged(double value) => _changed();
}

/// <summary>Eine Gruppe: Fader und Mute wirken auf alle zugeordneten Spuren.</summary>
public sealed partial class GroupViewModel : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText))]
    private double _volumeDb;

    [ObservableProperty]
    private bool _mute;

    public GroupViewModel(string name, Action changed, bool isNone = false)
    {
        _name = name;
        _changed = changed;
        IsNone = isNone;
    }

    /// <summary>Platzhalter „Keine Gruppe“ für die Auswahlliste.</summary>
    public bool IsNone { get; }

    public string VolumeText => TrackViewModel.FormatDb(VolumeDb);

    public MixGroup ToMixGroup() => new(VolumeDb, Mute);

    public override string ToString() => Name;

    partial void OnNameChanged(string value) => _changed();

    partial void OnVolumeDbChanged(double value) => _changed();

    partial void OnMuteChanged(bool value) => _changed();
}

/// <summary>Ein Effekt-Bus (Hall oder Echo) mit Rückweg-Fader und Effekt-Einstellungen.</summary>
public sealed partial class BusViewModel : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeText))]
    private double _returnDb;

    [ObservableProperty]
    private double _roomSize = 60;

    [ObservableProperty]
    private double _damping = 40;

    [ObservableProperty]
    private double _timeLeft = 375;

    [ObservableProperty]
    private double _timeRight = 500;

    [ObservableProperty]
    private double _feedback = 35;

    [ObservableProperty]
    private bool _pingPong = true;

    public BusViewModel(string name, BusEffect effect, Action changed)
    {
        Name = name;
        Effect = effect;
        _changed = changed;
    }

    public string Name { get; }

    public BusEffect Effect { get; }

    public bool IsReverb => Effect == BusEffect.Reverb;

    public bool IsDelay => Effect == BusEffect.Delay;

    public string VolumeText => TrackViewModel.FormatDb(ReturnDb);

    public ReverbSettings ReverbSettings => new([100], RoomSize, Damping);

    public DelaySettings DelaySettings => new([TimeLeft, TimeRight], [100], Feedback, Damping, PingPong);

    /// <summary>Wie lange der Effekt nachklingt (für das Ausklingen beim Mixdown).</summary>
    public double TailSeconds => IsReverb ? ReverbSettings.TailSeconds : DelaySettings.TailSeconds;

    public MixBus ToMixBus() => IsReverb
        ? new MixBus(BusEffect.Reverb, ReturnDb, Reverb: ReverbSettings)
        : new MixBus(BusEffect.Delay, ReturnDb, Delay: DelaySettings);

    partial void OnReturnDbChanged(double value) => _changed();

    partial void OnRoomSizeChanged(double value) => _changed();

    partial void OnDampingChanged(double value) => _changed();

    partial void OnTimeLeftChanged(double value) => _changed();

    partial void OnTimeRightChanged(double value) => _changed();

    partial void OnFeedbackChanged(double value) => _changed();

    partial void OnPingPongChanged(bool value) => _changed();
}
