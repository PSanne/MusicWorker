using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Tests;

public class AudioBufferTests
{
    [Fact]
    public void FrameCount_and_duration_are_derived_from_samples()
    {
        var buffer = TestAudio.Ramp(frames: 2000, channels: 2, sampleRate: 1000);

        Assert.Equal(2000, buffer.FrameCount);
        Assert.Equal(TimeSpan.FromSeconds(2), buffer.Duration);
    }

    [Fact]
    public void Constructor_rejects_incomplete_frames()
    {
        Assert.Throws<ArgumentException>(() => new AudioBuffer(new float[3], 44100, 2));
    }

    [Fact]
    public void Slice_copies_the_requested_frames_of_all_channels()
    {
        var slice = TestAudio.Ramp(10, channels: 2).Slice(3, 4);

        Assert.Equal(new float[] { 3, 4, 5, 6 }, TestAudio.Frames(slice, 0));
        Assert.Equal(new float[] { 3.5f, 4.5f, 5.5f, 6.5f }, TestAudio.Frames(slice, 1));
    }

    [Fact]
    public void RemoveRange_closes_the_gap()
    {
        var result = TestAudio.Ramp(6).RemoveRange(1, 3);

        Assert.Equal(new float[] { 0, 4, 5 }, TestAudio.Frames(result));
    }

    [Fact]
    public void Insert_places_audio_at_the_position()
    {
        var result = TestAudio.Ramp(3).Insert(1, TestAudio.Ramp(2));

        Assert.Equal(new float[] { 0, 0, 1, 1, 2 }, TestAudio.Frames(result));
    }

    [Fact]
    public void Operations_do_not_change_the_original()
    {
        var original = TestAudio.Ramp(5);

        _ = original.RemoveRange(0, 5);

        Assert.Equal(5, original.FrameCount);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 6)]
    [InlineData(5, 1)]
    public void Out_of_range_access_throws(int start, int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestAudio.Ramp(5).Slice(start, count));
    }

    [Fact]
    public void Mixing_formats_is_rejected()
    {
        var mono = TestAudio.Ramp(3, channels: 1);
        var stereo = TestAudio.Ramp(3, channels: 2);

        Assert.Throws<ArgumentException>(() => mono.Insert(0, stereo));
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(-6.0206, 0.5)]
    [InlineData(6.0206, 2.0)]
    public void Decibels_convert_to_gain(double db, double gain)
    {
        Assert.Equal(gain, Decibels.ToGain(db), precision: 3);
        Assert.Equal(db, Decibels.FromGain(gain), precision: 3);
    }
}
