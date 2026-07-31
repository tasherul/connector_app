using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace RetwhoConnector.App.Converters;

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        string status = value?.ToString() ?? string.Empty;
        string key =
            status.StartsWith("Not", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Disconnected", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? "NeutralBrush"
                : status.Contains("Registered", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Authenticated", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Completed", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Configured", StringComparison.OrdinalIgnoreCase)
                ? "SuccessBrush"
                : status.Contains("Connecting", StringComparison.OrdinalIgnoreCase) ||
                  status.Contains("Registering", StringComparison.OrdinalIgnoreCase) ||
                  status.Contains("Refreshing", StringComparison.OrdinalIgnoreCase)
                    ? "WarningBrush"
                    : status.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                      status.Contains("Changed", StringComparison.OrdinalIgnoreCase) ||
                      status.Contains("Replaced", StringComparison.OrdinalIgnoreCase)
                        ? "ErrorBrush"
                        : "NeutralBrush";
        return WpfApplication.Current.TryFindResource(key) as WpfBrush
            ?? WpfBrushes.Gray;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
