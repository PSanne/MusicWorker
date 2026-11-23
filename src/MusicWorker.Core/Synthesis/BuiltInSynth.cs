using MusicWorker.Core.Notes;

namespace MusicWorker.Core.Synthesis;

/// <summary>
/// Eingebauter, polyphoner Synthesizer mit zehn Klängen – ganz ohne Sample-Dateien:
/// Klavier (additiv mit Saiten-Inharmonie), E-Piano und Glocken (FM-Synthese), Orgel (Zugriegel),
/// Streicher, Flächenklang, Bass und Lead (subtraktiv: Sägezahn/Rechteck + Filter), Gitarre
/// (Karplus-Strong-Saitenmodell) und ein Schlagzeug nach General-MIDI-Belegung.
/// </summary>
public sealed class BuiltInSynth : IInstrumentEngine
{
    private const int MaxVoices = 48;

    private readonly BuiltInInstrument _instrument;
    private readonly List<SynthVoice> _voices = [];
    private readonly object _lock = new();
    private uint _noiseSeed = 0x1234567;

    public BuiltInSynth(BuiltInInstrument instrument, int sampleRate)
    {
        _instrument = instrument;
        SampleRate = sampleRate;
    }

    public int SampleRate { get; }

    public int ActiveVoices
    {
        get
        {
            lock (_lock)
            {
                return _voices.Count;
            }
        }
    }

    public void NoteOn(int pitch, int velocity)
    {
        pitch = Math.Clamp(pitch, 0, 127);
        velocity = Math.Clamp(velocity, 1, 127);
        lock (_lock)
        {
            if (_instrument != BuiltInInstrument.Drums)
            {
                foreach (var playing in _voices.Where(v => v.Pitch == pitch && !v.Released))
                {
                    playing.Release(); // gleiche Taste erneut: alte Note ausklingen lassen
                }
            }

            if (_voices.Count >= MaxVoices)
            {
                var oldest = _voices.OrderByDescending(v => v.Released).ThenBy(v => v.Started).First();
                _voices.Remove(oldest);
            }

            _noiseSeed = _noiseSeed * 1664525 + 1013904223;
            _voices.Add(Create(pitch, velocity, _noiseSeed));
        }
    }

    public void NoteOff(int pitch)
    {
        lock (_lock)
        {
            foreach (var voice in _voices.Where(v => v.Pitch == pitch && !v.Released))
            {
                voice.Release();
            }
        }
    }

    public void AllNotesOff()
    {
        lock (_lock)
        {
            foreach (var voice in _voices)
            {
                voice.Release();
            }
        }
    }

    public void Render(Span<float> stereo)
    {
        stereo.Clear();
        lock (_lock)
        {
            for (int i = _voices.Count - 1; i >= 0; i--)
            {
                var voice = _voices[i];
                voice.Render(stereo);
                if (voice.Finished)
                {
                    _voices.RemoveAt(i);
                }
            }
        }
    }

    private SynthVoice Create(int pitch, int velocity, uint seed) => _instrument switch
    {
        BuiltInInstrument.Piano => new PianoVoice(SampleRate, pitch, velocity, seed),
        BuiltInInstrument.ElectricPiano => new FmVoice(SampleRate, pitch, velocity, FmVoice.ElectricPiano),
        BuiltInInstrument.Bells => new FmVoice(SampleRate, pitch, velocity, FmVoice.Bell),
        BuiltInInstrument.Organ => new OrganVoice(SampleRate, pitch, velocity),
        BuiltInInstrument.Strings => new AnalogVoice(SampleRate, pitch, velocity, AnalogVoice.Strings),
        BuiltInInstrument.Pad => new AnalogVoice(SampleRate, pitch, velocity, AnalogVoice.Pad),
        BuiltInInstrument.Bass => new AnalogVoice(SampleRate, pitch, velocity, AnalogVoice.Bass),
        BuiltInInstrument.Lead => new AnalogVoice(SampleRate, pitch, velocity, AnalogVoice.Lead),
        BuiltInInstrument.Guitar => new PluckVoice(SampleRate, pitch, velocity, seed),
        _ => new DrumVoice(SampleRate, pitch, velocity, seed),
    };
}

