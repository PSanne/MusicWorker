namespace MusicWorker.Core.Dsp;

/// <summary>
/// Logarithmische Frequenzachse der Spektrumanzeige: genau 10 Oktaven von ≈ 19,8 Hz bis ≈ 20,3 kHz.
/// Sie ist so gewählt, dass die 15 Equalizer-Bänder (Abstand 2/3 Oktave) bei gleich breiten Spalten
/// exakt in der Spaltenmitte liegen – jeder Regler steht also genau unter seiner Frequenz.
/// </summary>
public static class SpectrumScale
{
    public static readonly double MinFrequency = 25 * Math.Pow(2, -1.0 / 3);
    public static readonly double MaxFrequency = 25 * Math.Pow(2, 29.0 / 3);

    /// <summary>Position 0 … 1 auf der Achse für eine Frequenz.</summary>
    public static double ToPosition(double frequency) =>
        Math.Log(frequency / MinFrequency) / Math.Log(MaxFrequency / MinFrequency);

    /// <summary>Frequenz an einer Position 0 … 1 der Achse.</summary>
    public static double FromPosition(double position) =>
        MinFrequency * Math.Pow(MaxFrequency / MinFrequency, position);

    /// <summary>Mittenfrequenzen von <paramref name="count"/> gleich breiten Spalten.</summary>
    public static double[] CenterFrequencies(int count) =>
        Enumerable.Range(0, count).Select(i => FromPosition((i + 0.5) / count)).ToArray();
}
