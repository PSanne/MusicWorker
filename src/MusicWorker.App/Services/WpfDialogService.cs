using System.Windows;
using Microsoft.Win32;
using MusicWorker.App.Views;

namespace MusicWorker.App.Services;

/// <summary>WPF-Umsetzung der Dialoge.</summary>
public sealed class WpfDialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public string? ShowOpenFileDialog(string filter)
    {
        var dialog = new OpenFileDialog { Title = "Audiodatei öffnen", Filter = filter };
        return Show(dialog) ? dialog.FileName : null;
    }

    public string? ShowSaveFileDialog(string filter, string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportieren",
            Filter = filter,
            FileName = suggestedFileName,
            DefaultExt = ".wav",
            AddExtension = true,
            OverwritePrompt = true,
        };
        return Show(dialog) ? dialog.FileName : null;
    }

    public double? AskValue(ValueRequest request)
    {
        var dialog = new ValueDialog(request) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public (double TempoPercent, double Semitones)? AskTempoAndPitch()
    {
        var dialog = new TempoPitchDialog { Owner = Owner };
        return dialog.ShowDialog() == true ? (dialog.TempoPercent, dialog.Semitones) : null;
    }

    public bool Confirm(string message, string title)
    {
        var result = Owner is { } owner
            ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    public void ShowError(string message)
    {
        if (Owner is { } owner)
        {
            MessageBox.Show(owner, message, "MusicWorker", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show(message, "MusicWorker", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool Show(CommonDialog dialog) =>
        (Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;
}