/// <summary>Grundgerüst einer Stimme: Tonhöhe, Panorama, Lautstärke nach Anschlag, Ende.</summary>
internal abstract class SynthVoice
{
    private static long _counter;

    protected SynthVoice(int sampleRate, int pitch, int velocity, double pan)
    {
        SampleRate = sampleRate;
        Pitch = pitch;
        Velocity = velocity;
        Frequency = Note.Frequency(pitch);
        Started = Interlocked.Increment(ref _counter);
        Gain = 0.22 * Math.Pow(velocity / 127.0, 1.6);
        double angle = (Math.Clamp(pan, -1, 1) + 1) * Math.PI / 4;
        PanLeft = (float)(Math.Cos(angle) * Math.Sqrt(2));
        PanRight = (float)(Math.Sin(angle) * Math.Sqrt(2));
    }

    public int Pitch { get; }

    public long Started { get; }

    public bool Released { get; private set; }

    public bool Finished { get; protected set; }

    protected int SampleRate { get; }

    protected int Velocity { get; }

    protected double Frequency { get; }

    protected double Gain { get; }

    protected float PanLeft { get; }

    protected float PanRight { get; }

    public virtual void Release() => Released = true;

    /// <summary>Addiert die nächsten Frames der Stimme.</summary>
    public void Render(Span<float> stereo)
    {
        int frames = stereo.Length / 2;
        for (int i = 0; i < frames && !Finished; i++)
        {
            float v = Next();
            stereo[2 * i] += v * PanLeft;
            stereo[2 * i + 1] += v * PanRight;
        }
    }

    /// <summary>Nächstes Sample (mono).</summary>
    protected abstract float Next();

    /// <summary>Panorama nach Tonhöhe: tiefe Töne etwas links, hohe etwas rechts (wie am Klavier).</summary>
    protected static double PitchPan(int pitch, double width) => Math.Clamp((pitch - 60) / 36.0, -1, 1) * width;
}

/// <summary>ADSR-Hüllkurve: Anstieg (linear), Abfall und Ausklang (exponentiell).</summary>
internal sealed class Envelope
{
    private readonly double _attackStep;
    private readonly double _decay;
    private readonly double _sustain;
    private double _release;
    private int _stage; // 0 Anstieg, 1 Abfall/Halten, 2 Ausklang

    public Envelope(int sampleRate, double attack, double decay, double sustain, double release)
    {
        _attackStep = 1 / Math.Max(1, attack * sampleRate);
        _decay = decay <= 0 ? 0 : Math.Exp(-1 / (decay * sampleRate / 4.6)); // ≈ -40 dB nach „decay“
        _sustain = sustain;
        _release = Math.Exp(-1 / (Math.Max(0.005, release) * sampleRate / 6.9)); // ≈ -60 dB nach „release“
        SampleRate = sampleRate;
    }

    public double Level { get; private set; }

    public bool Done => _stage == 2 && Level < 1e-4;

    private int SampleRate { get; }

    public void Release(double? seconds = null)
    {
        _stage = 2;
        if (seconds is { } s)
        {
            _release = Math.Exp(-1 / (Math.Max(0.005, s) * SampleRate / 6.9));
        }
    }

    public double Next()
    {
        switch (_stage)
        {
            case 0:
                Level += _attackStep;
                if (Level >= 1)
                {
                    Level = 1;
                    _stage = 1;
                }

                break;
            case 1:
                Level = _decay == 0 ? _sustain : _sustain + (Level - _sustain) * _decay;
                break;
            default:
                Level *= _release;
                break;
        }

        return Level;
    }
}

