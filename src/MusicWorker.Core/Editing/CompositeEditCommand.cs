using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Editing;

/// <summary>Fasst mehrere Befehle zu einem Rückgängig-Schritt zusammen.</summary>
public sealed class CompositeEditCommand : IEditCommand
{
    private readonly IReadOnlyList<IEditCommand> _commands;

    public CompositeEditCommand(string description, params IEditCommand[] commands)
    {
        Description = description;
        _commands = commands;
    }

    public string Description { get; }

    public AudioBuffer Apply(AudioBuffer buffer)
    {
        foreach (var command in _commands)
        {
            buffer = command.Apply(buffer);
        }

        return buffer;
    }

    public long LengthAfter(long lengthBefore)
    {
        foreach (var command in _commands)
        {
            lengthBefore = command.LengthAfter(lengthBefore);
        }

        return lengthBefore;
    }

    public IReadOnlyList<Marker> MapMarkers(IReadOnlyList<Marker> markers, long lengthBefore)
    {
        foreach (var command in _commands)
        {
            markers = command.MapMarkers(markers, lengthBefore);
            lengthBefore = command.LengthAfter(lengthBefore);
        }

        return markers;
    }

    public AudioBuffer Revert(AudioBuffer buffer)
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            buffer = _commands[i].Revert(buffer);
        }

        return buffer;
    }
}
