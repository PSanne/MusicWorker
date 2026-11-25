using MusicWorker.Core.Analysis;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Tests;

public class MusicAnalysisTests
{
    private const int Rate = 44100;

    /// <summary>Schlagzeug-ähnlicher Rhythmus: Bass-Schlag auf jedem Viertel, leise Hi-Hat auf den Achteln.</summary>
    private static AudioBuffer Beat(double bpm, double seconds, double offset = 0.1, int rate = Rate)
    {
        var random = new Random(3);
        int frames = (int)(seconds * rate);
        var samples = new float[frames];
        double beat = 60 / bpm;
        for (double t = offset; t < seconds; t += beat / 2)
        {
            bool quarter = Math.Abs((t - offset) / beat - Math.Round((t - offset) / beat)) < 1e-6;
            int start = (int)(t * rate);
            for (int i = 0; i < rate / 10 && start + i < frames; i++)
            {
                double env = Math.Exp(-i / (rate * (quarter ? 0.04 : 0.01)));
                samples[start + i] += quarter
                    ? (float)(0.7 * env * Math.Sin(2 * Math.PI * 60 * i / rate))
                    : (float)(0.15 * env * (random.NextDouble() - 0.5));
            }
        }

        return new AudioBuffer(samples, rate, 1);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(95)]
    [InlineData(140)]
    [InlineData(72)]
    public void Tempo_is_detected(double bpm)
    {
        var result = MusicAnalysis.DetectTempo(Beat(bpm, 20));

        Assert.NotNull(result);
        Assert.InRange(result!.Bpm, bpm - 1, bpm + 1);
    }

    [Theory]
    [InlineData(110)]
    [InlineData(64)]
    public void Tempo_is_detected_at_48_kHz(double bpm)
    {
        var result = MusicAnalysis.DetectTempo(Beat(bpm, 20, rate: 48000));

        Assert.NotNull(result);
        Assert.InRange(result!.Bpm, bpm - 1, bpm + 1);
    }

    [Fact]
    public void First_beat_is_found()
    {
        var result = MusicAnalysis.DetectTempo(Beat(120, 15, offset: 0.23));

        Assert.NotNull(result);
        double beat = 0.5;
        double phase = result!.FirstBeatSeconds % beat;
        Assert.True(Math.Abs(phase - 0.23) < 0.03 || Math.Abs(phase - 0.23 + beat) < 0.03, $"erster Schlag bei {result.FirstBeatSeconds:0.000} s");
    }

    [Fact]
    public void Silence_has_no_tempo()
    {
        Assert.Null(MusicAnalysis.DetectTempo(new AudioBuffer(new float[Rate * 10], Rate, 1)));
        Assert.Null(MusicAnalysis.DetectKey(new AudioBuffer(new float[Rate * 2], Rate, 1)));
    }

    /// <summary>Akkordfolge aus Sinustönen mit Obertönen (Grundtöne als MIDI-Tonhöhen).</summary>
    private static AudioBuffer Chords(params int[][] chords)
    {
        const double chordSeconds = 1.5;
        int frames = (int)(chords.Length * chordSeconds * Rate);
        var samples = new float[frames];
        for (int c = 0; c < chords.Length; c++)
        {
            int start = (int)(c * chordSeconds * Rate);
            foreach (int pitch in chords[c])
            {
                double hz = 440 * Math.Pow(2, (pitch - 69) / 12.0);
                for (int i = 0; i < chordSeconds * Rate; i++)
                {
                    double t = i / (double)Rate;
                    double env = Math.Exp(-t * 1.5);
                    samples[start + i] += (float)(0.1 * env * (Math.Sin(2 * Math.PI * hz * t) + 0.4 * Math.Sin(4 * Math.PI * hz * t) + 0.2 * Math.Sin(6 * Math.PI * hz * t)));
                }
            }
        }

        return new AudioBuffer(samples, Rate, 1);
    }

    [Fact]
    public void Key_of_a_minor_cadence()
    {
        // a-Moll: Am – Dm – E – Am (mit Basston)
        var audio = Chords([45, 57, 60, 64], [50, 62, 65, 69], [52, 64, 68, 71], [45, 57, 60, 64]);

        var key = MusicAnalysis.DetectKey(audio);

        Assert.NotNull(key);
        Assert.Equal("a-Moll", key!.Name);
        Assert.Equal(ScaleKind.Minor, key.Scale);
    }

    [Fact]
    public void Key_of_a_major_cadence()
    {
        // D-Dur: D – G – A7 – D
        var audio = Chords([50, 62, 66, 69], [55, 67, 71, 74], [57, 64, 67, 73], [50, 62, 66, 69]);

        var key = MusicAnalysis.DetectKey(audio);

        Assert.NotNull(key);
        Assert.Equal("D-Dur", key!.Name);
        Assert.Equal(2, key.Tonic);
    }
}
