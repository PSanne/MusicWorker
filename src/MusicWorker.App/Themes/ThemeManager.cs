using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using MusicWorker.App.Services;

namespace MusicWorker.App.Themes;

/// <summary>
/// Schaltet zwischen hellem und dunklem Design um – sofort und für alle offenen Fenster.
/// Das helle Design ist die gewohnte Windows-Optik; das dunkle ersetzt die Vorlagen der Bedienelemente.
/// </summary>
public static class ThemeManager
{
    private static readonly Uri LightUri = new("pack://application:,,,/MusicWorker;component/Themes/Light.xaml");
    private static readonly Uri DarkUri = new("pack://application:,,,/MusicWorker;component/Themes/Dark.xaml");
    private static bool _handlerRegistered;

    /// <summary>Ist gerade das dunkle Design aktiv?</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Wählt das Design; „Wie Windows“ folgt der Einstellung „App-Modus“ von Windows.</summary>
    public static void Apply(AppTheme theme)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        bool dark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.System => WindowsUsesDarkMode(),
            _ => false,
        };

        // Das bisherige Design-Wörterbuch (auch das aus App.xaml) ersetzen – bei mehreren gewinnt sonst das letzte.
        var merged = app.Resources.MergedDictionaries;
        foreach (var old in merged.Where(d => d.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true).ToArray())
        {
            merged.Remove(old);
        }

        merged.Add(new ResourceDictionary { Source = dark ? DarkUri : LightUri });
        IsDark = dark;

        if (!_handlerRegistered)
        {
            // Jedes neue Fenster (auch Dialoge) bekommt beim Laden das aktuelle Design.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window)
                {
                    ApplyTo(window);
                }
            }));
            _handlerRegistered = true;
        }

        foreach (Window window in app.Windows)
        {
            ApplyTo(window);
        }
    }

    /// <summary>Folgt Windows dem dunklen App-Modus? (Einstellungen → Personalisierung → Farben)</summary>
    public static bool WindowsUsesDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ApplyTo(Window window)
    {
        if (IsDark)
        {
            window.SetResourceReference(Control.BackgroundProperty, "Theme.WindowBackground");
            window.SetResourceReference(Control.ForegroundProperty, "Theme.Foreground");
        }
        else
        {
            window.ClearValue(Control.BackgroundProperty);
            window.ClearValue(Control.ForegroundProperty);
        }

        SetDarkTitleBar(window, IsDark);
    }

    /// <summary>Dunkle Titelleiste (Windows 10 ab 20H1 und Windows 11); ältere Versionen ignorieren das einfach.</summary>
    private static void SetDarkTitleBar(Window window, bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            int value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, 19, ref value, sizeof(int)); // ältere Windows-10-Versionen
            }
        }
        catch (Exception)
        {
            // Ohne DWM bleibt die Titelleiste einfach hell.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
