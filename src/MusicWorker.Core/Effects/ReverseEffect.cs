namespace MusicWorker.Core.Effects;

/// <summary>Kehrt das Audio um (spielt rückwärts). Die Kanäle bleiben dabei auf ihrer Seite.</summary>
public sealed class ReverseEffect : SampleEffect
{
    public override string Name => "Umkehren";

    protected override void Process(Span<float> samples, int channels, int sampleRate)
    {
        int frames = samples.Length / channels;
        for (int a = 0, b = frames - 1; a < b; a++, b--)
        {
            for (int c = 0; c < channels; c++)
            {
                (samples[a * channels + c], samples[b * channels + c]) =
                    (samples[b * channels + c], samples[a * channels + c]);
            }
        }
    }
}
