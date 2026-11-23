using System.Globalization;

namespace MusicWorker.App.Helpers;

public static class TimeFormatter
{
    /// <summary>Formatiert z. B. als „1:05,250“ (Minuten:Sekunden,Millisekunden).</summary>
    public static string Format(TimeSpan time, bool withMilliseconds = true)
    {
        var culture = CultureInfo.CurrentCulture;
        int minutes = (int)time.TotalMinutes;
        return withMilliseconds
            ? string.Format(culture, "{0}:{1:00}{2}{3:000}", minutes, time.Seconds,
                culture.NumberFormat.NumberDecimalSeparator, time.Milliseconds)
            : string.Format(culture, "{0}:{1:00}", minutes, time.Seconds);
    }
}
