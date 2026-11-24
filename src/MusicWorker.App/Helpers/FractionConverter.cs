using System.Globalization;
using System.Windows.Data;

namespace MusicWorker.App.Helpers;

/// <summary>Multipliziert eine Zahl mit dem Parameter (z. B. „höchstens 70 % der Fensterhöhe“).</summary>
public sealed class FractionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double factor = double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double f) ? f : 1;
        return value is double number && number > 0 ? number * factor : double.PositiveInfinity;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
