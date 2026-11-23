using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;
using MusicWorker.Core.Visualization;

namespace MusicWorker.Core.Tests;

public class PitchSpectrumTests
{
    private const int Rate = 44100;

    /// <summary>Stimmähnlicher Ton: Grundton plus Obertöne.</summary>
    private static AudioBuffer Voice(double hz, double seconds = 1, int channels = 1)
    {
        int frames = (int)(seconds * Rate);
        var samples = new float[frames * channels];
        for (int f = 0; f < frames; f++)
        {
            double t = f / (double)Rate;
            double v = 0.4 * Math.Sin(2 * Math.PI * hz * t) + 0.2 * Math.Sin(2 * Math.PI * 2 * hz * t) + 0.1 * Math.Sin(2 * Math.PI * 3 * hz * t);
            for (int c = 0; c < channels; c++)
            {
                samples[f * channels + c] = (float)v;
            }
        }

        return new AudioBuffer(samples, Rate, channels);
    }

    private static double Cents(double hz, double reference) => 1200 * Math.Log2(hz / reference);

    private static double MedianHz(AudioBuffer audio, double skipSeconds = 0.1)
    {
        int skip = (int)(skipSeconds * audio.SampleRate);
        var midi = PitchDetector.Track(audio.Slice(skip, audio.FrameCount - 2 * skip)).MedianMidi();
        Assert.NotNull(midi);
        return 440 * Math.Pow(2, (midi!.Value - 69) / 12);
    }

    [Theory]
    [InlineData(110)]
    [InlineData(220)]
    [InlineData(330)]
    [InlineData(587.3)]
    public void Yin_detects_the_fundamental(double hz)
    {
        Assert.InRange(Cents(MedianHz(Voice(hz)), hz), -10, 10);
    }

    [Fact]
    public void Silence_is_unvoiced()
    {
        var track = PitchDetector.Track(new AudioBuffer(new float[Rate], Rate, 1));

        Assert.Null(track.MedianMidi());
        Assert.All(track.Frames, f => Assert.False(f.IsVoiced));
    }

    [Fact]
    public void Pitch_shift_by_a_fourth_keeps_the_length()
    {
        var input = Voice(220, channels: 2);

        var shifted = new PitchShiftEffect(5).Apply(input);

        Assert.Equal(input.FrameCount, shifted.FrameCount);
        Assert.Equal(2, shifted.Channels);
        Assert.InRange(Cents(MedianHz(shifted), 220 * Math.Pow(2, 5 / 12.0)), -15, 15);
    }

    [Fact]
    public void Autotune_pulls_a_sharp_note_to_the_scale()
    {
        var sharp = Voice(229); // A3 (220 Hz) + 69 Cent → nächster Halbton ist A#3 (233,1 Hz)

        var tuned = new AutotuneEffect(0, ScaleKind.Chromatic, speedMs: 0, strengthPercent: 100).Apply(sharp);

        Assert.InRange(Cents(MedianHz(tuned), 233.08), -15, 15);
    }

    [Fact]
    public void Autotune_respects_the_key()
    {
        var input = Voice(300); // D4 + 37 Cent – in C-Dur liegt D4 (293,7 Hz) am nächsten

        var tuned = new AutotuneEffect(0, ScaleKind.Major, 0, 100).Apply(input);

        Assert.InRange(Cents(MedianHz(tuned), 293.66), -15, 15);
    }

    [Fact]
    public void Half_strength_corrects_half_way()
    {
        var input = Voice(226.4); // A3 + 50 Cent

        var corrections = new AutotuneEffect(9, ScaleKind.Major, 0, 50).Corrections(PitchDetector.Track(input), Rate);

        double median = corrections.OrderBy(c => c).ElementAt(corrections.Length / 2);
        Assert.InRange(Math.Abs(median), 0.2, 0.3); // ±0,25 Halbtöne
    }

