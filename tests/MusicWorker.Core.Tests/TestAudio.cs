using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Tests;

internal static class TestAudio
{
    /// <summary>Mono-Puffer, dessen Samples einfach die Werte 0, 1, 2, … (skaliert) sind – gut nachprüfbar.</summary>
    public static AudioBuffer Ramp(int frames, int channels = 1, int sampleRate = 1000)
    {
        var samples = new float[frames * channels];
        for (int frame = 0; frame < frames; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                samples[frame * channels + channel] = frame + channel * 0.5f;
            }
        }

        return new AudioBuffer(samples, sampleRate, channels);
    }

    public static float[] Frames(AudioBuffer buffer, int channel = 0) =>
        Enumerable.Range(0, buffer.FrameCount).Select(f => buffer.GetSample(f, channel)).ToArray();
}
