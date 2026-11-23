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

    public string? ShowSaveFileDialog(string filter, string suggestedFileName, string title = "Exportieren")
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = suggestedFileName,
            DefaultExt = FirstExtension(filter),
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

    public TempoPitchRequest? AskTempoAndPitch(TempoPitchOptions options)
    {
        var dialog = new TempoPitchDialog(options) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public double[]? AskParameters(ParameterRequest request)
    {
        var dialog = new ParametersDialog(request) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Values : null;
    }

    public TakesResult? EditTakes(TakesRequest request)
    {
        var dialog = new TakesDialog(request) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
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

    /// <summary>Erste Endung aus einem Filter wie „WAV|*.wav|MP3|*.mp3“ → „.wav“.</summary>
    private static string FirstExtension(string filter)
    {
        var parts = filter.Split('|');
        string pattern = parts.Length > 1 ? parts[1].Split(';')[0] : "*.wav";
        return pattern.StartsWith("*.", StringComparison.Ordinal) ? pattern[1..] : ".wav";
    }

    private static bool Show(CommonDialog dialog) =>
        (Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;
}
