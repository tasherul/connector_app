using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using RetwhoConnector.App.ViewModels;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace RetwhoConnector.App.Converters;

public sealed class DashboardSignalToBrushConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        string key = value is DashboardSignal signal
            ? signal switch
            {
                DashboardSignal.Healthy => "SuccessBrush",
                DashboardSignal.Warning => "WarningBrush",
                DashboardSignal.Error => "ErrorBrush",
                _ => "NeutralBrush",
            }
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
