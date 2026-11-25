using System.IO;
using System.Windows;
using Microsoft.Win32;
using MusicWorker.App.Audio;
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

    public ExportTarget? ShowExportDialog(string suggestedName, string title, ExportFormat preferred)
    {
        var formats = AudioFileService.ExportFormats;
        int index = Math.Max(0, formats.ToList().FindIndex(f => f.Format == preferred));
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = AudioFileService.ExportFilter,
            FilterIndex = index + 1,
            FileName = Path.GetFileNameWithoutExtension(suggestedName) + formats[index].Extension,
            DefaultExt = formats[index].Extension,
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (!Show(dialog))
        {
            return null;
        }

        var chosen = formats[Math.Clamp(dialog.FilterIndex - 1, 0, formats.Count - 1)];
        string path = dialog.FileName;
        if (!string.Equals(Path.GetExtension(path), chosen.Extension, StringComparison.OrdinalIgnoreCase))
        {
            // Endung passt nicht zum gewählten Format: Ist es eine bekannte Endung, gilt sie – sonst wird ergänzt.
            if (formats.Any(f => string.Equals(f.Extension, Path.GetExtension(path), StringComparison.OrdinalIgnoreCase)))
            {
                return new ExportTarget(path, AudioFileService.FormatFor(path, chosen.Format));
            }

            path += chosen.Extension;
        }

        return new ExportTarget(path, chosen.Format);
    }

    public string? ShowFolderDialog(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return Show(dialog) ? dialog.FolderName : null;
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

    public void ShowSheetMusic(SheetMusicRequest request) => new SheetMusicWindow(request) { Owner = Owner }.ShowDialog();

    public DrumGridResult? EditDrumPattern(DrumGridRequest request)
    {
        var window = new DrumGridWindow(request) { Owner = Owner };
        return window.ShowDialog() == true ? window.Result : null;
    }

    public bool Confirm(string message, string title)
    {
        var result = Owner is { } owner
            ? MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    public string? AskText(string title, string prompt, string initialText)
    {
        var dialog = new TextInputDialog(title, prompt, initialText) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Text : null;
    }

    public void ShowInfo(string message, string title)
    {
        if (Owner is { } owner)
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
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
