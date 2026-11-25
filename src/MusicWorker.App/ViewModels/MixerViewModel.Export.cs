using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Audio;
using MusicWorker.App.Services;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;
using MusicWorker.Core.Mixing;

namespace MusicWorker.App.ViewModels;

/// <summary>Mixdown-Export (mit Lautheitsziel), Einzelspuren (Stems) und Live-Lautheitsanzeige.</summary>
public sealed partial class MixerViewModel
{
    private LoudnessMeter? _loudness;

    /// <summary>Gespeicherte Einstellungen (vom Hauptfenster gesetzt; ohne sie gelten Standardwerte).</summary>
    public SettingsService? Settings { get; set; }

    /// <summary>Live-Lautheit der Wiedergabe, z. B. „-14,2 LUFS“ (Kurzzeit, 3 s).</summary>
    [ObservableProperty]
    private string _loudnessText = "–";

    private ExportFormat PreferredExportFormat =>
        Settings is { } s && Enum.TryParse<ExportFormat>(s.Settings.LastExportFormat, out var format) ? format : ExportFormat.Wav16;

    private void RememberExportFormat(ExportFormat format)
    {
        if (Settings is { } s)
        {
            s.Settings.LastExportFormat = format.ToString();
            s.Save();
        }
    }

    /// <summary>Für die Wiedergabe: Lautheitsmessung zwischen Mischpult und Soundkarte schalten.</summary>
    private NAudio.Wave.ISampleProvider WithLoudnessMeter(NAudio.Wave.ISampleProvider source)
    {
        _loudness = new LoudnessMeter(source.WaveFormat.SampleRate, source.WaveFormat.Channels);
        LoudnessText = "–";
        return new LoudnessTapSampleProvider(source, _loudness);
    }

