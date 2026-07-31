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
            IsAny(
                status,
                "Configured",
                "Connected",
                "Active",
                "Healthy",
                "Registered",
                "Authenticated",
                "Completed")
                ? "SuccessBrush"
                : StartsWithAny(
                    status,
                    "Connecting",
                    "Reconnecting",
                    "Disconnecting",
                    "Registering",
                    "Refreshing",
                    "Idle",
                    "Degraded")
                    ? "WarningBrush"
                    : ContainsAny(
                        status,
                        "Missing",
                        "Error",
                        "Failed",
                        "Changed",
                        "Replaced",
                        "Invalid")
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

    private static bool IsAny(string value, params string[] candidates) =>
        candidates.Any(candidate =>
            value.Equals(candidate, StringComparison.OrdinalIgnoreCase));

    private static bool StartsWithAny(
        string value,
        params string[] candidates) =>
        candidates.Any(candidate =>
            value.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(
        string value,
        params string[] candidates) =>
        candidates.Any(candidate =>
            value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
