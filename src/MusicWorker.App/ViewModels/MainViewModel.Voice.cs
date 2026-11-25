using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Services;
using MusicWorker.Core.Effects;

namespace MusicWorker.App.ViewModels;

/// <summary>Stimme &amp; KI im Editor: De-Esser, natürliche Tonhöhe, Gesang trennen.</summary>
public partial class MainViewModel
{
    private (double Frequency, double Reduction, double Sensitivity) _deEsser = (5.5, 10, 5);
    private (double Semitones, double Formant) _naturalPitch = (2, 0);

    /// <summary>Zischlaute („s“, „sch“) dämpfen – auf der Auswahl bzw. der ganzen Datei.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task DeEsserAsync()
    {
        var values = _dialogs.AskParameters(new ParameterRequest(
            "De-Esser",
            "Macht scharfe Zischlaute („s“, „sch“, „z“) leiser – der Rest der Stimme bleibt unverändert. Ideal für Gesang, Sprache und Podcasts.",
            [
                new SliderParameter("Ab Frequenz", 3, 10, _deEsser.Frequency, 0.5, "kHz", "Frauenstimmen eher 6 – 8 kHz, Männerstimmen eher 4 – 6 kHz."),
                new SliderParameter("Stärke", 3, 20, _deEsser.Reduction, 1, "dB", "So viele dB werden Zischlaute höchstens leiser."),
                new SliderParameter("Empfindlichkeit", 1, 10, _deEsser.Sensitivity, 1, string.Empty, "1 = nur sehr scharfe Zischlaute, 10 = schon leichte."),
            ]));
        if (values is null)
        {
            return;
        }

        _deEsser = (values[0], values[1], values[2]);
        await ApplyEffectAsync(new DeEsserEffect(values[0] * 1000, values[1], values[2]));
    }

    /// <summary>Tonhöhe ändern, ohne dass die Stimme nach Micky Maus oder Monster klingt.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task NaturalPitchAsync()
    {
        var values = _dialogs.AskParameters(new ParameterRequest(
            "Tonhöhe ändern – Klangfarbe erhalten",
            "Verschiebt die Tonhöhe, die Länge bleibt gleich. Die Klangfarbe der Stimme (Formanten) bleibt erhalten – so klingt eine höhere Stimme nicht nach Micky Maus und eine tiefere nicht nach Monster.",
            [
                new SliderParameter("Tonhöhe", -12, 12, _naturalPitch.Semitones, 0.5, "Halbtöne", "12 Halbtöne = eine Oktave."),
                new SliderParameter("Klangfarbe", -6, 6, _naturalPitch.Formant, 0.5, "Halbtöne", "0 = wie das Original. Plus = heller (kleiner, jünger), Minus = dunkler (größer, älter)."),
            ]));
        if (values is null || (Math.Abs(values[0]) < 0.01 && Math.Abs(values[1]) < 0.01))
        {
            return;
        }

        _naturalPitch = (values[0], values[1]);
        await ApplyEffectAsync(new FormantPreservingPitchEffect(values[0], values[1]));
    }

    /// <summary>Gesang und Begleitung per KI trennen: nur Gesang, nur Begleitung (Karaoke) oder beides ins Mischpult.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task SeparateVocalsAsync()
    {
        var values = _dialogs.AskParameters(new ParameterRequest(
            "Gesang und Begleitung trennen (KI)",
            "Eine künstliche Intelligenz trennt die Stimme von der Musik – komplett auf deinem Rechner. Wirkt auf die Auswahl, ohne Auswahl auf die ganze Datei. " +
            "Ein Song von 4 Minuten dauert je nach Rechner etwa 20 – 60 Sekunden.",
            [
                new ChoiceParameter("Ergebnis", ["Nur den Gesang behalten", "Nur die Begleitung behalten (Karaoke)", "Beides als zwei Spuren ins Mischpult"], 1),
            ]));
        if (values is null || !await AiModels.EnsureAsync(VocalSeparation.Default, _dialogs, text => StatusText = text))
        {
            return;
        }

        int choice = (int)values[0];
        if (choice < 2)
        {
            await ApplyEffectAsync(new KeepStemEffect(VocalSeparation.Default, choice == 0 ? Stem.Vocals : Stem.Accompaniment), respectChannelTarget: false);
            return;
        }

        StopPlayback();
        var range = _editor.EffectRange;
        var input = Buffer.Slice(range.Start, range.Length);
        IsBusy = true;
        StatusText = "Die KI trennt Gesang und Begleitung …";
        try
        {
            var progress = new Progress<double>(p => StatusText = string.Format(CultureInfo.CurrentCulture, "Die KI trennt Gesang und Begleitung … {0:0} %", p * 100));
            var (vocals, accompaniment) = await Task.Run(() => VocalSeparation.Default.Separate(input, progress));
            string name = Path.GetFileNameWithoutExtension(FileName);
            name = string.IsNullOrWhiteSpace(name) ? "Song" : name;
            Mixer.AddTrack(vocals, $"{name} – Gesang");
            Mixer.AddTrack(accompaniment, $"{name} – Begleitung");
            SelectedMainTab = 1;
            StatusText = "Gesang und Begleitung liegen jetzt als zwei Spuren im Mischpult.";
        }
        catch (Exception ex)
        {
            StatusText = "Die Trennung ist fehlgeschlagen.";
            _dialogs.ShowError($"Gesang und Begleitung konnten nicht getrennt werden.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Kümmert sich darum, dass KI-Modelle vor dem ersten Benutzen (mit Zustimmung) heruntergeladen werden.</summary>
internal static class AiModels
{
    public static async Task<bool> EnsureAsync(VocalSeparation separation, IDialogService dialogs, Action<string> status)
    {
        if (separation.IsModelInstalled)
        {
            return true;
        }

        if (!dialogs.Confirm(
            $"Für die KI-Trennung wird einmalig ein Modell heruntergeladen (ca. {VocalSeparation.ModelMegabytes} MB, „Spleeter“ von Deezer, freie MIT-Lizenz). " +
            "Danach funktioniert alles ohne Internet, und deine Musik verlässt nie deinen Rechner.\n\nJetzt herunterladen?",
            "KI-Modell herunterladen"))
        {
            return false;
        }

        try
        {
            var progress = new Progress<double>(p => status(string.Format(CultureInfo.CurrentCulture, "KI-Modell wird heruntergeladen … {0:0} %", p * 100)));
            await separation.DownloadModelAsync(progress);
            status("KI-Modell installiert.");
            return true;
        }
        catch (Exception ex)
        {
            status("Das KI-Modell konnte nicht heruntergeladen werden.");
            dialogs.ShowError($"Das KI-Modell konnte nicht heruntergeladen werden. Bitte die Internetverbindung prüfen und es noch einmal versuchen.\n\n{ex.Message}");
            return false;
        }
    }
}
