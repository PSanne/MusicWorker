using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.Core.Analysis;
using MusicWorker.Core.Effects;

namespace MusicWorker.App.ViewModels;

/// <summary>Musik-Assistent im Editor: Tempo und Tonart erkennen.</summary>
public partial class MainViewModel
{
    /// <summary>Erkennt Tempo (BPM) und Tonart der Auswahl bzw. der ganzen Datei und bietet an, sie zu übernehmen.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAudio))]
    private async Task DetectTempoAndKeyAsync()
    {
        var range = _editor.EffectRange;
        var slice = Buffer.Slice(range.Start, range.Length);
        IsBusy = true;
        StatusText = "Tempo und Tonart werden erkannt …";
        TempoResult? tempo;
        KeyResult? key;
        try
        {
            (tempo, key) = await Task.Run(() => (MusicAnalysis.DetectTempo(slice), MusicAnalysis.DetectKey(slice)));
        }
        finally
        {
            IsBusy = false;
        }

        string tempoText = tempo is null
            ? "Tempo: kein gleichmäßiger Rhythmus erkannt."
            : string.Format(CultureInfo.CurrentCulture, "Tempo: etwa {0:0.#} BPM ({1}). Manchmal empfindet man auch das halbe ({2:0.#}) oder doppelte ({3:0.#}) Tempo.",
                tempo.Bpm, Certainty(tempo.Confidence), tempo.Bpm / 2, tempo.Bpm * 2);
        string keyText = key is null
            ? "Tonart: nicht erkennbar (zu wenig Töne)."
            : string.Format(CultureInfo.CurrentCulture, "Tonart: {0} ({1}).", key.Name, Certainty(key.Confidence));
        StatusText = $"{tempoText} {keyText}";
        if (key is null)
        {
            _dialogs.ShowInfo($"{tempoText}\n\n{keyText}", "Tempo und Tonart");
            return;
        }

        if (_dialogs.Confirm($"{tempoText}\n\n{keyText}\n\nSoll die Tonart {key.Name} für Autotune und als Tonart des Projekts (Akkordfolgen im Noten-Editor) übernommen werden?", "Tempo und Tonart"))
        {
            _autotune = (key.Tonic, Scales.All.ToList().IndexOf(key.Scale), _autotune.Speed, _autotune.Strength);
            Mixer.ProjectKey = key;
            StatusText = $"Tonart {key.Name} übernommen – Autotune und Akkordfolgen verwenden sie jetzt.";
        }
    }

    private static string Certainty(double confidence) =>
        confidence >= 0.5 ? "deutlich" : confidence >= 0.2 ? "ziemlich sicher" : "unsicher";
}
