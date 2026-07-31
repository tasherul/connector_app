using System.Globalization;
using System.Collections.Generic;

namespace System.Windows
{
    public sealed class Application
    {
        private readonly Dictionary<string, object> _resources = [];

        public static Application? Current { get; set; }

        public object? TryFindResource(string key) =>
            _resources.GetValueOrDefault(key);

        public void SetResource(string key, object value) => _resources[key] = value;
    }
}

namespace System.Windows.Data
{
    public interface IValueConverter
    {
        object Convert(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture);

        object ConvertBack(
            object value,
            Type targetType,
            object parameter,
            CultureInfo culture);
    }
}

namespace System.Windows.Media
{
    public class Brush;

    public static class Brushes
    {
        public static Brush Gray { get; } = new();
    }
}
