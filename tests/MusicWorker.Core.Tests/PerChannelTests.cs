using MusicWorker.Core.Audio;
using MusicWorker.Core.Editing;
using MusicWorker.Core.Effects;

namespace MusicWorker.Core.Tests;

public class PerChannelTests
{
    private const int Rate = 44100;

    /// <summary>Stereo: links 440 Hz, rechts 220 Hz.</summary>
    private static AudioBuffer Stereo(double seconds = 2)
    {
        var l = Signals.Sine(440, seconds);
        var r = Signals.Sine(220, seconds);
        var samples = new float[l.FrameCount * 2];
        for (int f = 0; f < l.FrameCount; f++)
        {
            samples[2 * f] = l.GetSample(f, 0);
            samples[2 * f + 1] = r.GetSample(f, 0);
        }

        return new AudioBuffer(samples, Rate, 2);
    }

    [Fact]
    public void Slower_left_channel_lengthens_the_file_and_pads_the_right_at_the_end()
    {
        var input = Stereo();
        var whole = new SelectionRange(0, input.FrameCount);

        var result = PerChannelProcessor.Apply(input, whole, [new TempoPitchEffect(50, 0), null]);

        Assert.Equal(input.FrameCount * 2, result.Buffer.FrameCount);
        Assert.Equal(new[] { 0, input.FrameCount }, result.PaddingFrames);

        // Rechts: erst unverändert das Original, danach Stille.
        var right = result.Buffer.GetChannel(1);
        Assert.Equal(input.GetChannel(1).Samples.ToArray(), right.Slice(0, input.FrameCount).Samples.ToArray());
        Assert.All(right.Slice(input.FrameCount, input.FrameCount).Samples.ToArray(), s => Assert.Equal(0f, s));

        // Links: doppelt so lang, aber gleiche Tonhöhe.
        Assert.Equal(440, Signals.EstimateFrequency(result.Buffer.GetChannel(0)), 440 * 0.02);
    }

    [Fact]
    public void Pitch_per_channel_keeps_the_length()
    {
        var input = Stereo();
        var whole = new SelectionRange(0, input.FrameCount);

        var result = PerChannelProcessor.Apply(input, whole, [null, new TempoPitchEffect(100, 12)]);

        Assert.InRange(result.Buffer.FrameCount, input.FrameCount - 2, input.FrameCount + 2);
        Assert.Equal(440, Signals.EstimateFrequency(result.Buffer.GetChannel(0)), 440 * 0.02);
        Assert.Equal(440, Signals.EstimateFrequency(result.Buffer.GetChannel(1)), 440 * 0.02); // 220 Hz + 1 Oktave
    }

    [Fact]
    public void Each_channel_can_get_its_own_tempo_and_the_shorter_one_is_padded()
    {
        var input = Stereo();
        var whole = new SelectionRange(0, input.FrameCount);

        var result = PerChannelProcessor.Apply(input, whole,
            [new TempoPitchEffect(200, 0), new TempoPitchEffect(80, 0)]);

        int leftLength = (int)Math.Round(input.FrameCount / 2.0);
        int rightLength = (int)Math.Round(input.FrameCount / 0.8);
        Assert.Equal(rightLength, result.Buffer.FrameCount);
        Assert.Equal(new[] { leftLength, rightLength }, result.ChannelLengths);
        Assert.All(result.Buffer.GetChannel(0).Slice(leftLength, rightLength - leftLength).Samples.ToArray(),
            s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Selection_in_the_middle_shifts_the_rest_of_that_channel_and_pads_the_other_at_the_end()
    {
        var input = Stereo(3);
        var range = new SelectionRange(Rate, 2 * Rate); // Sekunde 1–2

        var result = PerChannelProcessor.Apply(input, range, [new TempoPitchEffect(50, 0), null]);

        // Links ist 1 s länger geworden → die ganze Datei auch.
        Assert.Equal(input.FrameCount + Rate, result.Buffer.FrameCount);
        Assert.Equal(2 * Rate, result.ProcessedLength);

        // Links: Anfang unverändert, der Rest nach der Auswahl ist um 1 s nach hinten gerückt.
        var left = result.Buffer.GetChannel(0);
        var originalLeft = input.GetChannel(0);
        Assert.Equal(originalLeft.Slice(0, Rate).Samples.ToArray(), left.Slice(0, Rate).Samples.ToArray());
        Assert.Equal(originalLeft.Slice(2 * Rate, Rate).Samples.ToArray(), left.Slice(3 * Rate, Rate).Samples.ToArray());

        // Rechts: komplett unverändert, die zusätzliche Sekunde ist Stille am Ende des Stücks.
        var right = result.Buffer.GetChannel(1);
        Assert.Equal(input.GetChannel(1).Samples.ToArray(), right.Slice(0, input.FrameCount).Samples.ToArray());
        Assert.All(right.Slice(input.FrameCount, Rate).Samples.ToArray(), s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Editor_can_apply_and_undo_a_per_channel_result()
    {
        var editor = new AudioEditor();
        var input = Stereo();
        editor.Load(input);
        var result = PerChannelProcessor.Apply(input, editor.EffectRange, [new TempoPitchEffect(50, 0), null]);

        editor.ApplyBufferResult("Tempo pro Kanal", result.Buffer, editor.Selection);
        Assert.Equal(input.FrameCount * 2, editor.Buffer.FrameCount);

        editor.Undo();
        Assert.Equal(input.FrameCount, editor.Buffer.FrameCount);
    }

    [Fact]
    public void Wrong_number_of_effects_is_rejected()
    {
        var input = Stereo(0.1);

        Assert.Throws<ArgumentException>(() =>
            PerChannelProcessor.Apply(input, new SelectionRange(0, input.FrameCount), [null]));
    }
}
