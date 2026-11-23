using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Mixing;

/// <summary>Ein Audio-Stück auf einer Spur, beginnend bei <see cref="StartFrame"/> (Mono oder Stereo).</summary>
public sealed record MixClip(AudioBuffer Audio, long StartFrame)
{
    public long EndFrame => StartFrame + Audio.FrameCount;
}

/// <summary>Eine Spur des Mischpults.</summary>
/// <param name="Clips">Die Clips der Spur.</param>
/// <param name="VolumeDb">Fader in dB (≤ -60 = stumm).</param>
/// <param name="Pan">Panorama von -1 (links) über 0 (Mitte) bis +1 (rechts).</param>
/// <param name="Mute">Stummgeschaltet.</param>
/// <param name="Solo">Solo: Gibt es mindestens eine Solo-Spur, sind nur Solo-Spuren zu hören.</param>
/// <param name="SendLevels">Pegel je Effekt-Bus, 0 … 1 (nach dem Fader abgegriffen).</param>
/// <param name="GroupIndex">Index der Gruppe (-1 = keine).</param>
public sealed record MixTrack(
    IReadOnlyList<MixClip> Clips,
    double VolumeDb = 0,
    double Pan = 0,
    bool Mute = false,
    bool Solo = false,
    IReadOnlyList<double>? SendLevels = null,
    int GroupIndex = -1);

/// <summary>Eine Gruppe: Ihr Fader und Mute wirken auf alle zugeordneten Spuren.</summary>
public sealed record MixGroup(double VolumeDb = 0, bool Mute = false);

/// <summary>Art des Effekts auf einem Effekt-Bus.</summary>
public enum BusEffect
{
    Reverb,
    Delay,
}

/// <summary>Ein Effekt-Bus: Spuren schicken über Sends Signal hinein, der Effekt-Anteil kommt zum Master zurück.</summary>
public sealed record MixBus(BusEffect Effect, double ReturnDb = 0, ReverbSettings? Reverb = null, DelaySettings? Delay = null);

/// <summary>Kompletter, unveränderlicher Zustand des Mischpults – wird bei jeder Änderung neu erzeugt.</summary>
public sealed record MixSnapshot(
    int SampleRate,
    IReadOnlyList<MixTrack> Tracks,
    IReadOnlyList<MixGroup> Groups,
    IReadOnlyList<MixBus> Buses,
    double MasterDb = 0)
{
    /// <summary>Ende des letzten Clips in Frames.</summary>
    public long Length => Tracks.SelectMany(t => t.Clips).Select(c => c.EndFrame).DefaultIfEmpty(0).Max();
}

/// <summary>Hilfsrechnungen für Fader und Panorama.</summary>
public static class MixMath
{
    /// <summary>Unterhalb dieses Werts gilt ein Fader als ganz zu.</summary>
    public const double SilenceDb = -60;

    public static double DbToGain(double db) => db <= SilenceDb ? 0 : Decibels.ToGain(db);

    /// <summary>
    /// Verstärkung links/rechts für ein Panorama: Mono-Quellen mit gleichbleibender Leistung
    /// (-3 dB in der Mitte), Stereo-Quellen als Balance (die Gegenseite wird leiser).
    /// </summary>
    public static (float Left, float Right) PanGains(double pan, int sourceChannels)
    {
        pan = Math.Clamp(pan, -1, 1);
        if (sourceChannels == 1)
        {
            double angle = (pan + 1) * Math.PI / 4;
            return ((float)Math.Cos(angle), (float)Math.Sin(angle));
        }

        return ((float)(pan > 0 ? 1 - pan : 1), (float)(pan < 0 ? 1 + pan : 1));
    }
}
