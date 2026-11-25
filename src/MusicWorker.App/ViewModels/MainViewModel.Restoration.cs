using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Services;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Editing;
using MusicWorker.Core.Effects;

namespace MusicWorker.App.ViewModels;

/// <summary>Restaurierung: Knackser/Knistern entfernen und Störgeräusche im Spektrum reparieren.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Im Spektrogramm mit Strg+Ziehen markierter Frequenzbereich (null = alle Frequenzen).</summary>
    [ObservableProperty]
    private FrequencyBand? _spectralBand;

    public string SpectralBandText => SpectralBand is { } band
        ? $"{FormatHz(band.LowHz)} – {FormatHz(band.HighHz)}"
        : "alle Frequenzen";

    partial void OnSpectralBandChanged(FrequencyBand? value)
    {
        OnPropertyChanged(nameof(SpectralBandText));
        RepairSpectrumCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task DeclickAsync()
    {
        var answer = _dialogs.AskParameters(new ParameterRequest(
            "Knackser und Knistern entfernen",
            "Findet kurze Störungen – Knackser und Knistern von Schallplatten oder Kassetten, Klicks von Schnitten – und " +
            "berechnet diese Stellen aus dem Klang davor und danach neu. Wirkt auf die Auswahl, ohne Auswahl auf die ganze Datei.",
            [
                new SliderParameter("Empfindlichkeit", 1, 10, 5, 0.5, "", "Höher = findet auch leises Knistern (kann bei sehr spitzen Klängen wie Schlagzeug zu viel erwischen)."),
                new SliderParameter("Länge bis", 0.5, 5, 2, 0.1, "ms", "Längere Störungen bleiben unangetastet. 2 ms passt für die meisten Knackser."),
            ]));
        if (answer is null)
        {
            return;
        }

        var effect = new DeclickEffect(answer[0], answer[1]);
        if (await ApplyEffectAsync(effect))
        {
            StatusText = effect.RepairedClicks switch
            {
                0 => "Keine Knackser gefunden – versuche eine höhere Empfindlichkeit.",
                1 => "1 Knackser entfernt.",
                var n => $"{n} Knackser entfernt.",
            };
        }
    }

    /// <summary>Markierte Stelle (und ggf. Frequenzbereich) mit dem Klang der Umgebung ersetzen oder leiser machen.</summary>
    [RelayCommand(CanExecute = nameof(CanRepairSpectrum))]
    private async Task RepairSpectrumAsync()
    {
        var selection = Selection;
        var answer = _dialogs.AskParameters(new ParameterRequest(
            "Störgeräusch entfernen",
            $"Bereich: {FormatFrame(selection.Start)} – {FormatFrame(selection.End)}, Frequenzen: {SpectralBandText}.\n\n" +
            "Tipp: Im Spektrogramm (Menü „Tonhöhe & Spektrum“) mit Strg+Ziehen nur die Störung markieren – z. B. das " +
            "Klingeln eines Telefons. Dann bleibt alles andere unverändert.",
            [
                new ChoiceParameter("Vorgehen", ["Durch den Klang davor und danach ersetzen", "Nur leiser machen"], 0,
                    "„Ersetzen“ eignet sich für Husten, Klingeln, Stuhlknarren; „Leiser“ für gleichmäßige Störungen."),
                new SliderParameter("Höchstens leiser um", 6, 60, 30, 1, "dB"),
            ]));
        if (answer is null)
        {
            return;
        }

        StopPlayback();
        int context = (int)(SpectralRepairEffect.ContextSeconds * Buffer.SampleRate * 1.5);
        int regionStart = Math.Max(0, selection.Start - context);
        int regionEnd = Math.Min(Buffer.FrameCount, selection.End + context);
        var band = SpectralBand;
        IAudioEffect effect = new SpectralRepairEffect(
            selection.Start - regionStart, selection.End - regionStart,
            band?.LowHz ?? 0, band?.HighHz ?? Buffer.SampleRate / 2.0,
            answer[0] < 0.5 ? SpectralRepairMode.Replace : SpectralRepairMode.Attenuate, answer[1]);
        if (!EditsAllChannels)
        {
            effect = new ChannelEffect(effect, EditLeft ? [0] : [1]);
        }

        var region = new SelectionRange(regionStart, regionEnd);
        var input = Buffer.Slice(region.Start, region.Length);
        IsBusy = true;
        StatusText = "Störgeräusch wird entfernt …";
        try
        {
            var output = await Task.Run(() => effect.Apply(input));
            _editor.ApplyEffectResult(effect.Name, region, output);
            _editor.Selection = selection;
            StatusText = $"{effect.Name} ✓ – mit Strg+Z rückgängig.";
        }
        catch (Exception ex)
        {
            StatusText = "Störgeräusch entfernen fehlgeschlagen.";
            _dialogs.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRepairSpectrum() => CanUseAudio() && HasSelection;
}
