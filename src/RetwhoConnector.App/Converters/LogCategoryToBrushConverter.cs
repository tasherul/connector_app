using System.Globalization;
using System.Windows.Data;
using RetwhoConnector.Core.Models;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;

namespace RetwhoConnector.App.Converters;

public sealed class LogCategoryToBrushConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        string key = value is AgentLogCategory category
            ? category switch
            {
                AgentLogCategory.Success => "LogSuccessBrush",
                AgentLogCategory.Session => "LogWarningBrush",
                AgentLogCategory.Action => "LogActionBrush",
                AgentLogCategory.Error => "LogErrorBrush",
                _ => "LogGeneralBrush",
            }
            : "LogGeneralBrush";
        return WpfApplication.Current.TryFindResource(key) as WpfBrush
            ?? WpfBrushes.Transparent;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
