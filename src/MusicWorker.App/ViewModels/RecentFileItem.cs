using System.IO;
using System.Windows.Input;

namespace MusicWorker.App.ViewModels;

/// <summary>Ein Eintrag im Menü „Zuletzt geöffnet“.</summary>
public sealed record RecentFileItem(int Number, string Path, ICommand OpenCommand)
{
    /// <summary>Menütext, z. B. „_1 Song.mp3“ (Unterstriche im Namen werden maskiert).</summary>
    public string Header => $"_{Number} {System.IO.Path.GetFileName(Path).Replace("_", "__")}";
}
