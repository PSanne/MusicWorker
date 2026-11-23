using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MusicWorker.App.Helpers;

/// <summary>true → ausgeblendet, false → sichtbar (Gegenstück zu BooleanToVisibilityConverter).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}
