namespace MusicWorker.Core.Dsp;

/// <summary>
/// Einstellungen des Kompressors. Die Regelparameter gelten für alle Kanäle, die Stärke
/// (Anteil des komprimierten Signals, „Parallel-Kompression“) ist pro Kanal einstellbar.
/// </summary>
public sealed class CompressorSettings
{
    public CompressorSettings(
        IReadOnlyList<double> mixPercent,
        double thresholdDb = -18,
        double ratio = 4,
        double attackMs = 10,
        double releaseMs = 150,
        double makeupDb = 0,
        bool linked = true)
    {
        ArgumentNullException.ThrowIfNull(mixPercent);
        if (mixPercent.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Kanal ist erforderlich.", nameof(mixPercent));
        }

        MixPercent = mixPercent.Select(m => Math.Clamp(m, 0, 100)).ToArray();
        ThresholdDb = Math.Clamp(thresholdDb, -60, 0);
        Ratio = Math.Clamp(ratio, 1, 20);
        AttackMs = Math.Clamp(attackMs, 0.1, 500);
        ReleaseMs = Math.Clamp(releaseMs, 5, 3000);
        MakeupDb = Math.Clamp(makeupDb, 0, 24);
        Linked = linked;
    }

    /// <summary>Kein Kompressor.</summary>
    public static CompressorSettings Off { get; } = new([0]);

    /// <summary>Stärke je Kanal in % (bei weniger Werten als Kanälen gilt der letzte für den Rest).</summary>
    public IReadOnlyList<double> MixPercent { get; }

    /// <summary>Ab diesem Pegel wird geregelt (dBFS).</summary>
    public double ThresholdDb { get; }

    /// <summary>Verhältnis, z. B. 4 = 4:1 (4 dB mehr Eingang → 1 dB mehr Ausgang).</summary>
    public double Ratio { get; }

    /// <summary>Wie schnell die Regelung einsetzt.</summary>
    public double AttackMs { get; }

    /// <summary>Wie schnell die Regelung wieder loslässt.</summary>
    public double ReleaseMs { get; }

    /// <summary>Aufholverstärkung nach der Kompression in dB.</summary>
    public double MakeupDb { get; }

    /// <summary>Gekoppelt: Alle Kanäle werden gemeinsam geregelt (stabiles Stereobild).</summary>
    public bool Linked { get; }

    public bool IsNeutral => MixPercent.All(m => m <= 0);

    public double MixFor(int channel) => MixPercent[Math.Min(channel, MixPercent.Count - 1)];
}
