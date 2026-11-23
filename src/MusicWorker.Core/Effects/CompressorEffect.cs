using System.Globalization;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Effects;

/// <summary>Kompressor als Effekt (für „In Audio übernehmen“ und Export).</summary>
public sealed class CompressorEffect : IAudioEffect
{
    private static readonly float ClipLimit = (float)Decibels.ToGain(-1.0);

    public CompressorEffect(CompressorSettings settings, bool preventClipping = true)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        PreventClipping = preventClipping;
    }

    public CompressorSettings Settings { get; }

    public bool PreventClipping { get; }

    public string Name => string.Format(CultureInfo.CurrentCulture, "Kompressor ({0:0} dB, {1:0.#}:1)",
        Settings.ThresholdDb, Settings.Ratio);

    public AudioBuffer Apply(AudioBuffer input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var samples = input.Samples.ToArray();
        new CompressorProcessor(input.SampleRate, input.Channels, Settings).Process(samples);
        if (PreventClipping)
        {
            Limit(samples, ClipLimit);
        }

        return new AudioBuffer(samples, input.SampleRate, input.Channels);
    }

    internal static void Limit(float[] samples, float limit)
    {
        float peak = 0;
        foreach (float sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        if (peak > limit)
        {
            float factor = limit / peak;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] *= factor;
            }
        }
    }
}
