using System.Globalization;
using System.Windows.Data;

namespace RetwhoConnector.App.Converters;

public sealed class UppercaseTextConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        value is string text ? text.ToUpperInvariant() : string.Empty;

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
