using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Effects;

/// <summary>
/// Wendet einen Effekt nur auf bestimmte Kanäle an (z. B. nur links); die übrigen bleiben unverändert.
/// Nur für Effekte geeignet, die die Länge nicht verändern.
/// </summary>
public sealed class ChannelEffect : IAudioEffect
{
    private readonly IAudioEffect _inner;

    public ChannelEffect(IAudioEffect inner, IReadOnlyList<int> channels)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        ArgumentNullException.ThrowIfNull(channels);
        if (channels.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Kanal muss gewählt sein.", nameof(channels));
        }

        Channels = channels.Distinct().Order().ToArray();
    }

    public IReadOnlyList<int> Channels { get; }

    public string Name => $"{_inner.Name} ({DescribeChannels(Channels)})";

    public static string DescribeChannels(IReadOnlyList<int> channels) => channels switch
    {
        [0] => "nur links",
        [1] => "nur rechts",
        _ => "Kanäle " + string.Join(", ", channels.Select(c => c + 1)),
    };

    public AudioBuffer Apply(AudioBuffer input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Channels.Count >= input.Channels)
        {
            return _inner.Apply(input);
        }

        var processed = _inner.Apply(input.ExtractChannels(Channels));
        if (processed.FrameCount != input.FrameCount)
        {
            throw new InvalidOperationException(
                $"„{_inner.Name}“ ändert die Länge und kann nur auf alle Kanäle gleichzeitig angewendet werden.");
        }

        return input.ReplaceChannels(Channels, processed);
    }
}
