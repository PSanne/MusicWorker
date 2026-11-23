namespace MusicWorker.Core.Effects;

/// <summary>Ersetzt den Bereich durch Stille (die Länge bleibt gleich).</summary>
public sealed class MuteEffect : SampleEffect
{
    public override string Name => "Stummschalten";

    protected override void Process(Span<float> samples, int channels, int sampleRate) => samples.Clear();
}
