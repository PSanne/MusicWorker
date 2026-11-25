using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;
using MusicWorker.Core.Recording;

namespace MusicWorker.Core.Tests;

public class LowLatencyTests
{
    [Fact]
    public void Monitor_ring_passes_samples_through_in_order()
    {
        var ring = new MonitorRing(1000);
        ring.Write([1, -1, 2, -2, 3, -3]);
        var output = new float[4];

        Assert.Equal(2, ring.MixInto(output, 1));
        Assert.Equal([1f, -1f, 2f, -2f], output);
        Assert.Equal(1, ring.AvailableFrames);
    }

    [Fact]
    public void Monitor_ring_mixes_with_gain_and_leaves_missing_frames_silent()
    {
        var ring = new MonitorRing(1000);
        ring.Write([1, 1]);
        float[] output = [0.5f, 0.5f, 0.5f, 0.5f];

        Assert.Equal(1, ring.MixInto(output, 0.5f));
        Assert.Equal([1f, 1f, 0.5f, 0.5f], output);
    }

    [Fact]
    public void Monitor_ring_drops_old_samples_to_keep_latency_low()
    {
        var ring = new MonitorRing(10000) { MaxLatencyFrames = 100 };
        var data = new float[2000 * 2];
        for (int i = 0; i < 2000; i++)
        {
            data[i * 2] = data[(i * 2) + 1] = i;
        }

        ring.Write(data);
        var output = new float[10 * 2];
        ring.MixInto(output, 1);

        Assert.Equal(1890, output[0]); // vorgespult: danach warten höchstens noch 100 Frames
        Assert.Equal(100, ring.AvailableFrames);
    }

    [Fact]
    public void Monitor_ring_overwrites_when_full()
    {
        var ring = new MonitorRing(64);
        var data = Enumerable.Range(0, 100 * 2).Select(i => (float)(i / 2)).ToArray();
        ring.Write(data);
        var output = new float[2];
        ring.MixInto(output, 1);

        Assert.Equal(36, output[0]); // die ältesten 36 Frames sind herausgefallen
        Assert.Equal(63, ring.AvailableFrames);
    }

    [Fact]
    public void Pcm_and_float_bytes_are_converted()
    {
        Assert.Equal([0.5f, -0.5f], SampleConversion.ToFloat(BitConverter.GetBytes((short)16384).Concat(BitConverter.GetBytes((short)-16384)).ToArray(), 16, false));
        Assert.Equal([0.25f], SampleConversion.ToFloat(BitConverter.GetBytes(0.25f), 32, true));
        byte[] pcm24 = [0x00, 0x00, 0x40]; // 0x400000 = halbe Vollaussteuerung
        Assert.Equal(0.5f, SampleConversion.ToFloat(pcm24, 24, false)[0], 5);
        Assert.Equal(-0.5f, SampleConversion.ToFloat(BitConverter.GetBytes(int.MinValue / 2), 32, false)[0], 5);
    }

    [Fact]
    public void Channels_are_remapped()
    {
        Assert.Equal([1f, 1f, 2f, 2f], SampleConversion.ToStereo([1, 2], 1));
        Assert.Equal([0.5f], SampleConversion.Remap([1, 0], 2, 1));
        Assert.Equal([1f, 2f, 4f, 5f], SampleConversion.Remap([1, 2, 3, 4, 5, 6], 3, 2));
    }

    [Fact]
    public void Resampler_keeps_frequency_and_level()
    {
        const int inRate = 48000, outRate = 44100;
        var sine = Enumerable.Range(0, inRate).Select(i => (float)Math.Sin(2 * Math.PI * 1000 * i / inRate)).ToArray();
        var resampler = new StreamingResampler(inRate, outRate, 1);
        var output = new List<float>();
        for (int i = 0; i < sine.Length; i += 480)
        {
            output.AddRange(resampler.Process(sine.AsSpan(i, 480))); // in kleinen Stücken wie vom Gerät
        }

        Assert.InRange(output.Count, outRate - 100, outRate + 10);
        var middle = output.Skip(2000).Take(40000).ToArray();
        double peak = middle.Max(v => Math.Abs(v));
        Assert.InRange(peak, 0.97, 1.03);

        // Frequenz über Nulldurchgänge: 1000 Hz bei 44,1 kHz
        int crossings = 0;
        for (int i = 1; i < middle.Length; i++)
        {
            if (middle[i - 1] < 0 && middle[i] >= 0)
            {
                crossings++;
            }
        }

        Assert.InRange(crossings * (double)outRate / middle.Length, 995, 1005);

        // Glatt: keine Sprünge an Stückgrenzen (Abweichung vom idealen Sinus klein)
        double phase = Math.Asin(Math.Clamp(middle[0], -1, 1));
        double maxStep = 0;
        for (int i = 1; i < middle.Length; i++)
        {
            maxStep = Math.Max(maxStep, Math.Abs(middle[i] - middle[i - 1]));
        }

        Assert.True(maxStep < 2 * Math.PI * 1000 / outRate * 1.05, $"größter Schritt {maxStep:0.000}");
        _ = phase;
    }

    [Fact]
    public void Resampler_is_transparent_for_equal_rates_and_handles_stereo()
    {
        var same = new StreamingResampler(44100, 44100, 2);
        Assert.Equal([1f, 2f], same.Process([1f, 2f]));

        var up = new StreamingResampler(22050, 44100, 2);
        var stereo = Enumerable.Range(0, 22050).SelectMany(i => new[] { 0.5f, -0.5f }).ToArray();
        var output = up.Process(stereo);
        var tail = output.Skip(200).Take(1000).ToArray();
        Assert.All(tail.Where((_, i) => i % 2 == 0), v => Assert.Equal(0.5f, v, 3));
        Assert.All(tail.Where((_, i) => i % 2 == 1), v => Assert.Equal(-0.5f, v, 3));
    }
}
