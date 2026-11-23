using MusicWorker.Core.Audio;

namespace MusicWorker.Core.Editing;

/// <summary>
/// Eine rückgängig machbare Bearbeitung (Command-Pattern).
/// <see cref="Apply"/> muss wiederholbar sein, damit „Wiederholen“ funktioniert.
/// </summary>
public interface IEditCommand
{
    /// <summary>Anzeigename, z. B. „Ausschneiden“ – erscheint als „Rückgängig: Ausschneiden“.</summary>
    string Description { get; }

    AudioBuffer Apply(AudioBuffer buffer);

    AudioBuffer Revert(AudioBuffer buffer);
}