/// <summary>Schnelle Sinus-Tabelle (Phase 0 … 1).</summary>
internal static class Wave
{
    private const int Size = 4096;
    private static readonly float[] Table = Enumerable.Range(0, Size + 1).Select(i => (float)Math.Sin(2 * Math.PI * i / Size)).ToArray();

    public static float Sin(double phase)
    {
        phase -= Math.Floor(phase);
        double index = phase * Size;
        int i = (int)index;
        float frac = (float)(index - i);
        return Table[i] + (Table[i + 1] - Table[i]) * frac;
    }

    /// <summary>Bandbegrenzter Sägezahn (PolyBLEP), Phase 0 … 1, Schritt <paramref name="dt"/>.</summary>
    public static double Saw(double phase, double dt) => 2 * phase - 1 - PolyBlep(phase, dt);

    /// <summary>Bandbegrenztes Rechteck mit Pulsbreite <paramref name="width"/>.</summary>
    public static double Pulse(double phase, double dt, double width)
    {
        double second = phase + (1 - width);
        second -= Math.Floor(second);
        return (phase < width ? 1 : -1) + PolyBlep(phase, dt) - PolyBlep(second, dt);
    }

    private static double PolyBlep(double t, double dt)
    {
        if (t < dt)
        {
            t /= dt;
            return t + t - t * t - 1;
        }

        if (t > 1 - dt)
        {
            t = (t - 1) / dt;
            return t * t + t + t + 1;
        }

        return 0;
    }
}

/// <summary>Resonanzfähiger Tiefpass (State-Variable-Filter, TPT-Form) – stabil auch bei schneller Modulation.</summary>
internal sealed class LowPass(int sampleRate)
{
    private double _ic1;
    private double _ic2;
    private double _a1;
    private double _a2;
    private double _a3;

    public void Set(double cutoff, double resonance)
    {
        double g = Math.Tan(Math.PI * Math.Clamp(cutoff, 20, sampleRate * 0.45) / sampleRate);
        double k = 2 - 2 * Math.Clamp(resonance, 0, 0.95);
        _a1 = 1 / (1 + g * (g + k));
        _a2 = g * _a1;
        _a3 = g * _a2;
    }

    public double Process(double x)
    {
        double v3 = x - _ic2;
        double v1 = _a1 * _ic1 + _a2 * v3;
        double v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
        _ic1 = 2 * v1 - _ic1;
        _ic2 = 2 * v2 - _ic2;
        return v2;
    }
}

/// <summary>Klavier: additive Teiltöne mit leichter Inharmonie, höhere klingen schneller ab; kurzes Hammergeräusch.</summary>
internal sealed class PianoVoice : SynthVoice
{
    private readonly double[] _phases;
    private readonly double[] _steps;
    private readonly double[] _amps;
    private readonly double[] _decays;
    private readonly Envelope _envelope;
    private uint _noise;
    private int _hammer;
    private double _hammerLevel;
    private double _hammerFilter;

    public PianoVoice(int sampleRate, int pitch, int velocity, uint seed)
        : base(sampleRate, pitch, velocity, PitchPan(pitch, 0.35))
    {
        _noise = seed | 1;
        double brightness = 0.45 + 0.55 * velocity / 127.0;
        double sustainSeconds = Math.Clamp(9 - (pitch - 21) * 0.09, 1.2, 9); // tiefe Saiten klingen länger
        const double inharmonicity = 0.0004;
        var partials = Enumerable.Range(1, 12)
            .Select(k => (K: k, F: Frequency * k * Math.Sqrt(1 + inharmonicity * k * k)))
            .Where(p => p.F < sampleRate * 0.45)
            .ToArray();
        _phases = new double[partials.Length];
        _steps = partials.Select(p => p.F / sampleRate).ToArray();
        _amps = partials.Select(p => Math.Pow(brightness, p.K - 1) / Math.Pow(p.K, 1.1) * (p.K == 1 ? 1 : 0.7)).ToArray();
        _decays = partials.Select(p => Math.Exp(-6.9 / (sustainSeconds / (1 + 0.35 * (p.K - 1)) * sampleRate))).ToArray();
        double norm = 1 / Math.Max(1, _amps.Sum() * 0.6);
        for (int i = 0; i < _amps.Length; i++)
        {
            _amps[i] *= norm;
        }

        _envelope = new Envelope(sampleRate, 0.002, 0, 1, 0.25);
        _hammer = (int)(0.006 * sampleRate);
        _hammerLevel = 0.15 * velocity / 127.0;
    }

