using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Services;
using MusicWorker.Core.Analysis;
using MusicWorker.Core.Notes;

namespace MusicWorker.App.ViewModels;

/// <summary>Eintrag im Menü „Tempowechsel“.</summary>
public sealed record TempoChangeMenuItem(string Header, int Index);

/// <summary>Tempospur: Tempowechsel im Song und Tempo-Erkennung.</summary>
public partial class MixerViewModel
{
    /// <summary>Tempowechsel nach dem Anfang (das Tempo am Anfang ist <see cref="Bpm"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTempoChanges), nameof(TempoChangeItems), nameof(Tempo), nameof(TempoSummary))]
    private IReadOnlyList<TempoChange> _tempoChanges = [];

    /// <summary>Erkannte bzw. gewählte Tonart des Projekts (Vorschlag für Akkordfolgen) – null = unbekannt.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectKeyText))]
    private KeyResult? _projectKey;

    private TempoMap? _tempoMap;
    private bool _restoringTempo;

    /// <summary>Der ganze Tempoverlauf (Grundtempo und Tempowechsel).</summary>
    public TempoMap Tempo => _tempoMap ??= new TempoMap(Bpm, TempoChanges);

    public bool HasTempoChanges => TempoChanges.Count > 0;

    public IReadOnlyList<TempoChangeMenuItem> TempoChangeItems => TempoChanges
        .Select((c, i) => new TempoChangeMenuItem(
            string.Format(CultureInfo.CurrentCulture, "Takt {0}: {1:0.#} BPM", BarNumber(c.Tick), c.Bpm), i))
        .ToArray();

    /// <summary>Kurzer Text für die Werkzeugleiste, z. B. „+2 Wechsel“.</summary>
    public string TempoSummary => TempoChanges.Count switch
    {
        0 => "Tempowechsel",
        1 => "1 Tempowechsel",
        _ => $"{TempoChanges.Count} Tempowechsel",
    };

    public string ProjectKeyText => ProjectKey is { } key ? $"Tonart: {key.Name}" : "Tonart: –";

    /// <summary>Nächster Schlag (Viertel) zu <paramref name="frame"/> – berücksichtigt Tempowechsel.</summary>
    public long SnapToBeat(long frame) => Tempo.NearestBeatFrame(frame, SampleRate);

    /// <summary>Ticks relativ zum Anfang eines Clips in Frames relativ zum Clip-Anfang.</summary>
    public long ClipTicksToFrames(long clipStartFrame, long ticks) => Tempo.ClipTicksToFrames(clipStartFrame, ticks, SampleRate);

    /// <summary>Frames relativ zum Anfang eines Clips in Ticks relativ zum Clip-Anfang.</summary>
    public long ClipFramesToTicks(long clipStartFrame, long frames) => Tempo.ClipFramesToTicks(clipStartFrame, frames, SampleRate);

    private long BarNumber(long tick) => (tick / MusicalTime.TicksPerBar(BeatsPerBar)) + 1;

    /// <summary>Tempowechsel am Anfang des Takts, in dem der Abspielkopf steht.</summary>
    [RelayCommand]
    private void AddTempoChange()
    {
        long barTicks = MusicalTime.TicksPerBar(BeatsPerBar);
        long tick = FramesToTicks(Math.Max(0, PlayheadFrame)) / barTicks * barTicks;
        if (tick <= 0)
        {
            tick = barTicks; // am Anfang gilt das Grundtempo – der erste Wechsel frühestens ab Takt 2
        }

        var request = new ValueRequest(
            "Tempowechsel",
            $"Neues Tempo ab Takt {BarNumber(tick)}:",
            "Ab diesem Takt läuft das Metronom, das Raster und alle Notenclips im neuen Tempo. Aufnahmen (Audio) bleiben unverändert.",
            20, 300, Math.Round(Tempo.BpmAt(tick)), 1, "BPM", "0.#");
        if (_dialogs.AskValue(request) is not { } bpm)
        {
            return;
        }

        var changes = TempoChanges.Where(c => c.Tick != tick).Append(new TempoChange(tick, bpm)).OrderBy(c => c.Tick).ToArray();
        ChangeTempo(changes);
        StatusText = string.Format(CultureInfo.CurrentCulture, "Tempowechsel ab Takt {0}: {1:0.#} BPM", BarNumber(tick), bpm);
    }

