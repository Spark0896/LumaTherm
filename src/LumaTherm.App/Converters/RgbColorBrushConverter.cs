using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LumaTherm.Core.Colors;
using MediaColor = System.Windows.Media.Color;

namespace LumaTherm.App.Converters;

public sealed class RgbColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is RgbColor color ? new SolidColorBrush(color.ToMediaColor()) : System.Windows.Media.Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal static class RgbColorMediaExtensions
{
    public static MediaColor ToMediaColor(this RgbColor color) => MediaColor.FromRgb(color.R, color.G, color.B);
}