    public override void Release()
    {
        base.Release();
        _envelope.Release(Pitch > 88 ? 1.5 : 0.25); // ganz oben gibt es keine Dämpfer
    }

    protected override float Next()
    {
        double sum = 0;
        double total = 0;
        for (int k = 0; k < _phases.Length; k++)
        {
            sum += Wave.Sin(_phases[k]) * _amps[k];
            total += _amps[k];
            _phases[k] += _steps[k];
            if (_phases[k] >= 1)
            {
                _phases[k] -= 1;
            }

            _amps[k] *= _decays[k];
        }

        if (_hammer > 0)
        {
            _noise = _noise * 1664525 + 1013904223;
            double white = (_noise >> 8) / 8388608.0 - 1;
            _hammerFilter += 0.3 * (white - _hammerFilter);
            sum += _hammerFilter * _hammerLevel;
            _hammerLevel *= 0.995;
            _hammer--;
        }

        double env = _envelope.Next();
        if (_envelope.Done || (total < 2e-4 && _hammer <= 0))
        {
            Finished = true;
        }

        return (float)(sum * env * Gain * 1.0);
    }
}

/// <summary>FM-Synthese (zwei Operatoren): E-Piano und Glocken.</summary>
internal sealed class FmVoice : SynthVoice
{
    public static readonly FmPatch ElectricPiano = new(Ratio: 1, Index: 1.8, IndexDecay: 1.2, Attack: 0.002, Decay: 2.6, Sustain: 0, Release: 0.3, BellRatio: 14, BellIndex: 0.6, Level: 1.1);
    public static readonly FmPatch Bell = new(Ratio: 3.5, Index: 3.5, IndexDecay: 1.3, Attack: 0.001, Decay: 4.5, Sustain: 0, Release: 1.6, BellRatio: 0, BellIndex: 0, Level: 0.8);

    private readonly FmPatch _patch;
    private readonly Envelope _envelope;
    private readonly double _step;
    private readonly double _indexDecay;
    private double _carrier;
    private double _modulator;
    private double _bell;
    private double _index;
    private double _bellIndex;

    public FmVoice(int sampleRate, int pitch, int velocity, FmPatch patch)
        : base(sampleRate, pitch, velocity, PitchPan(pitch, 0.3))
    {
        _patch = patch;
        _step = Frequency / sampleRate;
        double hardness = 0.5 + 0.5 * velocity / 127.0;
        _index = patch.Index * hardness;
        _bellIndex = patch.BellIndex * hardness;
        _indexDecay = Math.Exp(-1 / (patch.IndexDecay * sampleRate / 4.6));
        double decay = patch.Decay * Math.Clamp(1.6 - (pitch - 48) / 60.0, 0.5, 1.6);
        _envelope = new Envelope(sampleRate, patch.Attack, decay, patch.Sustain, patch.Release);
    }

    public override void Release()
    {
        base.Release();
        _envelope.Release();
    }

    protected override float Next()
    {
        double modulation = _index * Wave.Sin(_modulator) + (_bellIndex > 0 ? _bellIndex * Wave.Sin(_bell) : 0);
        double value = Wave.Sin(_carrier + modulation / (2 * Math.PI));
        _carrier += _step;
        _modulator += _step * _patch.Ratio;
        _bell += _step * _patch.BellRatio;
        _carrier -= Math.Floor(_carrier);
        _modulator -= Math.Floor(_modulator);
        _bell -= Math.Floor(_bell);
        _index *= _indexDecay;
        _bellIndex *= _indexDecay * 0.9995;
        double env = _envelope.Next();
        Finished = _envelope.Done || (_patch.Sustain == 0 && env < 1e-4);

        return (float)(value * env * Gain * _patch.Level);
    }