    [RelayCommand]
    private void EditTempoChange(int index)
    {
        if (index < 0 || index >= TempoChanges.Count)
        {
            return;
        }

        var change = TempoChanges[index];
        long barTicks = MusicalTime.TicksPerBar(BeatsPerBar);
        var parameters = _dialogs.AskParameters(new ParameterRequest(
            "Tempowechsel bearbeiten",
            "Ab welchem Takt soll welches Tempo gelten?",
            [
                new SliderParameter("Ab Takt", 2, 999, BarNumber(change.Tick), 1, string.Empty),
                new SliderParameter("Tempo", 20, 300, change.Bpm, 1, "BPM"),
                new ChoiceParameter("Aktion", ["Übernehmen", "Tempowechsel löschen"], 0),
            ]));
        if (parameters is null)
        {
            return;
        }

        var others = TempoChanges.Where((_, i) => i != index);
        if ((int)parameters[2] == 1)
        {
            ChangeTempo(others.ToArray());
            StatusText = "Tempowechsel gelöscht.";
            return;
        }

        long tick = ((long)Math.Round(parameters[0]) - 1) * barTicks;
        ChangeTempo(others.Where(c => c.Tick != tick).Append(new TempoChange(tick, parameters[1])).OrderBy(c => c.Tick).ToArray());
    }

    [RelayCommand]
    private void RemoveTempoChanges()
    {
        if (TempoChanges.Count > 0)
        {
            ChangeTempo([]);
            StatusText = "Alle Tempowechsel entfernt – der Song läuft wieder durchgehend im Grundtempo.";
        }
    }

    /// <summary>
    /// Erkennt das Tempo des gewählten Audioclips; auf Wunsch wird es als Projekttempo übernommen und der Clip
    /// so verschoben, dass sein erster Schlag genau auf einem Takt liegt.
    /// </summary>
    [RelayCommand]
    private async Task DetectClipTempo()
    {
        if (SelectedClip is not { IsNoteClip: false } clip)
        {
            StatusText = "Bitte zuerst einen Audioclip anklicken, dessen Tempo erkannt werden soll.";
            return;
        }

        StatusText = "Tempo wird erkannt …";
        var audio = clip.Audio;
        var result = await Task.Run(() => MusicAnalysis.DetectTempo(audio));
        if (result is null)
        {
            StatusText = "In diesem Clip ist kein gleichmäßiger Rhythmus zu erkennen.";
            return;
        }

        string sure = result.Confidence >= 0.5 ? "deutlich" : result.Confidence >= 0.2 ? "ziemlich sicher" : "unsicher";
        var choice = _dialogs.AskParameters(new ParameterRequest(
            "Tempo erkannt",
            string.Format(CultureInfo.CurrentCulture,
                "Der Clip „{0}“ hat etwa {1:0.#} BPM ({2}). Manchmal hört man auch das doppelte oder halbe Tempo – wähle, was zu deinem Gefühl passt.",
                clip.Track.Name, result.Bpm, sure),
            [
                new ChoiceParameter("Tempo", [
                    string.Format(CultureInfo.CurrentCulture, "{0:0.#} BPM", result.Bpm),
                    string.Format(CultureInfo.CurrentCulture, "{0:0.#} BPM (halb)", result.Bpm / 2),
                    string.Format(CultureInfo.CurrentCulture, "{0:0.#} BPM (doppelt)", result.Bpm * 2),
                ], 0),
                new ChoiceParameter("Übernehmen", [
                    "Als Projekttempo übernehmen und Clip auf den Takt legen",
                    "Nur als Projekttempo übernehmen",
                ], 0, "Beim Ausrichten wird der Clip so verschoben, dass sein erster Schlag auf einem Taktanfang liegt."),
            ]));
        if (choice is null)
        {
            StatusText = string.Format(CultureInfo.CurrentCulture, "Erkanntes Tempo: {0:0.#} BPM.", result.Bpm);
            return;
        }

        double bpm = Math.Clamp(result.Bpm * ((int)choice[0] switch { 1 => 0.5, 2 => 2.0, _ => 1.0 }), 20, 300);
        RecordUndo();
        TempoChanges = [];
        Bpm = Math.Round(bpm, 2);
        if ((int)choice[1] == 0)
        {
            long firstBeat = clip.StartFrame + (long)Math.Round(result.FirstBeatSeconds * clip.Audio.SampleRate);
            long bar = (long)Math.Round(MusicalTime.TicksPerBar(BeatsPerBar) * 60.0 / Bpm / MusicalTime.TicksPerQuarter * SampleRate);
            long target = Math.Max(0, (long)Math.Round(firstBeat / (double)bar) * bar);
            long delta = target - firstBeat;
            if (clip.StartFrame + delta < 0)
            {
                delta += bar;
            }

            clip.StartFrame += delta;
        }

        IsModified = true;
        OnStructureChanged();
        StatusText = string.Format(CultureInfo.CurrentCulture, "Projekttempo: {0:0.#} BPM{1}", Bpm,
            (int)choice[1] == 0 ? " – der Clip liegt jetzt auf dem Takt-Raster." : ".");
    }