    private void UpdateLoudnessText()
    {
        if (_loudness is { } meter)
        {
            LoudnessText = LoudnessOptions.Format(meter.ShortTerm);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditWithTracks))]
    private async Task ExportMixdownAsync()
    {
        string name = ProjectPath is null ? "Mixdown" : Path.GetFileNameWithoutExtension(ProjectPath) + " (Mixdown)";
        if (_dialogs.ShowExportDialog(name, "Mixdown exportieren", PreferredExportFormat) is not { } target)
        {
            return;
        }

        RememberExportFormat(target.Format);
        int choice = Settings?.Settings.MixdownLoudnessChoice ?? 0;
        double custom = Settings?.Settings.CustomLoudnessLufs ?? -14;
        var answer = _dialogs.AskParameters(new ParameterRequest(
            "Mixdown exportieren",
            "Soll der Mixdown auf eine bestimmte Lautheit gebracht werden? Streaming-Dienste stellen alle Titel auf " +
            "eine gleiche Lautheit ein – mit dem passenden Ziel klingt dein Stück dort so, wie du es hörst.",
            [
                new ChoiceParameter("Lautheit", LoudnessOptions.MixdownNames, Math.Clamp(choice, 0, LoudnessOptions.MixdownNames.Count - 1),
                    "Spitzen werden dabei automatisch unter -1 dBTP gehalten."),
                new SliderParameter("Eigener Wert", -30, -6, custom, 0.5, "LUFS", "Nur bei „Eigener Wert“."),
            ]));
        if (answer is null)
        {
            return;
        }

        choice = (int)answer[0];
        custom = answer[1];
        if (Settings is { } settings)
        {
            settings.Settings.MixdownLoudnessChoice = choice;
            settings.Settings.CustomLoudnessLufs = custom;
            settings.Save();
        }

        StopPlayback();
        IsBusy = true;
        StatusText = "Mixdown wird berechnet …";
        string path = target.Path;
        try
        {
            var markers = Markers;
            string? report = await Task.Run(() =>
            {
                var mix = RenderMixdown();
                string? message = null;
                if (choice > 0)
                {
                    var (normalized, loudness) = LoudnessNormalizer.Normalize(mix, LoudnessOptions.TargetFor(choice - 1, custom));
                    mix = normalized;
                    message = LoudnessOptions.Describe(loudness);
                }

                _files.Export(mix, path, target.Format);
                if (AudioFileService.SupportsMarkers(path))
                {
                    WavMarkers.Append(path, markers);
                }

                return message;
            });
            StatusText = $"Mixdown exportiert nach „{Path.GetFileName(path)}“." + (report is null ? string.Empty : " " + report);
        }
        catch (Exception ex)
        {
            StatusText = "Mixdown fehlgeschlagen.";
            _dialogs.ShowError($"Der Mixdown konnte nicht gespeichert werden.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Jede (nicht stummgeschaltete) Spur als eigene Datei – alle gleich lang, für andere Programme.</summary>
    [RelayCommand(CanExecute = nameof(CanEditWithTracks))]
    private async Task ExportStemsAsync()
    {
        if (_dialogs.ShowFolderDialog("Ordner für die Einzelspuren wählen") is not { } folder)
        {
            return;
        }

        var formats = AudioFileService.ExportFormats;
        int formatIndex = Math.Max(0, formats.ToList().FindIndex(f => f.Format == PreferredExportFormat));
        var answer = _dialogs.AskParameters(new ParameterRequest(
            "Einzelspuren exportieren",
            "Jede Spur wird als eigene Datei gespeichert – alle gleich lang und ab Projektbeginn, damit sie in einem " +
            "anderen Programm wieder genau zusammenpassen. Stummgeschaltete Spuren werden übersprungen.",
            [
                new ChoiceParameter("Format", formats.Select(f => f.Name).ToArray(), formatIndex),
                new ChoiceParameter("Inhalt",
                    ["Wie im Mix: Lautstärke, Panorama, Effekte und Hall-/Echo-Anteil", "Roh: nur die Clips, ohne Mischpult-Einstellungen"], 0,
                    "„Wie im Mix“: Alle Dateien zusammen ergeben wieder den Mix (ohne Master-Effekte)."),
            ]));
        if (answer is null)
        {
            return;
        }

        var format = formats[(int)answer[0]];
        bool raw = (int)answer[1] == 1;
        RememberExportFormat(format.Format);

        string project = ProjectPath is null ? "Projekt" : Path.GetFileNameWithoutExtension(ProjectPath);
        var snapshot = BuildSnapshot();
        var jobs = new List<(int Index, string Path)>();
        for (int i = 0; i < Tracks.Count; i++)
        {
            if (!Tracks[i].Mute)
            {
                jobs.Add((i, Path.Combine(folder, SafeFileName($"{project} - {i + 1:00} {Tracks[i].Name}") + format.Extension)));
            }
        }

        if (jobs.Count == 0)
        {
            _dialogs.ShowError("Alle Spuren sind stummgeschaltet – es gibt nichts zu exportieren.");
            return;
        }

        var existing = jobs.Where(j => File.Exists(j.Path)).Select(j => Path.GetFileName(j.Path)).ToArray();
        if (existing.Length > 0 && !_dialogs.Confirm(
                "Diese Dateien gibt es schon, sie werden überschrieben:\n\n" + string.Join("\n", existing.Take(10)) + (existing.Length > 10 ? "\n…" : string.Empty),
                "Dateien überschreiben?"))
        {
            return;
        }

        StopPlayback();
        IsBusy = true;
        long length = snapshot.Length;
        double tail = TailFrames() / (double)SampleRate;
        try
        {
            for (int n = 0; n < jobs.Count; n++)
            {
                var (index, path) = jobs[n];
                StatusText = $"Einzelspur {n + 1} von {jobs.Count}: „{Tracks[index].Name}“ …";
                await Task.Run(() =>
                {
                    var stem = MixEngine.Mixdown(Stems.ForTrack(snapshot, index, raw), raw ? 0 : tail, length);
                    _files.Export(stem, path, format.Format);
                });
            }

            StatusText = $"{jobs.Count} Einzelspuren gespeichert in „{folder}“.";
        }
        catch (Exception ex)
        {
            StatusText = "Export der Einzelspuren fehlgeschlagen.";
            _dialogs.ShowError($"Die Einzelspuren konnten nicht gespeichert werden.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string SafeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.Trim();
    }
}
