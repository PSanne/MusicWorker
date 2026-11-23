using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Editing;

/// <summary>
/// Ersetzt den kompletten Puffer – auch mit anderem Format (z. B. Stereo → Mono).
/// Speichert dafür den alten Puffer vollständig.
/// </summary>
public sealed class ReplaceBufferCommand : IEditCommand
{
    private readonly AudioBuffer _replacement;
    private AudioBuffer? _previous;

    public ReplaceBufferCommand(string description, AudioBuffer replacement)
    {
        Description = description;
        _replacement = replacement ?? throw new ArgumentNullException(nameof(replacement));
    }

    public string Description { get; }

    public AudioBuffer Apply(AudioBuffer buffer)
    {
        _previous = buffer;
        return _replacement;
    }

    public AudioBuffer Revert(AudioBuffer buffer) =>
        _previous ?? throw new InvalidOperationException("Der Befehl wurde noch nicht ausgeführt.");
}
