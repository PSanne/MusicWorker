using System.Windows.Input;

namespace MusicWorker.App.ViewModels;

/// <summary>Eintrag im Menü „Marker“: Klick springt zum Marker mit dem Index <see cref="Index"/>.</summary>
public sealed record MarkerMenuItem(string Header, int Index, ICommand JumpCommand);

public enum MarkerActionKind
{
    Jump,
    Move,
    Rename,
    Delete,
}

/// <summary>Was mit einem Marker passieren soll (Index in der sortierten Markerliste).</summary>
public sealed record MarkerAction(MarkerActionKind Kind, int Index, long Frame);
