using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using MusicWorker.Core.Persistence;

namespace MusicWorker.App.Services;

/// <summary>Fragt bei GitHub nach der neuesten veröffentlichten Version. Es werden keine persönlichen Daten gesendet.</summary>
public static class UpdateService
{
    public const string LatestReleaseApi = "https://api.github.com/repos/PSanne/MusicWorker/releases/latest";
    public const string ReleasesPage = "https://github.com/PSanne/MusicWorker/releases";
    public const string ProjectPage = "https://github.com/PSanne/MusicWorker";

    /// <summary>Version dieses Programms (aus <c>Directory.Build.props</c>).</summary>
    public static Version CurrentVersion
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        }
    }

    public static async Task<UpdateInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"MusicWorker/{CurrentVersion}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        string json = await http.GetStringAsync(LatestReleaseApi, cancellationToken);
        return UpdateInfo.Parse(json);
    }
}

/// <summary>Öffnet Dateien und Internetadressen mit dem passenden Windows-Programm.</summary>
public static class Shell
{
    public static void Open(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
}
