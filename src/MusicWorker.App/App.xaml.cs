using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using MusicWorker.App.Audio;
using MusicWorker.App.Services;
using MusicWorker.App.ViewModels;
using NAudio.MediaFoundation;

namespace MusicWorker.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
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
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();

        // Datei aus der Kommandozeile öffnen (z. B. „Öffnen mit …“ im Explorer).
        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
        {
            _ = viewModel.OpenFileAsync(e.Args[0]);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MediaFoundationApi.Shutdown();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Ein unerwarteter Fehler ist aufgetreten:\n\n{e.Exception.Message}",
            "MusicWorker", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