    internal sealed record FmPatch(double Ratio, double Index, double IndexDecay, double Attack, double Decay, double Sustain, double Release, double BellRatio, double BellIndex, double Level);
}

/// <summary>Orgel: Zugriegel-Mischung aus Sinustönen mit leichtem Leslie-Schweben.</summary>
internal sealed class OrganVoice : SynthVoice
{
    private static readonly double[] Ratios = [0.5, 1, 2, 3, 4, 6, 8];
    private static readonly double[] Levels = [0.4, 1, 0.65, 0.4, 0.3, 0.15, 0.12];

    private readonly double[] _phases = new double[Ratios.Length];
    private readonly double[] _steps;
    private readonly Envelope _envelope;
    private readonly double _tremoloStep;
    private double _tremolo;

    public OrganVoice(int sampleRate, int pitch, int velocity)
        : base(sampleRate, pitch, 100, PitchPan(pitch, 0.2)) // Orgeln kennen keinen Anschlag
    {
        _steps = Ratios.Select(r => Frequency * r / sampleRate).ToArray();
        _envelope = new Envelope(sampleRate, 0.006, 0, 1, 0.06);
        _tremoloStep = 6.2 / sampleRate;
    }

    public override void Release()
    {
        base.Release();
        _envelope.Release();
    }

    protected override float Next()
    {
        double sum = 0;
        for (int k = 0; k < _phases.Length; k++)
        {
            if (_steps[k] < 0.45)
            {
                sum += Wave.Sin(_phases[k]) * Levels[k];
            }

            _phases[k] += _steps[k];
            _phases[k] -= Math.Floor(_phases[k]);
        }

        _tremolo += _tremoloStep;
        double env = _envelope.Next();
        Finished = _envelope.Done;
        return (float)(sum / 3.2 * (1 + 0.08 * Wave.Sin(_tremolo)) * env * Gain * 2.0);
    }
}

/// <summary>Subtraktive Synthese: verstimmte Sägezähne/Rechtecke → Tiefpass mit Hüllkurve; Vibrato.</summary>
internal sealed class AnalogVoice : SynthVoice
{
    public static readonly AnalogPatch Strings = new(Oscillators: 3, Detune: 7, Pulse: false, SubOctave: 0, Cutoff: 2600, CutoffEnv: 600, FilterDecay: 0.6, Resonance: 0.1,
        Attack: 0.18, Decay: 0.4, Sustain: 0.85, Release: 0.45, Vibrato: 0.0025, VibratoDelay: 0.35, Level: 2.0);

    public static readonly AnalogPatch Pad = new(Oscillators: 2, Detune: 12, Pulse: false, SubOctave: 0.3, Cutoff: 900, CutoffEnv: 1800, FilterDecay: 2.5, Resonance: 0.2,
        Attack: 0.7, Decay: 1, Sustain: 0.9, Release: 1.3, Vibrato: 0.0015, VibratoDelay: 0.8, Level: 2.0);

    public static readonly AnalogPatch Bass = new(Oscillators: 1, Detune: 0, Pulse: false, SubOctave: 0.6, Cutoff: 220, CutoffEnv: 2200, FilterDecay: 0.25, Resonance: 0.35,
        Attack: 0.003, Decay: 0.45, Sustain: 0.6, Release: 0.09, Vibrato: 0, VibratoDelay: 0, Level: 1.0);

    public static readonly AnalogPatch Lead = new(Oscillators: 2, Detune: 5, Pulse: true, SubOctave: 0, Cutoff: 3200, CutoffEnv: 1500, FilterDecay: 0.4, Resonance: 0.25,
        Attack: 0.01, Decay: 0.3, Sustain: 0.85, Release: 0.15, Vibrato: 0.004, VibratoDelay: 0.3, Level: 1.0);

