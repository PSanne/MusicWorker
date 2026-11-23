namespace MusicWorker.Core.Dsp;

/// <summary>
/// Einstellungen des grafischen 15-Band-Equalizers (Bänder im Abstand von 2/3 Oktave, ISO-Mittenfrequenzen).
/// </summary>
public sealed class EqualizerSettings
{
    /// <summary>Mittenfrequenzen der 15 Bänder in Hz.</summary>
    public static IReadOnlyList<double> BandFrequencies { get; } =
        [25, 40, 63, 100, 160, 250, 400, 630, 1000, 1600, 2500, 4000, 6300, 10000, 16000];

    /// <summary>Maximale Anhebung/Absenkung pro Band in dB.</summary>
    public const double MaxGainDb = 12;

    /// <summary>Güte passend zu 2/3-Oktav-Bändern: √2^N / (2^N − 1) mit N = 2/3.</summary>
    public static readonly double BandQ = Math.Sqrt(Math.Pow(2, 2.0 / 3)) / (Math.Pow(2, 2.0 / 3) - 1);

    public EqualizerSettings(IReadOnlyList<double> gainsDb, double preampDb = 0)
    {
        ArgumentNullException.ThrowIfNull(gainsDb);
        if (gainsDb.Count != BandFrequencies.Count)
        {
            throw new ArgumentException($"Es werden genau {BandFrequencies.Count} Bandwerte erwartet.", nameof(gainsDb));
        }

        GainsDb = gainsDb.Select(g => Math.Clamp(g, -MaxGainDb, MaxGainDb)).ToArray();
        PreampDb = Math.Clamp(preampDb, -MaxGainDb, MaxGainDb);
    }

    public static EqualizerSettings Neutral { get; } = new(new double[BandFrequencies.Count]);

    /// <summary>Verstärkung pro Band in dB (gleiche Reihenfolge wie <see cref="BandFrequencies"/>).</summary>
    public IReadOnlyList<double> GainsDb { get; }

    /// <summary>Vorverstärkung vor den Filtern in dB.</summary>
    public double PreampDb { get; }

    public bool IsNeutral => PreampDb == 0 && GainsDb.All(g => g == 0);

    /// <summary>Filterkoeffizienten der 15 Bänder für eine Abtastrate.</summary>
    public BiquadCoefficients[] CreateCoefficients(int sampleRate)
    {
        var coefficients = new BiquadCoefficients[BandFrequencies.Count];
        for (int band = 0; band < coefficients.Length; band++)
        {
            coefficients[band] = BiquadCoefficients.PeakingEq(sampleRate, BandFrequencies[band], BandQ, GainsDb[band]);
        }

        return coefficients;
    }

    /// <summary>
    /// Gesamter Frequenzgang (alle Bänder plus Vorverstärkung) in dB bei den angegebenen Frequenzen –
    /// für die EQ-Kurve in der Spektrumanzeige.
    /// </summary>
    public double[] ResponseDb(int sampleRate, IReadOnlyList<double> frequencies)
    {
        var coefficients = CreateCoefficients(sampleRate);
        var result = new double[frequencies.Count];
        for (int i = 0; i < result.Length; i++)
        {
            double db = PreampDb;
            if (frequencies[i] < sampleRate / 2.0)
            {
                foreach (var c in coefficients)
                {
                    db += c.MagnitudeDb(frequencies[i], sampleRate);
                }
            }

            result[i] = db;
        }

        return result;
    }
}

/// <summary>Eine benannte Voreinstellung für den Equalizer.</summary>
public sealed record EqualizerPreset(string Name, IReadOnlyList<double> GainsDb)
{
    public override string ToString() => Name;
}

public static class EqualizerPresets
{
    //                                    25  40  63 100 160 250 400 630  1k 1.6k 2.5k 4k 6.3k 10k 16k
    public static IReadOnlyList<EqualizerPreset> All { get; } =
    [
        new("Neutral",           [  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0]),
        new("Bass-Boost",        [  6,  6,  5,  4,  2,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0]),
        new("Bass reduzieren",   [ -6, -6, -5, -4, -2,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0]),
        new("Höhen-Boost",       [  0,  0,  0,  0,  0,  0,  0,  0,  0,  1,  2,  3,  4,  5,  5]),
        new("Höhen reduzieren",  [  0,  0,  0,  0,  0,  0,  0,  0,  0, -1, -2, -3, -4, -5, -5]),
        new("Loudness",          [  6,  5,  4,  2,  0, -1, -1, -1,  0,  0,  1,  2,  3,  4,  4]),
        new("Stimme / Sprache",  [ -6, -5, -4, -2,  0,  1,  2,  3,  3,  3,  2,  1,  0, -1, -2]),
        new("Rock",              [  4,  4,  3,  2,  0, -1, -2, -1,  0,  1,  2,  3,  4,  4,  4]),
        new("Pop",               [ -1,  0,  1,  2,  3,  3,  2,  0, -1, -1,  0,  1,  2,  2,  2]),
        new("Jazz",              [  3,  3,  2,  2,  1,  0, -1, -1,  0,  1,  1,  2,  2,  3,  3]),
        new("Klassik",           [  0,  0,  0,  0,  0,  0,  0,  0,  0,  0, -1, -2, -3, -4, -4]),
        new("Elektronisch",      [  5,  5,  4,  1,  0, -2, -2,  0,  1,  2,  1,  2,  4,  5,  5]),
        new("Telefon",           [-12,-12,-12,-10, -6,  0,  3,  5,  6,  5,  3, -6,-12,-12,-12]),
    ];
}
