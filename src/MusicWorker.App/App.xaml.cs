using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using MusicWorker.App.Audio;
using MusicWorker.App.Services;
using MusicWorker.App.Themes;
using MusicWorker.App.ViewModels;
using MusicWorker.Core.Persistence;
using NAudio.MediaFoundation;

namespace MusicWorker.App;

public partial class App : Application
{
    private RecoverySession? _recovery;
    private AutosaveService? _autosave;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Zahlen und Zeiten in der Oberfläche im Format des Systems anzeigen (z. B. Komma statt Punkt).
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        // Windows Media Foundation wird für FLAC/M4A/WMA-Import und MP3-Export benötigt.
        MediaFoundationApi.Startup();
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var viewModel = new MainViewModel(
            new WpfDialogService(), new AudioFileService(), new PlaybackService(), new SettingsService());
        ThemeManager.Apply(viewModel.Theme); // vor dem Fenster, damit es gleich im richtigen Design erscheint
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();

        // Automatische Sicherung: verwaiste Sicherungen (Absturz) suchen, dann eine eigene Sitzung beginnen.
        IReadOnlyList<AbandonedSession> abandoned = [];
        try
        {
            var store = new RecoveryStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MusicWorker", "Wiederherstellung"));
            abandoned = store.FindAbandoned(IsRunning);
            using var self = Process.GetCurrentProcess();
            _recovery = store.StartSession(new SessionInfo(Environment.ProcessId, self.StartTime.ToUniversalTime().Ticks));
            _autosave = new AutosaveService(viewModel, _recovery, TimeSpan.FromMinutes(2));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ohne Sicherungsordner läuft das Programm trotzdem – nur eben ohne automatische Sicherung.
        }

        await viewModel.OfferRecoveryAsync(abandoned);

        // Datei aus der Kommandozeile öffnen (Doppelklick auf ein Projekt, „Öffnen mit …“ im Explorer).
        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
        {
            await viewModel.OpenPathAsync(e.Args[0]);
        }

        await viewModel.CheckForUpdatesOnStartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _autosave?.Dispose();
        _recovery?.End(); // normales Ende: Sicherung wird nicht mehr gebraucht
        MediaFoundationApi.Shutdown();
        base.OnExit(e);
    }

    /// <summary>Läuft die Sitzung noch (anderes MusicWorker-Fenster)? Prozess-ID und Startzeit müssen passen.</summary>
    private static bool IsRunning(SessionInfo session)
    {
        try
        {
            using var process = Process.GetProcessById(session.ProcessId);
            return Math.Abs(process.StartTime.ToUniversalTime().Ticks - session.StartTicks) < TimeSpan.TicksPerSecond;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Ein unerwarteter Fehler ist aufgetreten:\n\n{e.Exception.Message}",
            "MusicWorker", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
