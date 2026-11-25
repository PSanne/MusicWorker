using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Tests;

public class OggVorbisTests
{
    private static AudioBuffer Tone(int rate, int channels, double seconds)
    {
        int frames = (int)(rate * seconds);
        var samples = new float[frames * channels];
        for (int f = 0; f < frames; f++)
        {
            for (int c = 0; c < channels; c++)
            {
                samples[f * channels + c] = (float)(0.5 * Math.Sin(2 * Math.PI * (440 + 220 * c) * f / rate));
            }
        }

        return new AudioBuffer(samples, rate, channels);
    }

    private static double Correlation(AudioBuffer a, AudioBuffer b, int channel, int from, int count)
    {
        double ab = 0, aa = 0, bb = 0;
        for (int f = from; f < from + count; f++)
        {
            double x = a.GetSample(f, channel);
            double y = b.GetSample(f, channel);
            ab += x * y;
            aa += x * x;
            bb += y * y;
        }

        return ab / Math.Sqrt(aa * bb);
    }

    [Theory]
    [InlineData(44100, 2)]
    [InlineData(48000, 2)]
    [InlineData(44100, 1)]
    [InlineData(48000, 1)]
    public void Round_trip_keeps_length_and_sound(int rate, int channels)
    {
        var audio = Tone(rate, channels, 2);
        using var stream = new MemoryStream();

        OggVorbisFile.Write(audio, stream);
        stream.Position = 0;
        var decoded = OggVorbisFile.Read(stream);

        Assert.Equal(2, decoded.Channels); // Mono wird als Stereo gespeichert
        Assert.Equal(rate, decoded.SampleRate);
        Assert.Equal(audio.FrameCount, decoded.FrameCount);
        var reference = AudioConversion.ToStereo(audio);
        for (int c = 0; c < 2; c++)
        {
            double correlation = Correlation(reference, decoded, c, rate / 2, rate);
            Assert.True(correlation > 0.99, $"Kanal {c} klingt anders ({correlation:0.000})");
        }

        Assert.True(stream.Length < audio.FrameCount * 4 / 4, $"{stream.Length} Bytes – kaum komprimiert");
    }

    [Fact]
    public void Unusual_sample_rates_are_converted()
    {
        var audio = Tone(96000, 2, 1);
        using var stream = new MemoryStream();

        OggVorbisFile.Write(audio, stream);
        stream.Position = 0;
        var decoded = OggVorbisFile.Read(stream);

        Assert.Equal(48000, decoded.SampleRate);
        Assert.InRange(decoded.FrameCount, 47999, 48001);
    }

    [Fact]
    public void Low_sample_rates_are_raised()
    {
        var audio = Tone(22050, 1, 1);
        using var stream = new MemoryStream();

        OggVorbisFile.Write(audio, stream);
        stream.Position = 0;

        Assert.Equal(44100, OggVorbisFile.Read(stream).SampleRate);
    }

    [Fact]
    public void Resample_changes_rate_and_length()
    {
        var audio = Tone(22050, 1, 1);

        var resampled = AudioConversion.Resample(audio, 44100);

        Assert.Equal(44100, resampled.SampleRate);
        Assert.InRange(resampled.FrameCount, 44099, 44101);
        Assert.Same(audio, AudioConversion.Resample(audio, 22050));
    }
}
