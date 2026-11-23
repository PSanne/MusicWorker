using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Mixing;

/// <summary>
/// Mischt alle Spuren eines Projekts zu einem Stereo-Signal: Clips → Panorama → Fader (inkl. Gruppe)
/// → Summe; über Sends gelangt Signal in die Effekt-Busse (Hall, Echo), deren Effekt-Anteil zur Summe
/// zurückkommt → Master-Fader. Funktioniert blockweise für die Live-Wiedergabe (<see cref="Render"/>)
/// und am Stück für den Mixdown (<see cref="Mixdown"/>). Änderungen am Mischpult werden per
/// <see cref="Update"/> thread-sicher übernommen.
/// </summary>
public sealed class MixEngine
{
    private readonly int _sampleRate;
    private MixSnapshot _snapshot;
    private MixSnapshot? _appliedBusSnapshot;
    private long _position;

    private IBusProcessor[] _busProcessors = [];
    private float[] _trackPeaks = [];
    private float _masterPeakLeft;
    private float _masterPeakRight;

    private float[] _trackBuffer = [];
    private float[][] _busBuffers = [];
    private InsertChain?[] _trackInserts = [];
    private InsertChain? _masterInserts;
    private MasterLimiter? _limiter;

    public MixEngine(MixSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _sampleRate = snapshot.SampleRate;
    }

    public int SampleRate => _sampleRate;

    /// <summary>Aktuelle Leseposition in Frames.</summary>
    public long Position => Interlocked.Read(ref _position);

    /// <summary>Übernimmt einen neuen Zustand (z. B. Fader bewegt, Clip verschoben) – thread-sicher.</summary>
    public void Update(MixSnapshot snapshot) =>
        Volatile.Write(ref _snapshot, snapshot ?? throw new ArgumentNullException(nameof(snapshot)));

    /// <summary>Springt an eine andere Stelle.</summary>
    public void Seek(long frame) => Interlocked.Exchange(ref _position, Math.Max(0, frame));

    /// <summary>Spitzenpegel (0 … 1) einer Spur im zuletzt berechneten Block, nach dem Fader.</summary>
    public float GetTrackPeak(int track)
    {
        var peaks = Volatile.Read(ref _trackPeaks);
        return track >= 0 && track < peaks.Length ? Volatile.Read(ref peaks[track]) : 0;
    }

    /// <summary>Spitzenpegel (0 … 1) des Masters im zuletzt berechneten Block.</summary>
    public float GetMasterPeak(int channel) =>
        channel == 0 ? Volatile.Read(ref _masterPeakLeft) : Volatile.Read(ref _masterPeakRight);

    /// <summary>
    /// Berechnet die nächsten Frames ab <see cref="Position"/> in <paramref name="stereo"/>
    /// (verschachtelt L, R) und rückt die Position weiter. Nach dem Projektende entsteht Stille
    /// (bzw. das Ausklingen der Effekt-Busse).
    /// </summary>
    public void Render(Span<float> stereo)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        int frames = stereo.Length / 2;
        long position = Interlocked.Read(ref _position);
        stereo.Clear();

        PrepareBuffers(snapshot, frames);
        var tracks = snapshot.Tracks;
        bool anySolo = tracks.Any(t => t.Solo);
        var peaks = _trackPeaks;

        for (int t = 0; t < tracks.Count; t++)
        {
            var track = tracks[t];
            var group = track.GroupIndex >= 0 && track.GroupIndex < snapshot.Groups.Count
                ? snapshot.Groups[track.GroupIndex]
                : null;
            bool audible = !track.Mute && (!anySolo || track.Solo) && group is not { Mute: true };
            float gain = audible ? (float)MixMath.DbToGain(track.VolumeDb + (group?.VolumeDb ?? 0)) : 0f;
            if (gain == 0)
            {
                Volatile.Write(ref peaks[t], 0f);
                continue;
            }

            var buffer = _trackBuffer.AsSpan(0, frames * 2);
            buffer.Clear();
            bool hasAudio = false;
            foreach (var clip in track.Clips)
            {
                hasAudio |= AddClip(clip, position, frames, buffer, track.Pan);
            }

            if (!hasAudio && track.Inserts is null)
            {
                Volatile.Write(ref peaks[t], 0f);
                continue;
            }

            // Insert-Effekte (vor dem Fader). Auch ohne Clip im Block weiterrechnen, damit Filter sauber ausklingen.
            if (track.Inserts is { } inserts && !inserts.IsNeutral)
            {
                var chain = _trackInserts[t] ??= new InsertChain(_sampleRate, inserts);
                chain.Update(inserts);
                chain.Process(buffer);
            }
            else if (!hasAudio)
            {
                Volatile.Write(ref peaks[t], 0f);
                continue;
            }

            float peak = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                float v = buffer[i] * gain;
                buffer[i] = v;
                stereo[i] += v;
                peak = Math.Max(peak, Math.Abs(v));
            }

