using System.Globalization;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Effects;

/// <summary>Fügt Hall hinzu – pro Kanal unterschiedlich stark möglich.</summary>
public sealed class ReverbEffect : IAudioEffect
{
    private static readonly float ClipLimit = (float)Decibels.ToGain(-1.0);

    /// <param name="settings">Hall-Einstellungen.</param>
    /// <param name="tailSeconds">So viel wird am Ende angehängt, damit der Hall ausklingen kann (0 = Länge bleibt).</param>
    /// <param name="preventClipping">Bei Übersteuerung den Pegel auf -1 dBFS absenken.</param>
    /// <param name="channelOffset">Kanalnummer des ersten Kanals (für „nur rechts“).</param>
    public ReverbEffect(ReverbSettings settings, double tailSeconds = 0, bool preventClipping = true, int channelOffset = 0)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ArgumentOutOfRangeException.ThrowIfNegative(tailSeconds);
        TailSeconds = tailSeconds;
        PreventClipping = preventClipping;
        ChannelOffset = channelOffset;
    }

    public ReverbSettings Settings { get; }

    public double TailSeconds { get; }

    public bool PreventClipping { get; }

    public int ChannelOffset { get; }

    public string Name
    {
        get
        {
            var amounts = Settings.AmountPercent;
            return amounts.Distinct().Count() == 1
                ? string.Format(CultureInfo.CurrentCulture, "Hall {0:0} %", amounts[0])
                : string.Format(CultureInfo.CurrentCulture, "Hall (L {0:0} %, R {1:0} %)", amounts[0], amounts[1]);
        }
    }

    public AudioBuffer Apply(AudioBuffer input)
    {
        ArgumentNullException.ThrowIfNull(input);
        int tailFrames = (int)Math.Round(TailSeconds * input.SampleRate);
        var samples = new float[(input.FrameCount + tailFrames) * input.Channels];
        input.Samples.CopyTo(samples);

        new ReverbProcessor(input.SampleRate, input.Channels, Settings, ChannelOffset).Process(samples);

        if (PreventClipping)
        {
            float peak = 0;
            foreach (float sample in samples)
            {
                peak = Math.Max(peak, Math.Abs(sample));
            }

            if (peak > ClipLimit)
            {
                float factor = ClipLimit / peak;
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] *= factor;
                }
            }
        }

        return new AudioBuffer(samples, input.SampleRate, input.Channels);
    }
}