    private readonly AnalogPatch _patch;
    private readonly double[] _phases;
    private readonly double[] _steps;
    private readonly Envelope _envelope;
    private readonly LowPass _filter;
    private readonly double _filterDecay;
    private readonly double _velocityCutoff;
    private double _filterEnv = 1;
    private double _subPhase;
    private double _vibratoPhase;
    private int _time;

    public AnalogVoice(int sampleRate, int pitch, int velocity, AnalogPatch patch)
        : base(sampleRate, pitch, velocity, patch.Oscillators > 1 ? PitchPan(pitch, 0.25) : 0)
    {
        _patch = patch;
        _phases = Enumerable.Range(0, patch.Oscillators).Select(i => i * 0.37 % 1).ToArray();
        _steps = Enumerable.Range(0, patch.Oscillators)
            .Select(i => Frequency * Math.Pow(2, (patch.Oscillators == 1 ? 0 : (i - (patch.Oscillators - 1) / 2.0) / ((patch.Oscillators - 1) / 2.0)) * patch.Detune / 1200) / sampleRate)
            .ToArray();
        _envelope = new Envelope(sampleRate, patch.Attack, patch.Decay, patch.Sustain, patch.Release);
        _filter = new LowPass(sampleRate);
        _filterDecay = Math.Exp(-1 / (patch.FilterDecay * sampleRate / 4.6));
        _velocityCutoff = 0.5 + velocity / 127.0;
    }

    public override void Release()
    {
        base.Release();
        _envelope.Release();
    }

    protected override float Next()
    {
        double vibrato = 1;
        if (_patch.Vibrato > 0 && _time > _patch.VibratoDelay * SampleRate)
        {
            _vibratoPhase += 5.5 / SampleRate;
            vibrato = 1 + _patch.Vibrato * Wave.Sin(_vibratoPhase);
        }

        double sum = 0;
        for (int i = 0; i < _phases.Length; i++)
        {
            double dt = _steps[i] * vibrato;
            sum += _patch.Pulse ? Wave.Pulse(_phases[i], dt, 0.3) * 0.7 : Wave.Saw(_phases[i], dt);
            _phases[i] += dt;
            _phases[i] -= Math.Floor(_phases[i]);
        }

        sum /= _phases.Length;
        if (_patch.SubOctave > 0)
        {
            double dt = _steps[0] * 0.5;
            sum += Wave.Pulse(_subPhase, dt, 0.5) * _patch.SubOctave * 0.6;
            _subPhase += dt;
            _subPhase -= Math.Floor(_subPhase);
        }

        if ((_time & 31) == 0)
        {
            _filter.Set((_patch.Cutoff + _patch.CutoffEnv * _filterEnv) * _velocityCutoff, _patch.Resonance);
        }

        _filterEnv *= _filterDecay;
        _time++;
        double env = _envelope.Next();
        Finished = _envelope.Done;
        return (float)(_filter.Process(sum) * env * Gain * _patch.Level);
    }

    internal sealed record AnalogPatch(
        int Oscillators, double Detune, bool Pulse, double SubOctave, double Cutoff, double CutoffEnv, double FilterDecay, double Resonance,
        double Attack, double Decay, double Sustain, double Release, double Vibrato, double VibratoDelay, double Level);
}

/// <summary>Gezupfte Saite nach Karplus-Strong: ein Rauschimpuls läuft durch eine verlustbehaftete Verzögerung.</summary>
internal sealed class PluckVoice : SynthVoice
{
    private readonly float[] _line;
    private readonly double _fraction;
    private double _loss;
    private int _index;
    private double _previous;
    private int _quiet;

