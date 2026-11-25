using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Tests;

public class VoiceEffectsTests
{
    private const int Rate = 44100;

    private static double Rms(AudioBuffer b, double from, double to, double highPassHz = 0)
    {
        var filter = highPassHz > 0 ? new BiquadFilter(BiquadCoefficients.HighPass(Rate, highPassHz, 0.707)) : null;
        double sum = 0;
        int a = (int)(from * Rate), e = (int)(to * Rate);
        for (int i = 0; i < e; i++)
        {
            double v = b.GetSample(i, 0);
            if (filter is not null)
            {
                v = filter.Process(v);
            }

            if (i >= a)
            {
                sum += v * v;
            }
        }

        return Math.Sqrt(sum / (e - a));
    }

    /// <summary>Stimme (250 Hz) mit „s“-Lauten: 0,5–0,7 s Zischlaut allein, 1,2–1,4 s Zischlaut über dem Ton.</summary>
    private static AudioBuffer Speech()
    {
        var random = new Random(7);
        var noiseFilter = new BiquadFilter(BiquadCoefficients.HighPass(Rate, 6500, 0.707));
        var data = new float[Rate * 2];
        for (int i = 0; i < data.Length; i++)
        {
            double t = i / (double)Rate;
            bool hiss = (t >= 0.5 && t < 0.7) || (t >= 1.2 && t < 1.4);
            bool voice = !(t >= 0.5 && t < 0.7);
            double noise = noiseFilter.Process(random.NextDouble() * 2 - 1);
            data[i] = (float)((voice ? 0.3 * Math.Sin(2 * Math.PI * 250 * t) : 0) + (hiss ? 0.4 * noise : 0));
        }

        return new AudioBuffer(data, Rate, 1);
    }

    [Fact]
    public void De_esser_tames_sibilants_and_leaves_the_voice_alone()
    {
        var input = Speech();
        var output = new DeEsserEffect(5500, 10, 5).Apply(input);

        double hissAlone = 20 * Math.Log10(Rms(output, 0.52, 0.68, 5000) / Rms(input, 0.52, 0.68, 5000));
        double hissOverVoice = 20 * Math.Log10(Rms(output, 1.22, 1.38, 5000) / Rms(input, 1.22, 1.38, 5000));
        Assert.InRange(hissAlone, -11, -6);
        Assert.InRange(hissOverVoice, -11, -4);

        // Reine Stimme: praktisch unverändert
        double voiceDiff = 0;
        for (int i = (int)(0.1 * Rate); i < (int)(0.45 * Rate); i++)
        {
            voiceDiff = Math.Max(voiceDiff, Math.Abs(output.GetSample(i, 0) - input.GetSample(i, 0)));
        }

        Assert.True(voiceDiff < 0.003, $"Abweichung {voiceDiff:0.0000}");
    }

    [Fact]
    public void Less_sensitivity_means_less_reduction()
    {
        var input = Speech();
        double soft = Rms(new DeEsserEffect(5500, 10, 1).Apply(input), 1.22, 1.38, 5000);
        double strong = Rms(new DeEsserEffect(5500, 10, 10).Apply(input), 1.22, 1.38, 5000);
        Assert.True(strong < soft, $"{strong:0.000} < {soft:0.000}");
    }

    /// <summary>Vokal „a“: Pulsfolge mit 140 Hz durch zwei Formanten (700 Hz und 1200 Hz).</summary>
    private static AudioBuffer Vowel(double f0)
    {
        var data = new float[Rate];
        var f1 = new BiquadFilter(BiquadCoefficients.PeakingEq(Rate, 700, 4, 24));
        var f2 = new BiquadFilter(BiquadCoefficients.PeakingEq(Rate, 1200, 4, 18));
        var lp = new BiquadFilter(BiquadCoefficients.LowPass(Rate, 3000, 0.707));
        double phase = 0;
        for (int i = 0; i < data.Length; i++)
        {
            phase += f0 / Rate;
            double pulse = 0;
            if (phase >= 1)
            {
                phase -= 1;
                pulse = 1;
            }

            data[i] = (float)(0.05 * lp.Process(f2.Process(f1.Process(pulse))));
        }

        return new AudioBuffer(data, Rate, 1);
    }

    /// <summary>Frequenz der stärksten Resonanz (erster Formant) im geglätteten Durchschnittsspektrum.</summary>
    private static double FirstFormant(AudioBuffer buffer)
    {
        var mono = new float[buffer.FrameCount];
        for (int i = 0; i < mono.Length; i++)
        {
            mono[i] = buffer.GetSample(i, 0);
        }

        var spectra = Stft.Magnitudes(mono).ToArray();
        int bins = spectra[0].Length;
        var average = new double[bins];
        foreach (var s in spectra)
        {
            for (int k = 0; k < bins; k++)
            {
                average[k] += s[k];
            }
        }

        double binHz = Rate / (double)Stft.Size;
        int width = (int)(160 / binHz);
        double best = 0;
        double bestHz = 0;
        for (int k = (int)(400 / binHz); k < (int)(1000 / binHz); k++)
        {
            double sum = 0;
            for (int j = k - width; j <= k + width; j++)
            {
                sum += average[j];
            }

            if (sum > best)
            {
                best = sum;
                bestHz = k * binHz;
            }
        }

        return bestHz;
    }

    [Fact]
    public void Formants_stay_while_pitch_moves()
    {
        var vowel = Vowel(140);
        Assert.InRange(FirstFormant(vowel), 620, 780);

        var plain = new PitchShiftEffect(5).Apply(vowel);
        var natural = new FormantPreservingPitchEffect(5).Apply(vowel);

        Assert.True(FirstFormant(plain) > 820, $"ohne Formanterhalt {FirstFormant(plain):0} Hz");
        Assert.InRange(FirstFormant(natural), 600, 800);
        Assert.Equal(vowel.FrameCount, natural.FrameCount);

        double original = PitchDetector.Track(vowel).MedianMidi()!.Value;
        double shifted = PitchDetector.Track(natural).MedianMidi()!.Value;
        Assert.InRange(shifted - original, 4.6, 5.4);
    }

    [Fact]
    public void Level_is_kept_for_a_whispered_vowel()
    {
        // Geflüstert: Rauschen durch die Formanten – keine Obertöne, also muss der Pegel gleich bleiben.
        var random = new Random(3);
        var f1 = new BiquadFilter(BiquadCoefficients.PeakingEq(Rate, 700, 2, 18));
        var data = new float[Rate];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (float)(0.05 * f1.Process(random.NextDouble() * 2 - 1));
        }

        var whisper = new AudioBuffer(data, Rate, 1);
        var plain = new PitchShiftEffect(5).Apply(whisper);
        var natural = new FormantPreservingPitchEffect(5).Apply(whisper);
        double difference = 20 * Math.Log10(Rms(natural, 0.1, 0.9) / Rms(plain, 0.1, 0.9));
        Assert.True(Math.Abs(difference) < 2, $"Pegelunterschied {difference:0.0} dB");
    }

    [Fact]
    public void Formants_can_be_moved_on_their_own()
    {
        var vowel = Vowel(140);
        var darker = new FormantPreservingPitchEffect(0, -5).Apply(vowel);

        Assert.True(FirstFormant(darker) < 600, $"{FirstFormant(darker):0} Hz");
        double original = PitchDetector.Track(vowel).MedianMidi()!.Value;
        Assert.InRange(PitchDetector.Track(darker).MedianMidi()!.Value - original, -0.4, 0.4);
    }
}
