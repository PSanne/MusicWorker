using MusicWorker.App.ViewModels;

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

/// <summary>
/// Abstraktion für Dialoge, damit das ViewModel nichts von WPF-Fenstern wissen muss
/// (und sich so später auch automatisiert testen lässt).
/// </summary>
public interface IDialogService
{
    string? ShowOpenFileDialog(string filter);

    string? ShowSaveFileDialog(string filter, string suggestedFileName);

    /// <summary>Fragt einen Zahlenwert ab; null = abgebrochen.</summary>
    double? AskValue(ValueRequest request);

    /// <summary>Fragt Tempo (in %) und Tonhöhe (in Halbtönen) ab; null = abgebrochen.</summary>
    (double TempoPercent, double Semitones)? AskTempoAndPitch();

    /// <summary>Zeigt den Equalizer; true = „Anwenden“ gewählt.</summary>
    bool ShowEqualizer(EqualizerViewModel equalizer);

    bool Confirm(string message, string title);

    void ShowError(string message);
}
