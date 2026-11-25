using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Services;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;

namespace MusicWorker.App.ViewModels;

/// <summary>Lautheit (LUFS): messen, auf ein Ziel bringen und während der Wiedergabe anzeigen.</summary>
public sealed partial class MainViewModel
{
    private LoudnessMeter? _liveLoudness;

    /// <summary>Live-Anzeige während der Wiedergabe, z. B. „Lautheit: -14,2 LUFS“.</summary>
    [ObservableProperty]
    private string _loudnessText = string.Empty;

    /// <summary>Misst die Lautheit der Auswahl (ohne Auswahl der ganzen Datei) und erklärt das Ergebnis.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task MeasureLoudnessAsync()
    {
        var range = _editor.EffectRange;
        var audio = Buffer.Slice(range.Start, range.Length);
        IsBusy = true;
        StatusText = "Lautheit wird gemessen …";
        try
        {
            var result = await Task.Run(() => Loudness.Measure(audio));
            StatusText = $"Gesamtlautheit {LoudnessOptions.Format(result.IntegratedLufs)}";
            _dialogs.ShowInfo(
                (_editor.HasSelection ? "Gemessen wurde die Auswahl.\n\n" : string.Empty) +
                (result.HasLoudness ? LoudnessOptions.Explain(result) : "Das Audio ist zu leise oder kürzer als 0,4 Sekunden – die Lautheit lässt sich nicht messen."),
                "Lautheit messen");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Bringt die Auswahl (oder die ganze Datei) auf eine Ziel-Lautheit, z. B. -14 LUFS für Spotify.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task NormalizeLoudnessAsync()
    {
        var saved = _settings.Settings;
        var answer = _dialogs.AskParameters(new ParameterRequest(
            "Auf Lautheit normalisieren",
            "Normalisieren nach Lautheit richtet sich danach, wie laut wir etwas empfinden – nicht nur nach der höchsten Spitze. " +
            "So klingt dein Stück auf Spotify, YouTube oder im Podcast-Player so laut wie andere Titel. Ragen dabei Spitzen " +
            "über die Obergrenze, werden nur diese sanft abgesenkt.",
            [
                new ChoiceParameter("Ziel", LoudnessOptions.TargetNames, Math.Clamp(saved.LastLoudnessChoice, 0, LoudnessOptions.TargetNames.Count - 1)),
                new SliderParameter("Eigener Wert", -30, -6, saved.CustomLoudnessLufs, 0.5, "LUFS", "Nur bei „Eigener Wert“."),
                new SliderParameter("Obergrenze für Spitzen", -3, 0, -1, 0.1, "dBTP", "Empfohlen: -1 dBTP – so verzerrt es auch nach dem Umwandeln in MP3/AAC nicht."),
            ]));
        if (answer is null)
        {
            return;
        }

        saved.LastLoudnessChoice = (int)answer[0];
        saved.CustomLoudnessLufs = answer[1];
        _settings.Save();

        var effect = new LoudnessNormalizeEffect(LoudnessOptions.TargetFor((int)answer[0], answer[1]), answer[2]);
        if (await ApplyEffectAsync(effect, respectChannelTarget: false) && effect.Report is { } report)
        {
            StatusText = LoudnessOptions.Describe(report);
        }
    }

    /// <summary>Lautheitsmessung in die Wiedergabekette einhängen (ganz am Ende, misst also, was zu hören ist).</summary>
    private NAudio.Wave.ISampleProvider WithLiveLoudness(NAudio.Wave.ISampleProvider source)
    {
        _liveLoudness = new LoudnessMeter(source.WaveFormat.SampleRate, source.WaveFormat.Channels);
        return new Audio.LoudnessTapSampleProvider(source, _liveLoudness);
    }

    private void UpdateLiveLoudness()
    {
        LoudnessText = _liveLoudness is { } meter && !double.IsNegativeInfinity(meter.ShortTerm)
            ? $"Lautheit: {LoudnessOptions.Format(meter.ShortTerm)}"
            : string.Empty;
    }
}
