using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Tests;

public class LoudnessTests
{
    private static AudioBuffer Sine(double hz, double amplitude, double seconds, int rate = 48000, int channels = 2, double phase = 0)
    {
        int frames = (int)(seconds * rate);
        var samples = new float[frames * channels];
        for (int f = 0; f < frames; f++)
        {
            float v = (float)(amplitude * Math.Sin(2 * Math.PI * hz * f / rate + phase));
            for (int c = 0; c < channels; c++)
            {
                samples[f * channels + c] = v;
            }
        }

        return new AudioBuffer(samples, rate, channels);
    }

    [Fact]
    public void K_weighting_matches_the_standard_at_48_kHz()
    {
        var (b0, b1, b2, a1, a2) = KWeighting.ShelfCoefficients(48000);
        var (c1, c2) = KWeighting.HighPassCoefficients(48000);

        Assert.Equal(1.53512485958697, b0, 6);
        Assert.Equal(-2.69169618940638, b1, 6);
        Assert.Equal(1.19839281085285, b2, 6);
        Assert.Equal(-1.69065929318241, a1, 6);
        Assert.Equal(0.73248077421585, a2, 6);
        Assert.Equal(-1.99004745483398, c1, 6);
        Assert.Equal(0.99007225036621, c2, 6);
    }

    [Theory]
    [InlineData(48000, -23)]
    [InlineData(44100, -23)]
    [InlineData(44100, -20)]
    public void Stereo_1kHz_sine_reads_its_level_in_LUFS(int rate, double dbfs)
    {
        // EBU Tech 3341: 1 kHz, beide Kanäle mit -23 dBFS → -23 LUFS
        var tone = Sine(1000, Decibels.ToGain(dbfs), 20, rate);

        var result = Loudness.Measure(tone);

        Assert.InRange(result.IntegratedLufs, dbfs - 0.1, dbfs + 0.1);
        Assert.InRange(result.SamplePeakDb, dbfs - 0.05, dbfs + 0.05);
        Assert.InRange(result.LoudnessRange, 0, 0.2);
    }

    [Fact]
    public void Mono_sine_in_one_channel_is_3_dB_quieter()
    {
        var tone = Sine(1000, 1, 5, channels: 1);

        Assert.InRange(Loudness.Measure(tone).IntegratedLufs, -3.11, -2.91);
    }

    [Fact]
    public void Gating_ignores_silence()
    {
        // 10 s Ton bei -20 dBFS, dann 10 s Stille: die Stille darf das Ergebnis nicht verfälschen.
        var tone = Sine(1000, 0.1, 10);
        var samples = new float[tone.Samples.Length * 2];
        tone.Samples.CopyTo(samples);
        var withSilence = new AudioBuffer(samples, 48000, 2);

        Assert.InRange(Loudness.Measure(withSilence).IntegratedLufs, -20.1, -19.9);
        Assert.False(Loudness.Measure(new AudioBuffer(new float[96000], 48000, 2)).HasLoudness);
    }

    [Fact]
    public void Loudness_range_reflects_quiet_and_loud_parts()
    {
        var loud = Sine(1000, Decibels.ToGain(-20), 20);
        var quiet = Sine(1000, Decibels.ToGain(-30), 20);
        var samples = loud.Samples.ToArray().Concat(quiet.Samples.ToArray()).ToArray();

        var result = Loudness.Measure(new AudioBuffer(samples, 48000, 2));

        Assert.InRange(result.LoudnessRange, 9, 10.5);
        Assert.InRange(result.MaxShortTermLufs, -20.2, -19.8);
    }

    [Fact]
    public void True_peak_finds_peaks_between_samples()
    {
        // fs/4 mit 45° Phase: Samples treffen nur ±0,707, die echte Spitze ist 1,0.
        var tone = Sine(12000, 1, 1, phase: Math.PI / 4);

        double samplePeak = Decibels.FromGain(Loudness.SamplePeak(tone));
        double truePeak = TruePeak.Measure(tone);

        Assert.InRange(samplePeak, -3.1, -2.9);
        Assert.InRange(truePeak, -0.3, 0.2);
    }

    [Fact]
    public void Live_meter_reports_momentary_and_short_term()
    {
        var meter = new LoudnessMeter(48000, 2);
        var tone = Sine(1000, 0.1, 4);

        meter.Process(tone.Samples);

        Assert.InRange(meter.Momentary, -20.1, -19.9);
        Assert.InRange(meter.ShortTerm, -20.1, -19.9);
        meter.Reset();
        Assert.True(double.IsNegativeInfinity(meter.Momentary));
    }

    [Fact]
    public void Normalizing_reaches_the_target_without_exceeding_the_ceiling()
    {
        // Leiser Ton mit kurzen, kräftigen Spitzen (wie Schlagzeug): ohne Begrenzer wären -14 LUFS nur mit Übersteuerung möglich.
        var tone = Sine(220, 0.05, 10);
        var samples = tone.Samples.ToArray();
        for (int f = 0; f < tone.FrameCount; f += 24000)
        {
            for (int k = 0; k < 200; k++)
            {
                samples[(f + k) * 2] = samples[(f + k) * 2 + 1] = 0.9f * (k % 2 == 0 ? 1 : -1);
            }
        }

        var (audio, report) = LoudnessNormalizer.Normalize(new AudioBuffer(samples, 48000, 2), -14, -1);

        Assert.InRange(report.AfterLufs, -14.15, -13.85);
        Assert.True(report.TruePeakDb <= -0.9, $"Spitze {report.TruePeakDb:0.00} dBTP");
        Assert.True(report.LimitedDb > 1);
        Assert.Equal(tone.FrameCount, audio.FrameCount);
    }

    [Fact]
    public void Quieter_target_only_lowers_the_level()
    {
        var tone = Sine(1000, 0.5, 5);

        var (_, report) = LoudnessNormalizer.Normalize(tone, -23);

        Assert.InRange(report.AfterLufs, -23.05, -22.95);
        Assert.Equal(0, report.LimitedDb);
    }

    [Fact]
    public void Too_short_audio_cannot_be_normalized()
    {
        Assert.Throws<InvalidOperationException>(() => LoudnessNormalizer.Normalize(Sine(1000, 0.5, 0.2), -14));
    }
}
