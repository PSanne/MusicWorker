using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Tests;

public class FlacTests
{
    private static AudioBuffer Music(int frames, int channels, int rate = 44100, int seed = 1)
    {
        var random = new Random(seed);
        var samples = new float[frames * channels];
        for (int f = 0; f < frames; f++)
        {
            double t = f / (double)rate;
            double baseTone = 0.4 * Math.Sin(2 * Math.PI * 220 * t) + 0.2 * Math.Sin(2 * Math.PI * 330 * t);
            for (int c = 0; c < channels; c++)
            {
                samples[f * channels + c] = (float)(baseTone * (c == 0 ? 1 : 0.8) + 0.02 * (random.NextDouble() - 0.5));
            }
        }

        return new AudioBuffer(samples, rate, channels);
    }

    private static void AssertLossless(AudioBuffer original, AudioBuffer decoded, int bits)
    {
        Assert.Equal(original.Channels, decoded.Channels);
        Assert.Equal(original.SampleRate, decoded.SampleRate);
        Assert.Equal(original.FrameCount, decoded.FrameCount);
        double scale = 1 << (bits - 1);
        for (int i = 0; i < original.Samples.Length; i++)
        {
            double expected = Math.Clamp(Math.Round(original.Samples[i] * scale), -scale, scale - 1) / scale;
            Assert.Equal(expected, decoded.Samples[i], 9);
        }
    }

    [Theory]
    [InlineData(1, 16)]
    [InlineData(2, 16)]
    [InlineData(2, 24)]
    [InlineData(1, 24)]
    public void Round_trip_is_lossless(int channels, int bits)
    {
        // 10 000 Frames: zwei volle Blöcke und ein angefangener
        var audio = Music(10000, channels);
        using var stream = new MemoryStream();

        FlacFile.Write(audio, stream, bits);
        var decoded = FlacFile.Read(stream.ToArray());

        AssertLossless(audio, decoded, bits);
    }

    [Fact]
    public void Compresses_music_and_silence()
    {
        var audio = Music(44100 * 2, 2);
        var withSilence = new AudioBuffer(audio.Samples.ToArray().Concat(new float[44100 * 2]).ToArray(), 44100, 2);
        using var stream = new MemoryStream();

        FlacFile.Write(withSilence, stream);

        long wavSize = withSilence.Samples.Length * 2L;
        Assert.True(stream.Length < wavSize * 0.6, $"{stream.Length} von {wavSize} Bytes");
        AssertLossless(withSilence, FlacFile.Read(stream.ToArray()), 16);
    }

    [Fact]
    public void Clipping_and_extremes_survive()
    {
        var samples = new float[5000 * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = i % 7 == 0 ? 1.5f : i % 5 == 0 ? -1.2f : (i % 3 - 1);
        }

        var audio = new AudioBuffer(samples, 48000, 2);
        using var stream = new MemoryStream();
        FlacFile.Write(audio, stream, 24);

        AssertLossless(audio, FlacFile.Read(stream.ToArray()), 24);
    }

    [Fact]
    public void Tiny_and_empty_files_work()
    {
        foreach (int frames in new[] { 0, 1, 3, 4097 })
        {
            var audio = Music(frames, 2);
            using var stream = new MemoryStream();
            FlacFile.Write(audio, stream);
            Assert.Equal(frames, FlacFile.Read(stream.ToArray()).FrameCount);
        }
    }

    [Fact]
    public void Non_flac_data_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => FlacFile.Read(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
    }
}
