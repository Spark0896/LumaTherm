using System.Globalization;
using System.Windows.Data;
using LumaTherm.Core.Colors;

namespace LumaTherm.App.Converters;

public sealed class RgbColorHexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is RgbColor color ? color.ToHex() : "—";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
