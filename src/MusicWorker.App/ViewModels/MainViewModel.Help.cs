using System.ComponentModel;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MusicWorker.App.Services;
using MusicWorker.Core.Persistence;

namespace MusicWorker.App.ViewModels;

/// <summary>Menü „Hilfe“: Handbuch, Tastenkürzel, Update-Hinweis und „Über MusicWorker“.</summary>
public sealed partial class MainViewModel
{
    public const string ManualFileName = "Handbuch.pdf";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(UpdateText))]
    private UpdateInfo? _availableUpdate;

    /// <summary>Die Hinweisleiste „Neue Version verfügbar“ wird angezeigt.</summary>
    public bool HasUpdate => AvailableUpdate is not null;

    public string UpdateText => AvailableUpdate is { } update
        ? $"MusicWorker {update.Version.ToString(3)} ist erschienen (du hast {UpdateService.CurrentVersion.ToString(3)})."
        : string.Empty;

    public string VersionText => $"Version {UpdateService.CurrentVersion.ToString(3)}";

    /// <summary>Beim Start einmal täglich nach Updates suchen (Menü „Hilfe“).</summary>
    public bool IsUpdateCheckEnabled
    {
        get => _settings.Settings.UpdateCheckEnabled;
        set
        {
            if (_settings.Settings.UpdateCheckEnabled != value)
            {
                _settings.Settings.UpdateCheckEnabled = value;
                _settings.Save();
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Wird von der App beim Start aufgerufen: höchstens einmal am Tag, still im Hintergrund.</summary>
    public async Task CheckForUpdatesOnStartAsync()
    {
        if (!IsUpdateCheckEnabled || DateTime.UtcNow - _settings.Settings.LastUpdateCheck < TimeSpan.FromDays(1))
        {
            return;
        }

        try
        {
            var latest = await UpdateService.GetLatestAsync();
            _settings.Settings.LastUpdateCheck = DateTime.UtcNow;
            _settings.Save();
            if (latest is not null && latest.IsNewerThan(UpdateService.CurrentVersion))
            {
                AvailableUpdate = latest;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // Kein Internet oder GitHub nicht erreichbar – beim nächsten Start erneut versuchen.
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        StatusText = "Suche nach einer neuen Version …";
        try
        {
            var latest = await UpdateService.GetLatestAsync();
            _settings.Settings.LastUpdateCheck = DateTime.UtcNow;
            _settings.Save();
            if (latest is not null && latest.IsNewerThan(UpdateService.CurrentVersion))
            {
                AvailableUpdate = latest;
                StatusText = UpdateText;
            }
            else
            {
                StatusText = "Du hast die neueste Version.";
                _dialogs.ShowInfo($"Du hast die neueste Version von MusicWorker ({UpdateService.CurrentVersion.ToString(3)}).", "Nach Updates suchen");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            StatusText = "Update-Prüfung fehlgeschlagen.";
            _dialogs.ShowError("GitHub ist gerade nicht erreichbar. Bitte prüfe die Internetverbindung und versuche es später noch einmal.\n\n" + ex.Message);
        }
    }

    /// <summary>Setup der neuen Version herunterladen (öffnet den Browser).</summary>
    [RelayCommand]
    private void DownloadUpdate()
    {
        if (AvailableUpdate is { } update)
        {
            OpenLink(update.SetupUrl ?? (string.IsNullOrEmpty(update.PageUrl) ? UpdateService.ReleasesPage : update.PageUrl));
        }
    }

    [RelayCommand]
    private void ShowReleaseNotes() =>
        OpenLink(AvailableUpdate is { PageUrl: { Length: > 0 } page } ? page : UpdateService.ReleasesPage);

    [RelayCommand]
    private void DismissUpdate() => AvailableUpdate = null;

    [RelayCommand]
    private void OpenManual()
    {
        string path = Path.Combine(AppContext.BaseDirectory, ManualFileName);
        if (!File.Exists(path))
        {
            _dialogs.ShowError($"Das Handbuch wurde nicht gefunden:\n{path}\n\nDu findest es auch auf {UpdateService.ProjectPage}.");
            return;
        }

        OpenLink(path);
    }

    [RelayCommand]
    private void OpenProjectPage() => OpenLink(UpdateService.ProjectPage);

    [RelayCommand]
    private void ShowAbout() => _dialogs.ShowInfo(
        $"MusicWorker {UpdateService.CurrentVersion.ToString(3)}\n\n" +
        "Musik und Sprache bearbeiten, mischen, Noten schreiben und aufnehmen.\n\n" +
        "Verwendete Bibliotheken: NAudio, MeltySynth, CommunityToolkit.Mvvm, .NET Ogg Vorbis Encoder, NVorbis (alle MIT-Lizenz).\n\n" +
        UpdateService.ProjectPage,
        "Über MusicWorker");

    private void OpenLink(string target)
    {
        try
        {
            Shell.Open(target);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            _dialogs.ShowError($"„{target}“ konnte nicht geöffnet werden.\n\n{ex.Message}");
        }
    }
}
