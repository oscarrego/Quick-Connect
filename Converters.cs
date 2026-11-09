using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace QuickConnect
{
    /// <summary>Flips a bool — used to disable the Refresh button while refreshing.</summary>
    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : (object)false;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : (object)false;
    }

    /// <summary>Converts a hex-color string like "#2E7D32" to a Color struct for use in XAML SolidColorBrush.Color binding.</summary>
    public sealed class HexToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex)
            {
                try { return (Color)ColorConverter.ConvertFromString(hex); }
                catch { }
            }
            return Colors.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Converts a hex-color string to a SolidColorBrush — for Foreground bindings.</summary>
    public sealed class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex)
            {
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
                catch { }
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
