namespace MusicWorker.Core.Mixing;

/// <summary>
/// Einzelspuren („Stems“) für die Weiterarbeit in anderen Programmen: Jede Spur wird für sich berechnet –
/// gleich lang wie das Projekt, damit alle Dateien zusammen wieder genau passen.
/// </summary>
public static class Stems
{
    /// <summary>
    /// Mischpult-Zustand, in dem nur <paramref name="trackIndex"/> erklingt.
    /// <paramref name="raw"/> = nur die Clips (ohne Fader, Panorama, Effekte und Sends);
    /// sonst wie im Mix, aber ohne Summenbearbeitung – so ergeben alle Einzelspuren zusammen wieder den Mix.
    /// </summary>
    public static MixSnapshot ForTrack(MixSnapshot snapshot, int trackIndex, bool raw)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(trackIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(trackIndex, snapshot.Tracks.Count);
        if (raw)
        {
            return snapshot with
            {
                Tracks = [new MixTrack(snapshot.Tracks[trackIndex].Clips)],
                Groups = [],
                MasterDb = 0,
                MasterInserts = null,
                MasterLimiter = false,
                IsolatedTrack = -1,
            };
        }

        var tracks = snapshot.Tracks.Select((t, i) => i == trackIndex ? t with { Mute = false, Solo = false } : t with { Solo = false }).ToArray();
        return snapshot with
        {
            Tracks = tracks,
            MasterDb = 0,
            MasterInserts = null,
            MasterLimiter = false,
            IsolatedTrack = trackIndex,
        };
    }
}
