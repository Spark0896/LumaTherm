using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LumaTherm.App.Converters;
using LumaTherm.Core.Colors;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using UserControl = System.Windows.Controls.UserControl;

namespace LumaTherm.App.Controls;

public partial class ThermalGradientBar : UserControl
{
    public static readonly DependencyProperty ColdColorProperty = Register(nameof(ColdColor), typeof(RgbColor), ThermalProfile.Default.ColdColor);
    public static readonly DependencyProperty WarmColorProperty = Register(nameof(WarmColor), typeof(RgbColor), ThermalProfile.Default.WarmColor);
    public static readonly DependencyProperty HotColorProperty = Register(nameof(HotColor), typeof(RgbColor), ThermalProfile.Default.HotColor);
    public static readonly DependencyProperty ColdTemperatureProperty = Register(nameof(ColdTemperature), typeof(double), 35d);
    public static readonly DependencyProperty WarmTemperatureProperty = Register(nameof(WarmTemperature), typeof(double), 65d);
    public static readonly DependencyProperty ProfileProperty = Register(nameof(Profile), typeof(ThermalProfile), null!);
    public static readonly DependencyProperty HotTemperatureProperty = Register(nameof(HotTemperature), typeof(double), 85d);
    public static readonly DependencyProperty CurrentTemperatureProperty = Register(nameof(CurrentTemperature), typeof(double), double.NaN);

    public ThermalGradientBar() => InitializeComponent();

    public RgbColor ColdColor { get => (RgbColor)GetValue(ColdColorProperty); set => SetValue(ColdColorProperty, value); }
    public RgbColor WarmColor { get => (RgbColor)GetValue(WarmColorProperty); set => SetValue(WarmColorProperty, value); }
    public RgbColor HotColor { get => (RgbColor)GetValue(HotColorProperty); set => SetValue(HotColorProperty, value); }
    public double ColdTemperature { get => (double)GetValue(ColdTemperatureProperty); set => SetValue(ColdTemperatureProperty, value); }
    public double WarmTemperature { get => (double)GetValue(WarmTemperatureProperty); set => SetValue(WarmTemperatureProperty, value); }
    public double HotTemperature { get => (double)GetValue(HotTemperatureProperty); set => SetValue(HotTemperatureProperty, value); }
    public double CurrentTemperature { get => (double)GetValue(CurrentTemperatureProperty); set => SetValue(CurrentTemperatureProperty, value); }
    public ThermalProfile? Profile { get => (ThermalProfile?)GetValue(ProfileProperty); set => SetValue(ProfileProperty, value); }
    internal IReadOnlyList<GradientStop> RenderedStops => [.. CreateGradient().GradientStops];
    internal IReadOnlyList<string> RenderedLabels => Profile is null ? [$"{ColdTemperature:0.#}° Холодно", $"{WarmTemperature:0.#}° Тепло", $"{HotTemperature:0.#}° Пик"] : [.. Profile.Points.Select(point => $"{point.Temperature:0.#}°")];

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var barRect = new Rect(0, 26, Math.Max(0, ActualWidth), 10);
        drawingContext.DrawRoundedRectangle(CreateGradient(), null, barRect, 5, 5);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var labels = RenderedLabels;
        for (var i = 0; i < labels.Count; i++)
        {
            var text = new FormattedText(labels[i], CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI Variable"), 9, new SolidColorBrush(Color.FromRgb(0x77, 0x82, 0x8C)), pixelsPerDip);
            var x = labels.Count == 1 ? (ActualWidth - text.Width) / 2 : i * (ActualWidth - text.Width) / (labels.Count - 1);
            drawingContext.DrawText(text, new Point(Math.Max(0, x), 44));
        }

        var points = Profile?.Points;
        var cold = points?[0].Temperature ?? ColdTemperature;
        var hot = points?[^1].Temperature ?? HotTemperature;
        if (double.IsFinite(CurrentTemperature) && hot > cold)
        {
            var fraction = Math.Clamp((CurrentTemperature - cold) / (hot - cold), 0, 1);
            var x = ActualWidth * fraction;
            var color = Profile is null ? InterpolatedMarkerColor(fraction) : new ColorEngine(Profile, CurrentTemperature).Map(CurrentTemperature).ToMediaColor();
            drawingContext.DrawEllipse(new SolidColorBrush(color), new Pen(new SolidColorBrush(Color.FromRgb(0xF1, 0xF8, 0xFB)), 3), new Point(x, 31), 7, 7);
        }
    }

    private LinearGradientBrush CreateGradient()
    {
        IReadOnlyList<ThermalPoint> points = Profile?.Points ??
        [
            new ThermalPoint(ColdTemperature, ColdColor),
            new ThermalPoint(WarmTemperature, WarmColor),
            new ThermalPoint(HotTemperature, HotColor),
        ];
        var cold = points[0].Temperature;
        var hot = points[^1].Temperature;
        var stops = new GradientStopCollection();
        foreach (var point in points)
        {
            var offset = hot > cold ? Math.Clamp((point.Temperature - cold) / (hot - cold), 0, 1) : 0;
            stops.Add(new GradientStop(point.Color.ToMediaColor(), offset));
        }

        return new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5));
    }

    private Color InterpolatedMarkerColor(double fraction) => fraction <= 0.5 ? ColdColor.ToMediaColor() : fraction < 0.82 ? WarmColor.ToMediaColor() : HotColor.ToMediaColor();

    private static DependencyProperty Register(string name, Type type, object defaultValue) => DependencyProperty.Register(
        name, type, typeof(ThermalGradientBar), new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));
}
