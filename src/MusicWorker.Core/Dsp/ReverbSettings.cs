namespace MusicWorker.Core.Dsp;

/// <summary>Einstellungen des Halls: Stärke pro Kanal sowie Raumgröße und Dämpfung (jeweils 0–100 %).</summary>
public sealed class ReverbSettings
{
    public ReverbSettings(IReadOnlyList<double> amountPercent, double roomSizePercent = 50, double dampingPercent = 50)
    {
        ArgumentNullException.ThrowIfNull(amountPercent);
        if (amountPercent.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Kanal ist erforderlich.", nameof(amountPercent));
        }

        AmountPercent = amountPercent.Select(a => Math.Clamp(a, 0, 100)).ToArray();
        RoomSizePercent = Math.Clamp(roomSizePercent, 0, 100);
        DampingPercent = Math.Clamp(dampingPercent, 0, 100);
    }

    /// <summary>Kein Hall.</summary>
    public static ReverbSettings Off { get; } = new([0]);

    /// <summary>Hall-Stärke je Kanal in % (bei weniger Werten als Kanälen gilt der letzte für den Rest).</summary>
    public IReadOnlyList<double> AmountPercent { get; }

    public double RoomSizePercent { get; }

    public double DampingPercent { get; }

    public bool IsNeutral => AmountPercent.All(a => a <= 0);

    /// <summary>Ungefähre Nachklingzeit – so viel Stille wird beim Übernehmen ans Ende angehängt.</summary>
    public double TailSeconds => 1.0 + 4.0 * Math.Pow(RoomSizePercent / 100, 2);

    public double AmountFor(int channel) => AmountPercent[Math.Min(channel, AmountPercent.Count - 1)];
}
