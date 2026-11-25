namespace MusicWorker.Core.Audio;

/// <summary>Umwandlung roher Audiodaten von Aufnahmegeräten in Float-Samples.</summary>
public static class SampleConversion
{
    /// <summary>Bytes (PCM 8/16/24/32 Bit oder Float 32 Bit, verschachtelt) in Float-Samples −1 … 1.</summary>
    public static float[] ToFloat(ReadOnlySpan<byte> data, int bitsPerSample, bool isFloat)
    {
        int bytes = bitsPerSample / 8;
        if (bytes is < 1 or > 4)
        {
            throw new NotSupportedException($"{bitsPerSample} Bit werden nicht unterstützt.");
        }

        int count = data.Length / bytes;
        var result = new float[count];
        for (int i = 0; i < count; i++)
        {
            var s = data.Slice(i * bytes, bytes);
            result[i] = (isFloat, bytes) switch
            {
                (true, 4) => BitConverter.ToSingle(s),
                (_, 1) => (s[0] - 128) / 128f,
                (_, 2) => BitConverter.ToInt16(s) / 32768f,
                (_, 3) => ((s[0] << 8) | (s[1] << 16) | (s[2] << 24)) / 2147483648f,
                _ => BitConverter.ToInt32(s) / 2147483648f,
            };
        }

        return result;
    }

    /// <summary>Kanalzahl anpassen: Mono wird verdoppelt, bei weniger Zielkanälen werden die ersten beiden gemittelt.</summary>
    public static float[] Remap(float[] interleaved, int fromChannels, int toChannels)
    {
        ArgumentNullException.ThrowIfNull(interleaved);
        fromChannels = Math.Max(1, fromChannels);
        toChannels = Math.Max(1, toChannels);
        if (fromChannels == toChannels)
        {
            return interleaved;
        }

        int frames = interleaved.Length / fromChannels;
        var result = new float[frames * toChannels];
        for (int f = 0; f < frames; f++)
        {
            int source = f * fromChannels;
            if (toChannels == 1)
            {
                result[f] = fromChannels == 1 ? interleaved[source] : (interleaved[source] + interleaved[source + 1]) * 0.5f;
                continue;
            }

            for (int c = 0; c < toChannels; c++)
            {
                result[(f * toChannels) + c] = interleaved[source + Math.Min(c, fromChannels - 1)];
            }
        }

        return result;
    }

    /// <summary>Mono oder Stereo in Stereo (für das Mithören).</summary>
    public static float[] ToStereo(float[] interleaved, int channels) => Remap(interleaved, channels, 2);
}
