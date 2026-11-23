using System.Globalization;
using MusicWorker.Core.Audio;
using MusicWorker.Core.Dsp;

namespace MusicWorker.Core.Effects;

/// <summary>Echo als Effekt (für „In Audio übernehmen“ und Export).</summary>
public sealed class DelayEffect : IAudioEffect
{
    private static readonly float ClipLimit = (float)Decibels.ToGain(-1.0);

    /// <param name="settings">Echo-Einstellungen.</param>
    /// <param name="tailSeconds">So viel wird am Ende angehängt, damit die Wiederholungen ausklingen (0 = Länge bleibt).</param>
    /// <param name="preventClipping">Bei Übersteuerung den Pegel auf -1 dBFS absenken.</param>
    public DelayEffect(DelaySettings settings, double tailSeconds = 0, bool preventClipping = true)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ArgumentOutOfRangeException.ThrowIfNegative(tailSeconds);
        TailSeconds = tailSeconds;
        PreventClipping = preventClipping;
    }

    public DelaySettings Settings { get; }

    public double TailSeconds { get; }

    public bool PreventClipping { get; }

    public string Name
    {
        get
        {
            var times = Settings.TimeMs.Distinct().ToArray();
            string time = times.Length == 1
                ? string.Format(CultureInfo.CurrentCulture, "{0:0} ms", times[0])
                : string.Format(CultureInfo.CurrentCulture, "L {0:0} ms, R {1:0} ms", Settings.TimeMs[0], Settings.TimeMs[1]);
            return $"Echo ({time}{(Settings.PingPong ? ", Ping-Pong" : string.Empty)})";
        }
    }

    public AudioBuffer Apply(AudioBuffer input)
    {
        ArgumentNullException.ThrowIfNull(input);
        int tailFrames = (int)Math.Round(TailSeconds * input.SampleRate);
        var samples = new float[(input.FrameCount + tailFrames) * input.Channels];
        input.Samples.CopyTo(samples);

        new DelayProcessor(input.SampleRate, input.Channels, Settings).Process(samples);
        if (PreventClipping)
        {
            CompressorEffect.Limit(samples, ClipLimit);
        }

        return new AudioBuffer(samples, input.SampleRate, input.Channels);
    }
}
