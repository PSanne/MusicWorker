using System.Globalization;
using System.Windows.Data;
using MusicWorker.Core.Synthesis;

namespace MusicWorker.App.Helpers;

/// <summary>Zeigt den deutschen Namen eines eingebauten Instruments (z. B. „Klavier“).</summary>
public sealed class InstrumentNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is BuiltInInstrument instrument ? Instruments.NameOf(instrument) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
