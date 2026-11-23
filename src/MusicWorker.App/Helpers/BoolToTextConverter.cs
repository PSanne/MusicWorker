using System.Globalization;
using System.Windows.Data;

namespace MusicWorker.App.Helpers;

/// <summary>Wählt je nach bool-Wert einen von zwei Texten (z. B. „Stärke Links“ bei Stereo, sonst „Stärke“).</summary>
public sealed class BoolToTextConverter : IValueConverter
{
    public string TrueText { get; set; } = string.Empty;

    public string FalseText { get; set; } = string.Empty;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? TrueText : FalseText;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