    public PluckVoice(int sampleRate, int pitch, int velocity, uint seed)
        : base(sampleRate, pitch, velocity, PitchPan(pitch, 0.25))
    {
        double length = sampleRate / Frequency;
        _line = new float[Math.Max(2, (int)length)];
        _fraction = length - _line.Length;
        uint noise = seed | 1;
        double brightness = 0.25 + 0.6 * velocity / 127.0;
        double filtered = 0;
        for (int i = 0; i < _line.Length; i++)
        {
            noise = noise * 1664525 + 1013904223;
            double white = (noise >> 8) / 8388608.0 - 1;
            filtered += brightness * (white - filtered); // weicherer Anschlag = dunklerer Klang
            _line[i] = (float)filtered;
        }

        double mean = _line.Average(v => (double)v);
        for (int i = 0; i < _line.Length; i++)
        {
            _line[i] -= (float)mean;
        }

        double seconds = Math.Clamp(5 - (pitch - 40) * 0.06, 1, 5);
        _loss = Math.Pow(10, -3 / (seconds * Frequency)); // ≈ -60 dB nach „seconds“
    }

    public override void Release()
    {
        base.Release();
        _loss = Math.Min(_loss, Math.Pow(10, -3 / (0.12 * Frequency))); // abgedämpft
    }

    protected override float Next()
    {
        int next = (_index + 1) % _line.Length;
        double current = _line[_index];
        double value = current + (_line[next] - current) * _fraction;
        double averaged = 0.5 * (value + _previous) * _loss;
        _previous = value;
        _line[_index] = (float)averaged;
        _index = next;
        if (Math.Abs(averaged) < 1e-4)
        {
            if (++_quiet > _line.Length * 4)
            {
                Finished = true;
            }
        }
        else
        {
            _quiet = 0;
        }

        return (float)(value * Gain * 1.6);
    }
}

/// <summary>Schlagzeug nach General-MIDI-Belegung (36 Bassdrum, 38 Snare, 42 Hi-Hat, 49 Becken …). Spielt immer ganz aus.</summary>
internal sealed class DrumVoice : SynthVoice
{
    private readonly DrumKind _kind;
    private readonly LowPass _filter;
    private readonly double _toneStart;
    private readonly double _toneEnd;
    private readonly double _toneDecay;
    private readonly double _noiseDecay;
    private readonly double _sweep;
    private uint _noise;
    private double _phase;
    private double _tone;
    private double _noiseLevel;
    private double _toneLevel;
    private double _lastNoise;
    private int _time;

    public DrumVoice(int sampleRate, int pitch, int velocity, uint seed)
        : base(sampleRate, pitch, velocity, DrumPan(pitch))
    {
        _noise = seed | 1;
        _filter = new LowPass(sampleRate);
        _kind = pitch switch
        {
            35 or 36 => DrumKind.Kick,
            37 => DrumKind.Stick,
            38 or 40 => DrumKind.Snare,
            39 => DrumKind.Clap,
            42 or 44 => DrumKind.ClosedHat,
            46 => DrumKind.OpenHat,
            41 or 43 or 45 or 47 or 48 or 50 => DrumKind.Tom,
            49 or 52 or 55 or 57 => DrumKind.Crash,
            51 or 53 or 59 => DrumKind.Ride,
            56 => DrumKind.Cowbell,
            54 or 69 or 70 => DrumKind.Shaker,
            _ => pitch < 41 ? DrumKind.Kick : DrumKind.Tom,
        };

        (double tone, double toneEnd, double toneSeconds, double noiseSeconds, double toneLevel, double noiseLevel, double cutoff) = _kind switch
        {
            DrumKind.Kick => (150.0, 48.0, 0.38, 0.02, 1.4, 0.25, 4000.0),
            DrumKind.Stick => (900, 700, 0.03, 0.03, 0.5, 0.6, 6000),
            DrumKind.Snare => (190, 160, 0.12, 0.22, 0.7, 0.9, 9000),
            DrumKind.Clap => (0, 0, 0, 0.25, 0, 1.1, 3500),
            DrumKind.ClosedHat => (0, 0, 0, 0.05, 0, 0.6, 16000),
            DrumKind.OpenHat => (0, 0, 0, 0.45, 0, 0.5, 16000),
            DrumKind.Tom => (TomFrequency(pitch) * 1.4, TomFrequency(pitch), 0.38, 0.06, 1.1, 0.25, 3000),
            DrumKind.Crash => (0, 0, 0, 1.8, 0, 0.55, 14000),
            DrumKind.Ride => (5200, 5200, 1.0, 1.1, 0.08, 0.35, 12000),
            DrumKind.Cowbell => (560, 560, 0.3, 0, 0.6, 0, 6000),
            _ => (0, 0, 0, 0.12, 0, 0.45, 11000),
        };

        _toneStart = tone;
        _toneEnd = toneEnd;
        _toneDecay = toneSeconds > 0 ? Math.Exp(-6.9 / (toneSeconds * sampleRate)) : 0;
        _noiseDecay = noiseSeconds > 0 ? Math.Exp(-6.9 / (noiseSeconds * sampleRate)) : 0;
        _sweep = Math.Exp(-1 / (0.035 * sampleRate));
        _toneLevel = toneLevel;
        _noiseLevel = noiseLevel;
        _tone = tone;
        _filter.Set(cutoff, 0.1);
    }