            Volatile.Write(ref peaks[t], peak);

            // Sends (nach dem Fader) in die Effekt-Busse
            var sends = track.SendLevels;
            if (sends is null)
            {
                continue;
            }

            for (int b = 0; b < _busBuffers.Length && b < sends.Count; b++)
            {
                float level = (float)Math.Clamp(sends[b], 0, 1);
                if (level <= 0)
                {
                    continue;
                }

                var bus = _busBuffers[b];
                for (int i = 0; i < buffer.Length; i++)
                {
                    bus[i] += buffer[i] * level;
                }
            }
        }

        // Effekt-Busse berechnen und ihren Effekt-Anteil zur Summe geben
        for (int b = 0; b < _busProcessors.Length; b++)
        {
            var bus = _busBuffers[b].AsSpan(0, frames * 2);
            _busProcessors[b].Process(bus);
            float returnGain = (float)MixMath.DbToGain(snapshot.Buses[b].ReturnDb);
            for (int i = 0; i < bus.Length; i++)
            {
                stereo[i] += bus[i] * returnGain;
            }
        }

        // Master: Insert-Effekte → Fader → Begrenzer
        if (snapshot.MasterInserts is { IsNeutral: false } masterInserts)
        {
            _masterInserts ??= new InsertChain(_sampleRate, masterInserts);
            _masterInserts.Update(masterInserts);
            _masterInserts.Process(stereo);
        }

        float master = (float)MixMath.DbToGain(snapshot.MasterDb);
        float peakLeft = 0, peakRight = 0;
        for (int i = 0; i < stereo.Length; i += 2)
        {
            stereo[i] *= master;
            stereo[i + 1] *= master;
            peakLeft = Math.Max(peakLeft, Math.Abs(stereo[i]));
            peakRight = Math.Max(peakRight, Math.Abs(stereo[i + 1]));
        }

        if (snapshot.MasterLimiter)
        {
            _limiter ??= new MasterLimiter(_sampleRate);
            _limiter.Process(stereo);
            peakLeft = Math.Min(peakLeft, 0.995f);
            peakRight = Math.Min(peakRight, 0.995f);
        }

        Volatile.Write(ref _masterPeakLeft, peakLeft);
        Volatile.Write(ref _masterPeakRight, peakRight);

        // Nur weiterrücken, wenn nicht inzwischen gesprungen wurde.
        Interlocked.CompareExchange(ref _position, position + frames, position);
    }

    /// <summary>Mischt das ganze Projekt am Stück (plus <paramref name="tailSeconds"/> zum Ausklingen).</summary>
    public static AudioBuffer Mixdown(MixSnapshot snapshot, double tailSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        long total = snapshot.Length + (long)Math.Round(Math.Max(0, tailSeconds) * snapshot.SampleRate);
        var output = new float[checked((int)(total * 2))];
        var engine = new MixEngine(snapshot);
        const int block = 4096;
        for (int offset = 0; offset < output.Length; offset += block * 2)
        {
            int length = Math.Min(block * 2, output.Length - offset);
            engine.Render(output.AsSpan(offset, length));
        }

        return new AudioBuffer(output, snapshot.SampleRate, 2);
    }

    /// <summary>Addiert den Anteil eines Clips im Block (mit Panorama) in den Spurpuffer.</summary>
    private static bool AddClip(MixClip clip, long position, int frames, Span<float> buffer, double pan)
    {
        long from = Math.Max(position, clip.StartFrame);
        long to = Math.Min(position + frames, clip.EndFrame);
        if (from >= to)
        {
            return false;
        }

        var audio = clip.Audio;
        var samples = audio.Samples;
        int channels = audio.Channels;
        var (left, right) = MixMath.PanGains(pan, Math.Min(channels, 2));
        bool fades = clip.FadeInFrames > 0 || clip.FadeOutFrames > 0;

        for (long frame = from; frame < to; frame++)
        {
            long inClip = frame - clip.StartFrame;
            int source = (int)inClip * channels;
            int target = (int)(frame - position) * 2;
            float fade = fades ? ClipTools.FadeGain(clip, inClip) : 1f;
            if (channels == 1)
            {
                float v = samples[source] * fade;
                buffer[target] += v * left;
                buffer[target + 1] += v * right;
            }
            else
            {
                buffer[target] += samples[source] * left * fade;
                buffer[target + 1] += samples[source + 1] * right * fade;
            }
        }

        return true;
    }

    /// <summary>Puffer und Bus-Effekte passend zum aktuellen Zustand bereitstellen.</summary>
    private void PrepareBuffers(MixSnapshot snapshot, int frames)
    {
        if (_trackBuffer.Length < frames * 2)
        {
            _trackBuffer = new float[frames * 2];
        }

        if (_trackPeaks.Length != snapshot.Tracks.Count)
        {
            Volatile.Write(ref _trackPeaks, new float[snapshot.Tracks.Count]);
            _trackInserts = new InsertChain?[snapshot.Tracks.Count];
        }

        bool busLayoutChanged = _busProcessors.Length != snapshot.Buses.Count ||
            _busProcessors.Where((p, i) => p.Effect != snapshot.Buses[i].Effect).Any();
        if (busLayoutChanged)
        {
            _busProcessors = snapshot.Buses.Select(b => CreateBusProcessor(b, _sampleRate)).ToArray();
            _appliedBusSnapshot = snapshot;
        }
        else if (!ReferenceEquals(_appliedBusSnapshot, snapshot))
        {
            for (int b = 0; b < _busProcessors.Length; b++)
            {
                _busProcessors[b].Update(snapshot.Buses[b]);
            }

            _appliedBusSnapshot = snapshot;
        }

        if (_busBuffers.Length != _busProcessors.Length || (_busBuffers.Length > 0 && _busBuffers[0].Length < frames * 2))
        {
            _busBuffers = _busProcessors.Select(_ => new float[frames * 2]).ToArray();
        }

        foreach (var bus in _busBuffers)
        {
            Array.Clear(bus);
        }
    }

    private static IBusProcessor CreateBusProcessor(MixBus bus, int sampleRate) => bus.Effect switch
    {
        BusEffect.Reverb => new ReverbBus(sampleRate, bus),
        _ => new DelayBus(sampleRate, bus),
    };

    private interface IBusProcessor
    {
        BusEffect Effect { get; }

        void Update(MixBus bus);

        void Process(Span<float> stereo);
    }

    /// <summary>Hall-Bus: liefert nur den Hall (100 %), ohne Originalsignal.</summary>
    private sealed class ReverbBus(int sampleRate, MixBus bus) : IBusProcessor
    {
        private readonly ReverbProcessor _processor = new(sampleRate, 2, Settings(bus), wetOnly: true);

        public BusEffect Effect => BusEffect.Reverb;

        public void Update(MixBus bus) => _processor.Update(Settings(bus));

        public void Process(Span<float> stereo) => _processor.Process(stereo);

        private static ReverbSettings Settings(MixBus bus)
        {
            var r = bus.Reverb ?? new ReverbSettings([100]);
            return new ReverbSettings([100], r.RoomSizePercent, r.DampingPercent);
        }
    }

    /// <summary>Echo-Bus: liefert nur die Wiederholungen (100 %), ohne Originalsignal.</summary>
    private sealed class DelayBus(int sampleRate, MixBus bus) : IBusProcessor
    {
        private readonly DelayProcessor _processor = new(sampleRate, 2, Settings(bus), wetOnly: true);

        public BusEffect Effect => BusEffect.Delay;

        public void Update(MixBus bus) => _processor.Update(Settings(bus));

        public void Process(Span<float> stereo) => _processor.Process(stereo);

        private static DelaySettings Settings(MixBus bus)
        {
            var d = bus.Delay ?? new DelaySettings([375], [100]);
            return new DelaySettings(d.TimeMs, [100], d.FeedbackPercent, d.DampingPercent, d.PingPong);
        }
    }
}
