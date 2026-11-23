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

    /// <summary>Hüllkurven und Panorama werden alle 64 Frames (≈ 1,5 ms) ausgewertet und dazwischen übergeblendet.</summary>
    private const int Step = 64;

    private float[][] _trackBuffers = [];
    private bool[] _active = [];
    private bool[] _ducked = [];
    private Ducker?[] _duckers = [];
    private float[][] _duckGains = [];
    private float[] _ramp = [];
    private float[] _panMono = [];
    private float[] _panStereo = [];
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

        // 1. Jede Spur: Clips (mit Panorama) → Insert-Effekte → Fader und Lautstärke-Hüllkurve
        for (int t = 0; t < tracks.Count; t++)
        {
            _active[t] = false;
            var track = tracks[t];
            var group = track.GroupIndex >= 0 && track.GroupIndex < snapshot.Groups.Count
                ? snapshot.Groups[track.GroupIndex]
                : null;
            bool audible = !track.Mute && (!anySolo || track.Solo) && group is not { Mute: true };
            double faderDb = track.VolumeDb + (group?.VolumeDb ?? 0);
            if (!audible || MixMath.DbToGain(faderDb) == 0)
            {
                continue;
            }

            var buffer = _trackBuffers[t].AsSpan(0, frames * 2);
            buffer.Clear();
            FillPanGains(track, position, frames);
            bool hasAudio = false;
            foreach (var clip in track.Clips)
            {
                hasAudio |= AddClip(clip, position, frames, buffer, _panMono, _panStereo);
            }

            var highPassLane = track.LaneFor(AutomationTarget.HighPass);
            bool hasInserts = track.Inserts is { IsNeutral: false } || highPassLane is not null;
            if (!hasAudio && !hasInserts)
            {
                continue;
            }

            // Insert-Effekte (vor dem Fader). Auch ohne Clip im Block weiterrechnen, damit Filter sauber ausklingen.
            if (hasInserts)
            {
                ProcessInserts(t, track, highPassLane, position, buffer, frames);
            }

            ApplyGain(buffer, faderDb, track.LaneFor(AutomationTarget.Volume), position, frames);
            _active[t] = true;
        }

        // 2. Auto-Absenkung: erst alle Absenkungen aus den (noch unabgesenkten) Quellspuren berechnen, dann anwenden
        for (int t = 0; t < tracks.Count; t++)
        {
            if (tracks[t].Ducking is { } ducking && ducking.SourceTrack >= 0 && ducking.SourceTrack < tracks.Count && ducking.SourceTrack != t)
            {
                var ducker = _duckers[t] ??= new Ducker(_sampleRate);
                var source = _active[ducking.SourceTrack] ? _trackBuffers[ducking.SourceTrack].AsSpan(0, frames * 2) : [];
                _duckGains[t] ??= new float[_trackBuffers[t].Length / 2];
                _ducked[t] = ducker.Compute(source, _duckGains[t].AsSpan(0, frames), ducking);
            }
            else
            {
                _duckers[t] = null;
                _ducked[t] = false;
            }
        }

        // 3. Summe, Pegel und Sends (nach dem Fader) in die Effekt-Busse
        for (int t = 0; t < tracks.Count; t++)
        {
            if (!_active[t])
            {
                Volatile.Write(ref peaks[t], 0f);
                continue;
            }

            var track = tracks[t];
            var buffer = _trackBuffers[t].AsSpan(0, frames * 2);
            if (_ducked[t])
            {
                var gains = _duckGains[t];
                for (int i = 0; i < frames; i++)
                {
                    buffer[2 * i] *= gains[i];
                    buffer[2 * i + 1] *= gains[i];
                }
            }

            float peak = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                stereo[i] += buffer[i];
                peak = Math.Max(peak, Math.Abs(buffer[i]));
            }

            Volatile.Write(ref peaks[t], peak);

            for (int b = 0; b < _busBuffers.Length; b++)
            {
                var lane = track.LaneFor(snapshot.Buses[b].Effect == BusEffect.Reverb ? AutomationTarget.SendHall : AutomationTarget.SendEcho);
                double level = track.SendLevels is { } sends && b < sends.Count ? Math.Clamp(sends[b], 0, 1) : 0;
                if (lane is null && level <= 0)
                {
                    continue;
                }

                var bus = _busBuffers[b];
                if (lane is null)
                {
                    float constant = (float)level;
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        bus[i] += buffer[i] * constant;
                    }
                }
                else
                {
                    FillRamp(lane, position, frames, static v => (float)Math.Clamp(v, 0, 1));
                    for (int i = 0; i < frames; i++)
                    {
                        float g = RampAt(i);
                        bus[2 * i] += buffer[2 * i] * g;
                        bus[2 * i + 1] += buffer[2 * i + 1] * g;
                    }
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

    /// <summary>
    /// Addiert den Anteil eines Clips im Block in den Spurpuffer. Das Panorama kommt aus
    /// <paramref name="panMono"/>/<paramref name="panStereo"/> (L, R je Abschnitt von <see cref="Step"/> Frames).
    /// </summary>
    private static bool AddClip(MixClip clip, long position, int frames, Span<float> buffer, float[] panMono, float[] panStereo)
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
        var pan = channels == 1 ? panMono : panStereo;
        bool fades = clip.FadeInFrames > 0 || clip.FadeOutFrames > 0;

        for (long frame = from; frame < to; frame++)
        {
            long inClip = frame - clip.StartFrame;
            int source = (int)inClip * channels;
            int local = (int)(frame - position);
            int target = local * 2;
            int step = (local / Step) * 2;
            float left = pan[step];
            float right = pan[step + 1];
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

    /// <summary>Panorama-Verstärkungen je Abschnitt – fest oder aus der Panorama-Hüllkurve.</summary>
    private void FillPanGains(MixTrack track, long position, int frames)
    {
        var lane = track.LaneFor(AutomationTarget.Pan);
        int steps = frames / Step + 1;
        for (int k = 0; k < steps; k++)
        {
            double pan = lane is null ? track.Pan : lane.ValueAt(position + (long)k * Step + Step / 2);
            if (lane is null && k > 0)
            {
                _panMono[2 * k] = _panMono[0];
                _panMono[2 * k + 1] = _panMono[1];
                _panStereo[2 * k] = _panStereo[0];
                _panStereo[2 * k + 1] = _panStereo[1];
                continue;
            }

            (_panMono[2 * k], _panMono[2 * k + 1]) = MixMath.PanGains(pan, 1);
            (_panStereo[2 * k], _panStereo[2 * k + 1]) = MixMath.PanGains(pan, 2);
        }
    }

    /// <summary>Insert-Effekte; mit Hochpass-Hüllkurve wird die Filterfrequenz alle 256 Frames nachgeführt.</summary>
    private void ProcessInserts(int t, MixTrack track, AutomationLane? highPassLane, long position, Span<float> buffer, int frames)
    {
        var inserts = track.Inserts ?? TrackInserts.Neutral;
        if (highPassLane is null)
        {
            var chain = _trackInserts[t] ??= new InsertChain(_sampleRate, inserts);
            chain.Update(inserts);
            chain.Process(buffer);
            return;
        }

        const int filterStep = 256;
        for (int f = 0; f < frames; f += filterStep)
        {
            int n = Math.Min(filterStep, frames - f);
            var current = inserts with { HighPassHz = Math.Round(highPassLane.ValueAt(position + f + n / 2), 1) };
            var chain = _trackInserts[t] ??= new InsertChain(_sampleRate, current);
            chain.Update(current);
            chain.Process(buffer.Slice(f * 2, n * 2));
        }
    }

    /// <summary>Fader und Lautstärke-Hüllkurve (in dB zusätzlich zum Fader, ohne Knacksen weich übergeblendet).</summary>
    private void ApplyGain(Span<float> buffer, double faderDb, AutomationLane? lane, long position, int frames)
    {
        if (lane is null)
        {
            float gain = (float)MixMath.DbToGain(faderDb);
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] *= gain;
            }

            return;
        }

        FillRamp(lane, position, frames, v => (float)MixMath.DbToGain(faderDb + v));
        for (int i = 0; i < frames; i++)
        {
            float g = RampAt(i);
            buffer[2 * i] *= g;
            buffer[2 * i + 1] *= g;
        }
    }

    /// <summary>Hüllkurven-Werte an den Abschnittsgrenzen (alle <see cref="Step"/> Frames) berechnen.</summary>
    private void FillRamp(AutomationLane lane, long position, int frames, Func<double, float> map)
    {
        int points = frames / Step + 2;
        for (int k = 0; k < points; k++)
        {
            _ramp[k] = map(lane.ValueAt(position + (long)k * Step));
        }
    }

    /// <summary>Linear zwischen den Abschnittsgrenzen übergeblendeter Wert für Frame <paramref name="i"/> des Blocks.</summary>
    private float RampAt(int i)
    {
        int k = i / Step;
        float frac = (i - k * Step) / (float)Step;
        return _ramp[k] + (_ramp[k + 1] - _ramp[k]) * frac;
    }

    /// <summary>Puffer und Bus-Effekte passend zum aktuellen Zustand bereitstellen.</summary>
    private void PrepareBuffers(MixSnapshot snapshot, int frames)
    {
        int trackCount = snapshot.Tracks.Count;
        if (_trackPeaks.Length != trackCount)
        {
            Volatile.Write(ref _trackPeaks, new float[trackCount]);
            _trackInserts = new InsertChain?[trackCount];
            _trackBuffers = new float[trackCount][];
            _active = new bool[trackCount];
            _ducked = new bool[trackCount];
            _duckers = new Ducker?[trackCount];
            _duckGains = new float[trackCount][];
        }

        for (int t = 0; t < trackCount; t++)
        {
            if (_trackBuffers[t] is null || _trackBuffers[t].Length < frames * 2)
            {
                _trackBuffers[t] = new float[frames * 2];
                _duckGains[t] = null!;
            }
        }

        int steps = frames / Step + 2;
        if (_ramp.Length < steps)
        {
            _ramp = new float[steps];
            _panMono = new float[steps * 2];
            _panStereo = new float[steps * 2];
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

    /// <summary>
    /// Auto-Absenkung: Ein Pegelfolger misst die Quellspur; liegt sie über der Schwelle, wird um
    /// <see cref="DuckingSettings.AmountDb"/> abgesenkt (mit Einschwing- und Rückkehrzeit).
    /// </summary>
    private sealed class Ducker(int sampleRate)
    {
        private readonly double _detectAttack = Coefficient(5, sampleRate);
        private readonly double _detectRelease = Coefficient(150, sampleRate);
        private double _envelope;
        private double _reductionDb;

        /// <summary>Berechnet die Verstärkung je Frame; false, wenn nichts abgesenkt wird.</summary>
        public bool Compute(ReadOnlySpan<float> source, Span<float> gains, DuckingSettings settings)
        {
            double threshold = Decibels.ToGain(settings.ThresholdDb);
            double attack = Coefficient(Math.Max(1, settings.AttackMs), sampleRate);
            double release = Coefficient(Math.Max(1, settings.ReleaseMs), sampleRate);
            double amount = Math.Max(0, settings.AmountDb);
            bool any = false;
            for (int i = 0; i < gains.Length; i++)
            {
                double x = source.IsEmpty ? 0 : Math.Max(Math.Abs(source[2 * i]), Math.Abs(source[2 * i + 1]));
                _envelope = x > _envelope ? x + _detectAttack * (_envelope - x) : x + _detectRelease * (_envelope - x);
                double target = _envelope > threshold ? amount : 0;
                _reductionDb = target > _reductionDb
                    ? target + attack * (_reductionDb - target)
                    : target + release * (_reductionDb - target);
                if (_reductionDb < 0.01)
                {
                    gains[i] = 1f;
                    continue;
                }

                gains[i] = (float)Decibels.ToGain(-_reductionDb);
                any = true;
            }

            return any;
        }

        private static double Coefficient(double ms, int rate) => Math.Exp(-1 / (ms / 1000 * rate));
    }
}
