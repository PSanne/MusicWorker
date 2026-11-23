using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Mixing;

namespace MusicWorker.App.ViewModels;

/// <summary>Ein Clip auf einer Spur: Audio plus Startposition auf der Zeitleiste.</summary>
public sealed partial class ClipViewModel : ObservableObject
{
    [ObservableProperty]
    private AudioBuffer _audio;

    [ObservableProperty]
    private long _startFrame;

    public ClipViewModel(TrackViewModel track, AudioBuffer audio, long startFrame)
    {
        Track = track;
        _audio = audio;
        _startFrame = startFrame;
    }

    public TrackViewModel Track { get; set; }

    public long EndFrame => StartFrame + Audio.FrameCount;

    // Für schnelles Speichern: Welche Datei gehört zu diesem (unveränderten) Audio?
    internal string? SavedFileName { get; set; }

    internal AudioBuffer? SavedAudio { get; set; }

    internal string? SavedProjectPath { get; set; }
}

/// <summary>Verschiebung eines Clips auf der Zeitleiste.</summary>
public sealed record ClipMove(ClipViewModel Clip, long NewStart);

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

    public TrackViewModel(string name, string color, Action changed)
    {
        _name = name;
        Color = color;
        _changed = changed;
    }

    /// <summary>Farbe der Spur (Clips und Kanalzug), z. B. „#4FC3F7“.</summary>
    public string Color { get; }

    public ObservableCollection<ClipViewModel> Clips { get; } = [];

    public string VolumeText => FormatDb(VolumeDb);

    public string PanText => Math.Abs(Pan) < 0.5
        ? "Mitte"
        : Pan < 0
            ? string.Format(CultureInfo.CurrentCulture, "L {0:0}", -Pan)
            : string.Format(CultureInfo.CurrentCulture, "R {0:0}", Pan);

    public MixTrack ToMixTrack(int groupIndex) => new(
        Clips.Select(c => new MixClip(c.Audio, c.StartFrame)).ToArray(),
        VolumeDb,
        Pan / 100,
        Mute,
        Solo,
        [SendHall / 100, SendEcho / 100],
        groupIndex);

    internal static string FormatDb(double db) => db <= MixMath.SilenceDb
        ? "-∞ dB"
        : string.Format(CultureInfo.CurrentCulture, "{0:+0.0;-0.0;0.0} dB", db);

    partial void OnNameChanged(string value) => _changed();

    partial void OnVolumeDbChanged(double value) => _changed();

    partial void OnPanChanged(double value) => _changed();

    partial void OnMuteChanged(bool value) => _changed();

    partial void OnSoloChanged(bool value) => _changed();

    partial void OnSendHallChanged(double value) => _changed();

    partial void OnSendEchoChanged(double value) => _changed();

    partial void OnGroupChanged(GroupViewModel? value) => _changed();
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