    [Fact]
    public void Scales_find_the_nearest_tone()
    {
        Assert.Equal(62, Scales.Nearest(62.4, 0, ScaleKind.Major));        // D
        Assert.Equal(64, Scales.Nearest(63.2, 0, ScaleKind.Major));        // Es gehört nicht zu C-Dur → E
        Assert.Equal(63, Scales.Nearest(63.2, 0, ScaleKind.Minor));        // in c-Moll schon
        Assert.Equal(69, Scales.Nearest(69.4, 9, ScaleKind.MinorPentatonic));
        Assert.Equal("A4 +12 ct", Scales.Describe(69.12));
    }

    [Fact]
    public void Pitch_ramp_glides_up_an_octave()
    {
        var input = Voice(220, 2);

        var ramp = new PitchRampEffect(0, 12).Apply(input);

        double start = MedianHz(ramp.Slice(2205, 13230), 0);
        double end = MedianHz(ramp.Slice(ramp.FrameCount - 13230, 11025), 0);
        Assert.InRange(Cents(start, 220), -150, 150);
        Assert.InRange(Cents(end, 440), -150, 150);
    }

    [Fact]
    public void Stft_without_changes_reconstructs_the_input()
    {
        var input = Voice(440, 0.5);
        var mono = input.Samples.ToArray();

        var output = Stft.Process(mono, (_, _, _) => { });

        double maxError = mono.Zip(output, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(maxError < 1e-4, $"Fehler {maxError}");
    }

    [Fact]
    public void Noise_reduction_lowers_the_noise_and_keeps_the_tone()
    {
        var random = new Random(3);
        int frames = Rate * 2;
        var samples = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float noise = (float)(random.NextDouble() * 2 - 1) * 0.05f;
            float tone = f >= Rate ? (float)(0.5 * Math.Sin(2 * Math.PI * 440 * f / Rate)) : 0;
            samples[f] = noise + tone;
        }

        var input = new AudioBuffer(samples, Rate, 1);
        var profile = NoiseProfile.Learn(input.Slice(0, Rate / 2));

        var output = new NoiseReductionEffect(profile, reductionDb: 20, sensitivity: 2).Apply(input);

        double noiseBefore = Signals.Rms(input.Slice(Rate / 2, Rate / 4), 0);
        double noiseAfter = Signals.Rms(output.Slice(Rate / 2, Rate / 4), 0);
        double toneBefore = Signals.Rms(input.Slice(Rate + Rate / 4, Rate / 2), 0);
        double toneAfter = Signals.Rms(output.Slice(Rate + Rate / 4, Rate / 2), 0);
        Assert.True(20 * Math.Log10(noiseAfter / noiseBefore) < -12, $"Rauschen nur um {20 * Math.Log10(noiseAfter / noiseBefore):0.0} dB gesenkt");
        Assert.InRange(20 * Math.Log10(toneAfter / toneBefore), -1.5, 0.5);
    }

    [Fact]
    public void Spectrogram_shows_a_tone_at_its_frequency()
    {
        var tone = Signals.Sine(1000, 1, 0.5f, Rate);

        var spectrogram = Spectrogram.Compute(tone, columns: 20, rows: 100);

        int brightest = Enumerable.Range(0, spectrogram.Rows).MaxBy(r => spectrogram[10, r]);
        Assert.InRange(spectrogram.FrequencyOfRow(brightest), 900, 1110);
        Assert.True(spectrogram[10, brightest] > 0.8f);
        Assert.True(spectrogram[10, spectrogram.Rows - 1] < 0.3f); // tiefe Frequenzen leer
    }

    [Fact]
    public void Voice_presets_produce_audio()
    {
        var input = Voice(220, 0.5, channels: 2);
        foreach (var preset in VoiceEffect.All)
        {
            var output = new VoiceEffect(preset).Apply(input);
            Assert.Equal(input.FrameCount, output.FrameCount);
            Assert.DoesNotContain(output.Samples.ToArray(), float.IsNaN);
            Assert.True(Signals.Peak(output) > 0.02f, $"{preset} ist stumm");
        }

        double helium = MedianHz(new VoiceEffect(VoicePreset.Helium).Apply(Voice(220)));
        Assert.InRange(Cents(helium, 220 * Math.Pow(2, 7 / 12.0)), -20, 20);
    }
}
