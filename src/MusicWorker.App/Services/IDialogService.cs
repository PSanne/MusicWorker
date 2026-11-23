namespace MusicWorker.App.Services;

/// <summary>
/// Abstraktion für Dialoge, damit das ViewModel nichts von WPF-Fenstern wissen muss
/// (und sich so später auch automatisiert testen lässt).
/// </summary>
public interface IDialogService
{
    string? ShowOpenFileDialog(string filter);

    string? ShowSaveFileDialog(string filter, string suggestedFileName);

    /// <summary>Fragt die gewünschte Lautstärkeänderung in dB ab; null = abgebrochen.</summary>
    double? AskGainDecibels();

    bool Confirm(string message, string title);

    void ShowError(string message);
}
