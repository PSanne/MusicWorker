using MusicWorker.Core.Dsp;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Tests;

public class TempoPitchTests
{
    [Theory]
    [InlineData(2.0)]
    [InlineData(0.5)]
    [InlineData(1.25)]
    public void Stretch_changes_length_but_keeps_pitch(double factor)
    {
        var input = Signals.Sine(440, 2);

        var output = TimeStretcher.Stretch(input, factor);

        Assert.Equal((int)Math.Round(input.FrameCount * factor), output.FrameCount);
        Assert.Equal(440, Signals.EstimateFrequency(output), 440 * 0.02);
        Assert.Equal(Signals.Rms(input), Signals.Rms(output), 0.1 * Signals.Rms(input));
    }

    [Fact]
    public void Stretch_keeps_stereo_channels_separate()
    {
        var left = Signals.Sine(440, 1);
        var stereo = new Audio.AudioBuffer(
            Enumerable.Range(0, left.FrameCount).SelectMany(f => new[] { left.GetSample(f, 0), 0f }).ToArray(),
            44100, 2);

        var output = TimeStretcher.Stretch(stereo, 1.5);

        Assert.All(Enumerable.Range(0, output.FrameCount), f => Assert.Equal(0f, output.GetSample(f, 1)));
    }

    [Theory]
    [InlineData(2.0, 880)]
    [InlineData(0.5, 220)]
    public void ChangeSpeed_changes_pitch_and_length(double speed, double expectedFrequency)
    {
        var input = Signals.Sine(440, 2);

        var output = Resampler.ChangeSpeed(input, speed);

        Assert.Equal((int)Math.Round(input.FrameCount / speed), output.FrameCount);
        Assert.Equal(expectedFrequency, Signals.EstimateFrequency(output), expectedFrequency * 0.02);
    }

    [Theory]
    [InlineData(100, 12, 880)]
    [InlineData(100, -12, 220)]
    [InlineData(100, 7, 659.3)]
    public void Pitch_shift_keeps_duration(double tempo, double semitones, double expectedFrequency)
    {
        var input = Signals.Sine(440, 2);

        var output = new TempoPitchEffect(tempo, semitones).Apply(input);

        Assert.InRange(output.FrameCount, input.FrameCount - 2, input.FrameCount + 2);
        Assert.Equal(expectedFrequency, Signals.EstimateFrequency(output), expectedFrequency * 0.02);
    }

    [Theory]
    [InlineData(200, 0.5)]
    [InlineData(50, 2.0)]
    [InlineData(125, 0.8)]
    public void Tempo_change_keeps_pitch(double tempo, double expectedLengthFactor)
    {
        var input = Signals.Sine(440, 2);

        var output = new TempoPitchEffect(tempo, 0).Apply(input);

        Assert.InRange(output.FrameCount / (double)input.FrameCount, expectedLengthFactor - 0.001, expectedLengthFactor + 0.001);
        Assert.Equal(440, Signals.EstimateFrequency(output), 440 * 0.02);
    }

    [Fact]
    public void Name_describes_the_change()
    {
        var name = new TempoPitchEffect(120, -2).Name;

        Assert.StartsWith("Tempo 120 %", name);
        Assert.EndsWith("Tonhöhe -2 HT", name);
    }

    [Theory]
    [InlineData(49, 0)]
    [InlineData(201, 0)]
    [InlineData(100, 13)]
    public void Out_of_range_values_are_rejected(double tempo, double semitones)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TempoPitchEffect(tempo, semitones));
    }
}
