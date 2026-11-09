using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace QuickConnect
{
    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is bool b ? !b : (object)false;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => v is bool b ? !b : (object)false;
    }

    /// <summary>Converts an int? battery level to a coloured bar string "▓▓▓▒▒" style glyph.</summary>
    public sealed class BatteryIconConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            if (v is not int pct) return "\uE996"; // unknown battery glyph
            return pct switch
            {
                <= 10 => "\uE850", // battery 0
                <= 20 => "\uE851", // battery 1
                <= 40 => "\uE852", // battery 2
                <= 60 => "\uE853", // battery 3
                <= 80 => "\uE854", // battery 4
                _     => "\uE83F", // battery full (Segoe MDL2)
            };
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotSupportedException();
    }

    /// <summary>Returns red brush for low battery (≤20%), gray otherwise.</summary>
    public sealed class BatteryColorConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            if (v is int pct && pct <= 20)
                return new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));
            return new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61));
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotSupportedException();
    }

    /// <summary>Converts a nullable int to its string or "Unavailable".</summary>
    public sealed class NullableIntConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is int i ? $"{i}%" : "Unavailable";
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotSupportedException();
    }
}
