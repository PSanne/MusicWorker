using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Mixing;

/// <summary>
/// Ein Audio-Stück auf einer Spur, beginnend bei <see cref="StartFrame"/> (Mono oder Stereo).
/// <see cref="FadeInFrames"/>/<see cref="FadeOutFrames"/> blenden ein bzw. aus (gleiche Leistung) –
/// so entstehen Überblendungen, wenn sich zwei Clips überlappen.
/// </summary>
public sealed record MixClip(AudioBuffer Audio, long StartFrame, long FadeInFrames = 0, long FadeOutFrames = 0)
{
    public long EndFrame => StartFrame + Audio.FrameCount;
}

/// <summary>
/// Insert-Effekte eines Kanalzugs (bzw. des Masters): Hochpass, 3-Band-EQ und ein Kompressor
/// mit nur einem Regler. Neutral, wenn alles auf 0 bzw. aus steht.
/// </summary>
/// <param name="LowDb">Tiefen (Kuhschwanz bei 120 Hz), ±12 dB.</param>
/// <param name="MidDb">Mitten (Glocke bei 1 kHz), ±12 dB.</param>
/// <param name="HighDb">Höhen (Kuhschwanz bei 8 kHz), ±12 dB.</param>
/// <param name="HighPassHz">Hochpass (20 Hz = aus).</param>
/// <param name="CompressionPercent">Kompression 0 … 100 % (Schwelle, Verhältnis und Aufholen werden daraus abgeleitet).</param>
public sealed record TrackInserts(
    double LowDb = 0,
    double MidDb = 0,
    double HighDb = 0,
    double HighPassHz = 20,
    double CompressionPercent = 0)
{
    public static TrackInserts Neutral { get; } = new();

    public bool IsNeutral => LowDb == 0 && MidDb == 0 && HighDb == 0 && HighPassHz <= 20.5 && CompressionPercent <= 0;
}

/// <summary>Eine Spur des Mischpults.</summary>
/// <param name="Clips">Die Clips der Spur.</param>
/// <param name="VolumeDb">Fader in dB (≤ -60 = stumm).</param>
/// <param name="Pan">Panorama von -1 (links) über 0 (Mitte) bis +1 (rechts).</param>
/// <param name="Mute">Stummgeschaltet.</param>
/// <param name="Solo">Solo: Gibt es mindestens eine Solo-Spur, sind nur Solo-Spuren zu hören.</param>
/// <param name="SendLevels">Pegel je Effekt-Bus, 0 … 1 (nach dem Fader abgegriffen).</param>
/// <param name="GroupIndex">Index der Gruppe (-1 = keine).</param>
/// <param name="Inserts">Insert-Effekte (vor dem Fader); null = keine.</param>
/// <param name="Automation">Hüllkurven (Lautstärke, Panorama, Sends, Hochpass); leere werden ignoriert.</param>
/// <param name="Ducking">Auto-Absenkung durch eine andere Spur; null = aus.</param>
public sealed record MixTrack(
    IReadOnlyList<MixClip> Clips,
    double VolumeDb = 0,
    double Pan = 0,
    bool Mute = false,
    bool Solo = false,
    IReadOnlyList<double>? SendLevels = null,
    int GroupIndex = -1,
    TrackInserts? Inserts = null,
    IReadOnlyList<AutomationLane>? Automation = null,
    DuckingSettings? Ducking = null)
{
    /// <summary>Die (nicht leere) Hüllkurve für ein Ziel oder null.</summary>
    public AutomationLane? LaneFor(AutomationTarget target)
    {
        if (Automation is null)
        {
            return null;
        }

        foreach (var lane in Automation)
        {
            if (lane.Target == target && !lane.IsEmpty)
            {
                return lane;
            }
        }

        return null;
    }
}

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
/// <param name="SampleRate">Abtastrate des Projekts.</param>
/// <param name="Tracks">Alle Spuren.</param>
/// <param name="Groups">Alle Gruppen (Spuren verweisen per Index darauf).</param>
/// <param name="Buses">Effekt-Busse (Sends der Spuren in derselben Reihenfolge).</param>
/// <param name="MasterDb">Master-Fader in dB.</param>
/// <param name="MasterInserts">Master-Effekte (EQ, Hochpass, Kompression) vor dem Master-Fader.</param>
/// <param name="MasterLimiter">Begrenzer am Ende: verhindert Übersteuerung der Summe.</param>
/// <param name="IsolatedTrack">Nur diese Spur erklingt (Einzelspur-Export); -1 = alle.</param>
public sealed record MixSnapshot(
    int SampleRate,
    IReadOnlyList<MixTrack> Tracks,
    IReadOnlyList<MixGroup> Groups,
    IReadOnlyList<MixBus> Buses,
    double MasterDb = 0,
    TrackInserts? MasterInserts = null,
    bool MasterLimiter = false,
    int IsolatedTrack = -1)
{
    /// <summary>Ende des letzten Clips in Frames.</summary>
    public long Length => Tracks.SelectMany(t => t.Clips).Select(c => c.EndFrame).DefaultIfEmpty(0).Max();
}

