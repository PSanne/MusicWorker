namespace MusicWorker.Core.Audio;

/// <summary>Umrechnung zwischen Dezibel und linearem Verstärkungsfaktor.</summary>
public static class Decibels
{
    public static double ToGain(double decibels) => Math.Pow(10.0, decibels / 20.0);

    public static double FromGain(double gain) =>
        gain <= 0 ? double.NegativeInfinity : 20.0 * Math.Log10(gain);
}
