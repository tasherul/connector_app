using System.Globalization;
using System.Windows.Data;

namespace RetwhoConnector.App.Converters;

public sealed class WidthToStatusColumnCountConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is double width && double.IsFinite(width) && width >= 920
            ? 4
            : 2;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