/// <summary>Werkzeuge für Clips auf einer Spur.</summary>
public static class ClipTools
{
    /// <summary>
    /// Legt Überblendungen fest: Überlappen sich zwei Clips einer Spur, blendet der erste über die
    /// Überlappung aus und der zweite ein.
    /// </summary>
    public static IReadOnlyList<MixClip> WithCrossfades(IEnumerable<MixClip> clips)
    {
        var sorted = clips.OrderBy(c => c.StartFrame).ToArray();
        for (int i = 0; i + 1 < sorted.Length; i++)
        {
            var a = sorted[i];
            var b = sorted[i + 1];
            long overlap = Math.Min(a.EndFrame - b.StartFrame, Math.Min(a.Audio.FrameCount, b.Audio.FrameCount));
            if (overlap > 0)
            {
                sorted[i] = a with { FadeOutFrames = Math.Max(a.FadeOutFrames, overlap) };
                sorted[i + 1] = b with { FadeInFrames = Math.Max(b.FadeInFrames, overlap) };
            }
        }

        return sorted;
    }

    /// <summary>Verstärkung durch Ein-/Ausblenden an einer Stelle des Clips (gleiche Leistung: sin/cos).</summary>
    public static float FadeGain(MixClip clip, long frameInClip)
    {
        double gain = 1;
        if (clip.FadeInFrames > 0 && frameInClip < clip.FadeInFrames)
        {
            gain *= Math.Sin((frameInClip + 0.5) / clip.FadeInFrames * Math.PI / 2);
        }

        long fromEnd = clip.Audio.FrameCount - frameInClip;
        if (clip.FadeOutFrames > 0 && fromEnd <= clip.FadeOutFrames)
        {
            gain *= Math.Sin((fromEnd - 0.5) / clip.FadeOutFrames * Math.PI / 2);
        }

        return (float)gain;
    }

    /// <summary>
    /// Fügt mehrere Clips (inkl. Lücken als Stille und Überblendungen) zu einem Audio zusammen.
    /// Sind Mono- und Stereo-Clips gemischt, wird Stereo daraus.
    /// </summary>
    public static (AudioBuffer Audio, long StartFrame) Merge(IReadOnlyList<MixClip> clips)
    {
        if (clips.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Clip ist erforderlich.", nameof(clips));
        }

        var faded = WithCrossfades(clips);
        long start = faded.Min(c => c.StartFrame);
        long end = faded.Max(c => c.EndFrame);
        int channels = faded.Max(c => c.Audio.Channels);
        int rate = faded[0].Audio.SampleRate;
        var output = new float[checked((int)(end - start) * channels)];

        foreach (var clip in faded)
        {
            var audio = clip.Audio;
            for (int f = 0; f < audio.FrameCount; f++)
            {
                float gain = FadeGain(clip, f);
                long target = (clip.StartFrame - start + f) * channels;
                for (int c = 0; c < channels; c++)
                {
                    output[target + c] += audio.GetSample(f, Math.Min(c, audio.Channels - 1)) * gain;
                }
            }
        }

        return (new AudioBuffer(output, rate, channels), start);
    }

    /// <summary>
    /// Findet den hörbaren Teil (erstes bis letztes Sample über <paramref name="thresholdDb"/>),
    /// mit etwas Luft davor und danach. Leer, wenn alles still ist.
    /// </summary>
    public static Editing.SelectionRange FindContent(AudioBuffer audio, double thresholdDb = -50, double paddingSeconds = 0.01)
    {
        float threshold = (float)Decibels.ToGain(thresholdDb);
        int first = -1;
        int last = -1;
        for (int f = 0; f < audio.FrameCount; f++)
        {
            for (int c = 0; c < audio.Channels; c++)
            {
                if (Math.Abs(audio.GetSample(f, c)) >= threshold)
                {
                    if (first < 0)
                    {
                        first = f;
                    }

                    last = f;
                    break;
                }
            }
        }

        if (first < 0)
        {
            return default;
        }

        int padding = (int)(paddingSeconds * audio.SampleRate);
        return new Editing.SelectionRange(Math.Max(0, first - padding), Math.Min(audio.FrameCount, last + 1 + padding));
    }
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
