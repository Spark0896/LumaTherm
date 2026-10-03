using System.Windows.Media;
using LumaTherm.App.Converters;
using LumaTherm.Core.Colors;
using Point = System.Windows.Point;
namespace LumaTherm.App.Controls;

internal static class ThermalGradientBrush
{
    public static LinearGradientBrush Create(ThermalProfile profile, double minimum, double maximum)
    {
        var engine = new ColorEngine(profile, minimum);
        var stops = new GradientStopCollection();
        const int samples = 240;
        for (var index = 0; index <= samples; index++)
        {
            var fraction = index / (double)samples;
            stops.Add(new(engine.Map(minimum + (maximum - minimum) * fraction).ToMediaColor(), fraction));
        }
        var brush = new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5));
        brush.Freeze();
        return brush;
    }
}