    /// <summary>
    /// Bei Tempowechseln hängt der Klang eines Notenclips davon ab, wo er liegt: verschobene Clips neu berechnen.
    /// </summary>
    private void RefreshMovedNoteClips()
    {
        if (Tempo.IsConstant)
        {
            return;
        }

        foreach (var clip in Tracks.SelectMany(t => t.Clips).Where(c => c.IsNoteClip && c.RenderedStartFrame != c.StartFrame && !c.NeedsRender))
        {
            clip.NoteLengthFrames = ClipTicksToFrames(clip.StartFrame, clip.LengthTicks);
            clip.RenderedStartFrame = clip.StartFrame; // nur einmal anstoßen
            RequestRender(clip, immediate: true);
        }
    }

    /// <summary>Erkennt die Tonart des gewählten Clips (Audio oder Noten) und merkt sie sich als Tonart des Projekts.</summary>
    [RelayCommand]
    private async Task DetectClipKey()
    {
        if (SelectedClip is not { } clip)
        {
            StatusText = "Bitte zuerst einen Clip anklicken, dessen Tonart erkannt werden soll.";
            return;
        }

        if (clip.IsNoteClip && clip.NeedsRender)
        {
            await EnsureNotesRenderedAsync();
        }

        StatusText = "Tonart wird erkannt …";
        var audio = clip.Audio;
        var key = await Task.Run(() => MusicAnalysis.DetectKey(audio));
        if (key is null)
        {
            StatusText = "In diesem Clip ist keine Tonart zu erkennen (zu wenig Töne).";
            return;
        }

        ProjectKey = key;
        IsModified = true;
        StatusText = $"Erkannte Tonart: {key.Name} – wird im Noten-Editor für Akkordfolgen vorgeschlagen.";
    }

    private void ChangeTempo(IReadOnlyList<TempoChange> changes)
    {
        RecordUndo();
        TempoChanges = changes;
    }

    partial void OnTempoChangesChanged(IReadOnlyList<TempoChange> value)
    {
        _tempoMap = null;
        IsModified = true;
        if (!_restoringTempo)
        {
            RenderNoteClips();
        }
    }
}
