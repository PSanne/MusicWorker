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
public interface IDialogService
{
    string? ShowOpenFileDialog(string filter);

    string? ShowSaveFileDialog(string filter, string suggestedFileName, string title = "Exportieren");

    /// <summary>Fragt einen Zahlenwert ab; null = abgebrochen.</summary>
    double? AskValue(ValueRequest request);

    /// <summary>Fragt Tempo (in %) und Tonhöhe (in Halbtönen) ab – auf Wunsch pro Kanal; null = abgebrochen.</summary>
    TempoPitchRequest? AskTempoAndPitch(TempoPitchOptions options);

    bool Confirm(string message, string title);

    void ShowError(string message);
}
