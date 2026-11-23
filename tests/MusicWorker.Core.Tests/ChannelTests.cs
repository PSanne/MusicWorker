using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Editing;
using MusicWorker.Core.Effects;
using MusicWorker.Core.Visualization;

namespace MusicWorker.Core.Tests;

public class ChannelTests
{
    // Stereo: links 1, 2, 3 · rechts -1, -2, -3
    private static AudioBuffer Stereo() => new([1f, -1f, 2f, -2f, 3f, -3f], 1000, 2);

    [Fact]
    public void GetChannel_extracts_a_mono_buffer()
    {
        Assert.Equal(new[] { 1f, 2f, 3f }, Stereo().GetChannel(0).Samples.ToArray());
        Assert.Equal(new[] { -1f, -2f, -3f }, Stereo().GetChannel(1).Samples.ToArray());
        Assert.Equal(1, Stereo().GetChannel(1).Channels);
    }

    [Fact]
    public void ReplaceChannels_only_touches_the_given_channel()
    {
        var result = Stereo().ReplaceChannels([1], new AudioBuffer([7f, 8f, 9f], 1000, 1));

        Assert.Equal(new[] { 1f, 7f, 2f, 8f, 3f, 9f }, result.Samples.ToArray());
    }

    [Fact]
    public void ChannelEffect_applies_only_to_the_selected_channel()
    {
        var effect = new ChannelEffect(new GainEffect(Decibels.FromGain(2)), [0]);

        var result = effect.Apply(Stereo());

        Assert.Equal(new[] { 2f, -1f, 4f, -2f, 6f, -3f }, result.Samples.ToArray(), new Tolerance());
        Assert.EndsWith("(nur links)", effect.Name);
    }

    [Fact]
    public void ChannelEffect_normalizes_by_the_peak_of_its_own_channel()
    {
        var input = new AudioBuffer([0.5f, 0.1f, -0.25f, 0.05f], 1000, 2);

        var result = new ChannelEffect(new NormalizeEffect(0), [1]).Apply(input);

        Assert.Equal(0.5f, result.GetSample(0, 0));          // links unverändert
        Assert.Equal(1f, result.GetSample(0, 1), 1e-5f);     // rechts: 0,1 → 1,0
        Assert.Equal(0.5f, result.GetSample(1, 1), 1e-5f);
    }

    [Fact]
    public void ChannelEffect_rejects_length_changing_effects()
    {
        var effect = new ChannelEffect(new TempoPitchEffect(200, 0), [0]);

        Assert.Throws<InvalidOperationException>(() => effect.Apply(Signals.Sine(440, 0.5, channels: 2)));
    }

    [Fact]
    public void KeepChannel_makes_mono_and_can_be_undone()
    {
        var editor = new AudioEditor();
        editor.Load(Stereo());

        editor.KeepChannel(1);

        Assert.Equal(1, editor.Buffer.Channels);
        Assert.Equal(new[] { -1f, -2f, -3f }, editor.Buffer.Samples.ToArray());
        Assert.Equal("Nur rechten Kanal behalten", editor.UndoDescription);

        editor.Undo();
        Assert.Equal(2, editor.Buffer.Channels);
        Assert.Equal(Stereo().Samples.ToArray(), editor.Buffer.Samples.ToArray());
    }

    [Fact]
    public void Stereo_clipboard_cannot_be_pasted_into_mono()
    {
        var editor = new AudioEditor();
        editor.Load(Stereo());
        editor.SelectAll();
        editor.Copy();

        editor.KeepChannel(0);

        Assert.False(editor.CanPaste);
    }

    [Fact]
    public void Peaks_can_be_computed_per_channel()
    {
        var left = WaveformPeaks.Compute(Stereo(), 0, 3, columns: 1, channel: 0);
        var right = WaveformPeaks.Compute(Stereo(), 0, 3, columns: 1, channel: 1);

        Assert.Equal(new PeakColumn(1, 3), left[0]);
        Assert.Equal(new PeakColumn(-3, -1), right[0]);
    }

    [Fact]
    public void Spectrum_can_be_analyzed_per_channel()
    {
        // links 1 kHz, rechts 100 Hz
        var l = Signals.Sine(1000, 0.2, 0.5f);
        var r = Signals.Sine(100, 0.2, 0.5f);
        var stereo = new AudioBuffer(
            Enumerable.Range(0, l.FrameCount).SelectMany(f => new[] { l.GetSample(f, 0), r.GetSample(f, 0) }).ToArray(),
            44100, 2);
        var left = new SpectrumAnalyzer(44100);
        var right = new SpectrumAnalyzer(44100);

        left.WriteChannel(stereo.Samples, 2, 0);
        right.WriteChannel(stereo.Samples, 2, 1);

        int Band(double f) => (int)(SpectrumScale.ToPosition(f) * 60);
        var leftBands = left.GetBands(60);
        var rightBands = right.GetBands(60);
        Assert.True(leftBands[Band(1000)] > -10 && leftBands[Band(100)] < -50);
        Assert.True(rightBands[Band(100)] > -10 && rightBands[Band(1000)] < -50);
    }

    private sealed class Tolerance : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => Math.Abs(x - y) < 1e-4f;

        public int GetHashCode(float obj) => 0;
    }
}