    private enum DrumKind
    {
        Kick,
        Stick,
        Snare,
        Clap,
        ClosedHat,
        OpenHat,
        Tom,
        Crash,
        Ride,
        Cowbell,
        Shaker,
    }

    public override void Release()
    {
        // Schlagzeug klingt immer ganz aus.
    }

    protected override float Next()
    {
        double value = 0;
        if (_toneLevel > 1e-4 && _toneStart > 0)
        {
            _tone = _toneEnd + (_tone - _toneEnd) * _sweep;
            _phase += _tone / SampleRate;
            _phase -= Math.Floor(_phase);
            value += (_kind == DrumKind.Cowbell
                ? (Wave.Pulse(_phase, _tone / SampleRate, 0.5) + Wave.Pulse(_phase * 1.51 % 1, _tone * 1.51 / SampleRate, 0.5)) * 0.35
                : Wave.Sin(_phase)) * _toneLevel;
            _toneLevel *= _toneDecay;
        }

        if (_noiseLevel > 1e-4)
        {
            _noise = _noise * 1664525 + 1013904223;
            double white = (_noise >> 8) / 8388608.0 - 1;
            double noise = _kind is DrumKind.ClosedHat or DrumKind.OpenHat or DrumKind.Crash or DrumKind.Ride or DrumKind.Shaker
                ? white - _lastNoise // Hochpass: nur Zischen
                : white;
            _lastNoise = white;
            double shaped = _filter.Process(noise);
            if (_kind == DrumKind.Clap && _time < 0.03 * SampleRate)
            {
                shaped *= (_time / (int)(0.01 * SampleRate)) % 2 == 0 ? 1 : 0.2; // drei schnelle Klatscher
            }

            value += shaped * _noiseLevel;
            if (_kind != DrumKind.Clap || _time > 0.03 * SampleRate)
            {
                _noiseLevel *= _noiseDecay;
            }
        }

        _time++;
        if (_toneLevel <= 1e-4 && _noiseLevel <= 1e-4)
        {
            Finished = true;
        }

        return (float)(value * Gain * 0.9);
    }

    private static double TomFrequency(int pitch) => pitch switch
    {
        41 => 80,
        43 => 100,
        45 => 120,
        47 => 140,
        48 => 165,
        50 => 195,
        _ => 90 + (pitch - 40) * 6,
    };

    private static double DrumPan(int pitch) => pitch switch
    {
        42 or 44 or 46 => 0.3,
        41 or 43 => 0.25,
        47 or 48 or 50 => -0.25,
        49 or 57 => -0.35,
        51 or 59 => 0.35,
        _ => 0,
    };
}
