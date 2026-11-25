namespace MusicWorker.App.Services;

/// <summary>Beschreibt eine Zahlen-Abfrage mit Schieberegler (z. B. Lautstärke, Dauer).</summary>
public sealed record ValueRequest(
    string Title,
    string Prompt,
    string Hint,
    double Minimum,
    double Maximum,
    double Value,
    double SmallChange,
    string Unit,
    string Format = "0.0");

/// <summary>Vorgaben für den Dialog „Tempo &amp; Tonhöhe“.</summary>
/// <param name="IsStereo">Stereo-Datei → Pro-Kanal-Modus verfügbar.</param>
/// <param name="PerChannel">Dialog im Pro-Kanal-Modus öffnen.</param>
/// <param name="RangeDuration">Länge des Bereichs, der bearbeitet wird (für die Längenvorschau).</param>
public sealed record TempoPitchOptions(bool IsStereo, bool PerChannel, TimeSpan RangeDuration);

/// <summary>Ergebnis des Dialogs „Tempo &amp; Tonhöhe“ (ohne Pro-Kanal gelten die „Links“-Werte für alles).</summary>
public sealed record TempoPitchRequest(
    bool PerChannel, double TempoLeft, double SemitonesLeft, double TempoRight, double SemitonesRight);

/// <summary>
/// Abstraktion für Dialoge, damit das ViewModel nichts von WPF-Fenstern wissen muss
/// (und sich so später auch automatisiert testen lässt).
/// </summary>
/// <summary>Ein Eintrag im Parameter-Dialog.</summary>
public abstract record DialogParameter(string Label, string Hint);

/// <summary>Schieberegler mit Zahlenfeld.</summary>
public sealed record SliderParameter(string Label, double Minimum, double Maximum, double Value, double Step, string Unit, string Hint = "")
    : DialogParameter(Label, Hint);

/// <summary>Auswahlliste; der Wert ist der Index der Auswahl.</summary>
public sealed record ChoiceParameter(string Label, IReadOnlyList<string> Options, int Selected, string Hint = "")
    : DialogParameter(Label, Hint);

/// <summary>Dialog mit mehreren Einstellungen (z. B. Autotune).</summary>
public sealed record ParameterRequest(string Title, string Intro, IReadOnlyList<DialogParameter> Parameters);

/// <summary>Takes eines Clips zum Bearbeiten im Comping-Fenster.</summary>
public sealed record TakesRequest(
    string TrackName,
    IReadOnlyList<MusicWorker.Core.Audio.AudioBuffer> Takes,
    IReadOnlyList<MusicWorker.Core.Recording.CompSegment> Comp,
    int OutputDevice);

/// <summary>Ergebnis des Comping-Fensters.</summary>
/// <param name="Takes">Verbleibende Takes.</param>
/// <param name="Comp">Neue Zusammenstellung.</param>
/// <param name="Flatten">Takes zu einem normalen Clip zusammenfügen.</param>
public sealed record TakesResult(
    IReadOnlyList<MusicWorker.Core.Audio.AudioBuffer> Takes,
    IReadOnlyList<MusicWorker.Core.Recording.CompSegment> Comp,
    bool Flatten);

/// <summary>Vorgaben für das Schlagzeug-Raster.</summary>
/// <param name="Pattern">Muster, mit dem das Raster startet.</param>
/// <param name="Repeats">Wie viele Takte eingefügt werden.</param>
/// <param name="BeatsPerBar">Taktart (Schläge pro Takt).</param>
/// <param name="StartBar">Ab welchem Takt eingefügt wird (nur zur Anzeige).</param>
/// <param name="IsDrumTrack">Hat die Spur ein Schlagzeug-Instrument?</param>
public sealed record DrumGridRequest(MusicWorker.Core.Notes.DrumPattern Pattern, int Repeats, int BeatsPerBar, int StartBar, bool IsDrumTrack);

/// <summary>Ergebnis des Schlagzeug-Rasters.</summary>
public sealed record DrumGridResult(MusicWorker.Core.Notes.DrumPattern Pattern, int Repeats, bool ReplaceExisting);

/// <summary>Eine Stimme für das Notenblatt: Name und Noten (Ticks ab Songanfang).</summary>
public sealed record SheetMusicPart(string Name, IReadOnlyList<MusicWorker.Core.Notes.Note> Notes);

/// <summary>Inhalt für das Notenblatt-Fenster.</summary>
public sealed record SheetMusicRequest(string Title, IReadOnlyList<SheetMusicPart> Parts, int BeatsPerBar, double Bpm, int KeyTonic, bool KeyMinor);

/// <summary>Gewählte Zieldatei und Format aus dem Export-Dialog.</summary>
public sealed record ExportTarget(string Path, MusicWorker.App.Audio.ExportFormat Format);

public interface IDialogService
{
    /// <summary>Speichern-Dialog mit allen Exportformaten; das zuletzt gewählte Format ist vorausgewählt. null = abgebrochen.</summary>
    ExportTarget? ShowExportDialog(string suggestedName, string title, MusicWorker.App.Audio.ExportFormat preferred);

    /// <summary>Ordner auswählen; null = abgebrochen.</summary>
    string? ShowFolderDialog(string title);

    string? ShowOpenFileDialog(string filter);

    string? ShowSaveFileDialog(string filter, string suggestedFileName, string title = "Exportieren");

    /// <summary>Fragt einen Zahlenwert ab; null = abgebrochen.</summary>
    double? AskValue(ValueRequest request);

    /// <summary>Fragt Tempo (in %) und Tonhöhe (in Halbtönen) ab – auf Wunsch pro Kanal; null = abgebrochen.</summary>
    TempoPitchRequest? AskTempoAndPitch(TempoPitchOptions options);

    /// <summary>Mehrere Einstellungen abfragen; liefert je Parameter den Wert (bei Auswahllisten den Index) oder null.</summary>
    double[]? AskParameters(ParameterRequest request);

    /// <summary>Takes &amp; Comping bearbeiten; null = abgebrochen.</summary>
    TakesResult? EditTakes(TakesRequest request);

    /// <summary>Notenblatt anzeigen (mit Drucken und PDF-Export).</summary>
    void ShowSheetMusic(SheetMusicRequest request);

    /// <summary>Schlagzeug-Raster bearbeiten; null = abgebrochen.</summary>
    DrumGridResult? EditDrumPattern(DrumGridRequest request);

    bool Confirm(string message, string title);

    /// <summary>Fragt einen kurzen Text ab (z. B. Markername); null = abgebrochen.</summary>
    string? AskText(string title, string prompt, string initialText);

    /// <summary>Hinweis mit nur einem „OK“-Knopf.</summary>
    void ShowInfo(string message, string title);

    void ShowError(string message);
}
