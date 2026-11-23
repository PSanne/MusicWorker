namespace MusicWorker.Core.Dsp;

/// <summary>Einstellungen des Echos: Zeit und Stärke pro Kanal, Rückkopplung, Dämpfung, Ping-Pong.</summary>
public sealed class DelaySettings
{
    public const double MaxTimeMs = 2000;

    public DelaySettings(
        IReadOnlyList<double> timeMs,
        IReadOnlyList<double> mixPercent,
        double feedbackPercent = 35,
        double dampingPercent = 30,
        bool pingPong = false)
    {
        ArgumentNullException.ThrowIfNull(timeMs);
        ArgumentNullException.ThrowIfNull(mixPercent);
        if (timeMs.Count == 0 || mixPercent.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Kanal ist erforderlich.");
        }

        TimeMs = timeMs.Select(t => Math.Clamp(t, 1, MaxTimeMs)).ToArray();
        MixPercent = mixPercent.Select(m => Math.Clamp(m, 0, 100)).ToArray();
        FeedbackPercent = Math.Clamp(feedbackPercent, 0, 90);
        DampingPercent = Math.Clamp(dampingPercent, 0, 100);
        PingPong = pingPong;
    }

    /// <summary>Kein Echo.</summary>
    public static DelaySettings Off { get; } = new([350], [0]);

    public IReadOnlyList<double> TimeMs { get; }

    public IReadOnlyList<double> MixPercent { get; }

    /// <summary>Anteil, der wieder ins Echo zurückgeführt wird – bestimmt die Zahl der Wiederholungen.</summary>
    public double FeedbackPercent { get; }

    /// <summary>Jede Wiederholung wird dumpfer (Höhen werden gedämpft), wie bei einem Bandecho.</summary>
    public double DampingPercent { get; }

    /// <summary>Wiederholungen springen zwischen links und rechts hin und her.</summary>
    public bool PingPong { get; }

    public bool IsNeutral => MixPercent.All(m => m <= 0);

    public double TimeFor(int channel) => TimeMs[Math.Min(channel, TimeMs.Count - 1)];

    public double MixFor(int channel) => MixPercent[Math.Min(channel, MixPercent.Count - 1)];

    /// <summary>Zeit, bis die Wiederholungen auf etwa -60 dB abgeklungen sind (höchstens 15 s).</summary>
    public double TailSeconds
    {
        get
        {
            double longest = TimeMs.Max() / 1000;
            double feedback = FeedbackPercent / 100;
            double repeats = feedback > 0.001 ? Math.Log(0.001) / Math.Log(feedback) : 0;
            return Math.Min(15, longest * (1 + repeats) * (PingPong ? 2 : 1));
        }
    }
}
