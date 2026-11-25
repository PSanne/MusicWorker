using System.Globalization;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;

namespace MusicWorker.App.ViewModels;

/// <summary>Auswahl der Lautheitsziele und Texte für Meldungen.</summary>
public static class LoudnessOptions
{
    public const string CustomName = "Eigener Wert (Regler unten)";

    /// <summary>Ziele für „Auf Lautheit normalisieren“: die üblichen Werte plus eigener Wert.</summary>
    public static IReadOnlyList<string> TargetNames { get; } = [.. Loudness.Targets.Select(t => t.Name), CustomName];

    /// <summary>Für den Mixdown zusätzlich „nicht verändern“ an erster Stelle.</summary>
    public static IReadOnlyList<string> MixdownNames { get; } = ["Nicht verändern (nur Übersteuerung verhindern)", .. TargetNames];

    /// <summary>LUFS-Wert zu einem Eintrag aus <see cref="TargetNames"/>.</summary>
    public static double TargetFor(int index, double custom) =>
        index >= 0 && index < Loudness.Targets.Count ? Loudness.Targets[index].Lufs : Math.Clamp(custom, -40, -5);

    public static string Format(double lufs) =>
        double.IsNegativeInfinity(lufs) ? "–" : lufs.ToString("0.0", CultureInfo.CurrentCulture) + " LUFS";

    /// <summary>„Lautheit von -20,3 auf -14,0 LUFS gebracht …“</summary>
    public static string Describe(LoudnessReport report)
    {
        string text = $"Lautheit von {Format(report.BeforeLufs)} auf {Format(report.AfterLufs)} gebracht";
        if (report.LimitedDb > 0.05)
        {
            text += $", Spitzen um bis zu {report.LimitedDb.ToString("0.0", CultureInfo.CurrentCulture)} dB begrenzt";
        }

        text += $" (echte Spitze {report.TruePeakDb.ToString("0.0", CultureInfo.CurrentCulture)} dBTP).";
        if (report.LimitedDb > 6)
        {
            text += " Achtung: Das ist stark begrenzt und kann gepresst klingen – ein etwas leiseres Ziel klingt natürlicher.";
        }

        return text;
    }

    /// <summary>Ausführliches Messergebnis für den Hinweisdialog.</summary>
    public static string Explain(LoudnessResult result)
    {
        var c = CultureInfo.CurrentCulture;
        return
            $"Gesamtlautheit:  {Format(result.IntegratedLufs)}\n" +
            $"Lauteste Stelle (3 s):  {Format(result.MaxShortTermLufs)}\n" +
            $"Lautheitsumfang:  {result.LoudnessRange.ToString("0.0", c)} LU\n" +
            $"Echte Spitze:  {result.TruePeakDb.ToString("0.0", c)} dBTP (Sample-Spitze {result.SamplePeakDb.ToString("0.0", c)} dBFS)\n\n" +
            "Zum Vergleich: Spotify, YouTube und Tidal spielen mit -14 LUFS ab, Apple Music mit -16, Podcasts liegen meist bei " +
            "-16 LUFS, Rundfunk bei -23 LUFS. Ist dein Stück lauter, wird es dort leiser gestellt – ist es leiser, klingt es " +
            "neben anderen Titeln schwächer.\n\n" +
            "Die echte Spitze sollte unter -1 dBTP liegen, damit es nach dem Umwandeln (MP3, AAC) nicht verzerrt.\n" +
            "Ein Lautheitsumfang unter 5 LU bedeutet gleichmäßig laut (Pop, Sprache), über 10 LU sehr dynamisch (Klassik, Film).";
    }
}
