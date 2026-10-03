using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using LumaTherm.App.Converters;
using LumaTherm.Core.Colors;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using FontFamily = System.Windows.Media.FontFamily;
using UserControl = System.Windows.Controls.UserControl;

namespace LumaTherm.App.Controls;

public partial class TemperatureRing : UserControl
{
    public static readonly DependencyProperty TemperatureProperty = DependencyProperty.Register(
        nameof(Temperature), typeof(double), typeof(TemperatureRing),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender, OnAccessibleValueChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(TemperatureRing),
        new FrameworkPropertyMetadata(35d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(TemperatureRing),
        new FrameworkPropertyMetadata(85d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DisplayColorProperty = DependencyProperty.Register(
        nameof(DisplayColor), typeof(RgbColor), typeof(TemperatureRing),
        new FrameworkPropertyMetadata(ThermalProfile.Default.ColdColor, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty NowLabelProperty = DependencyProperty.Register(
        nameof(NowLabel), typeof(string), typeof(TemperatureRing),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnLocalizedTextChanged));

    public static readonly DependencyProperty UnavailableTextProperty = DependencyProperty.Register(
        nameof(UnavailableText), typeof(string), typeof(TemperatureRing),
        new FrameworkPropertyMetadata(string.Empty, OnLocalizedTextChanged));

    public static readonly DependencyProperty AccessibleNameFormatProperty = DependencyProperty.Register(
        nameof(AccessibleNameFormat), typeof(string), typeof(TemperatureRing),
        new FrameworkPropertyMetadata("{0}", OnLocalizedTextChanged));

    public TemperatureRing()
    {
        InitializeComponent();
        SetResourceReference(NowLabelProperty, "Dashboard.GpuNow");
        SetResourceReference(UnavailableTextProperty, "Accessibility.TemperatureUnavailable");
        SetResourceReference(AccessibleNameFormatProperty, "Accessibility.GpuTemperatureFormat");
        UpdateAccessibleName();
    }

    public double Temperature { get => (double)GetValue(TemperatureProperty); set => SetValue(TemperatureProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public RgbColor DisplayColor { get => (RgbColor)GetValue(DisplayColorProperty); set => SetValue(DisplayColorProperty, value); }
    public string NowLabel { get => (string)GetValue(NowLabelProperty); set => SetValue(NowLabelProperty, value); }
    public string UnavailableText { get => (string)GetValue(UnavailableTextProperty); set => SetValue(UnavailableTextProperty, value); }
    public string AccessibleNameFormat { get => (string)GetValue(AccessibleNameFormatProperty); set => SetValue(AccessibleNameFormatProperty, value); }
    internal string AccessibleValue => double.IsFinite(Temperature) ? $"{Temperature.ToString("0.#", CultureInfo.InvariantCulture)} °C" : UnavailableText;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var diameter = Math.Max(0, Math.Min(ActualWidth, ActualHeight) - 18);
        if (diameter <= 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = diameter / 2;
        var trackPen = RoundedPen(new SolidColorBrush(Color.FromRgb(0x32, 0x38, 0x3E)), 10);
        var color = DisplayColor.ToMediaColor();
        var valuePen = RoundedPen(new SolidColorBrush(color), 10);
        DrawArc(drawingContext, center, radius, 135, 270, trackPen);

        var denominator = Math.Max(0.001, Maximum - Minimum);
        var fraction = double.IsFinite(Temperature) ? Math.Clamp((Temperature - Minimum) / denominator, 0, 1) : 0;
        var sweep = 270 * fraction;
        if (sweep > 0.01) DrawArc(drawingContext, center, radius, 135, sweep, valuePen);

        var marker = PointOnCircle(center, radius, 135 + sweep);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var valueText = double.IsFinite(Temperature) ? $"{Temperature:0.#}°C" : "—°C";
        DrawCenteredText(drawingContext, valueText, center.Y - 23, 26, FontWeights.SemiBold, Color.FromRgb(0xEE, 0xF4, 0xF8), dpi);
        DrawCenteredText(drawingContext, NowLabel, center.Y + 13, 12, FontWeights.Normal, Color.FromRgb(0xAE, 0xBA, 0xC4), dpi);
    }

    private static Pen RoundedPen(Brush brush, double thickness) => new(brush, thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
    };

    private static void DrawArc(DrawingContext context, Point center, double radius, double startAngle, double sweepAngle, Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(PointOnCircle(center, radius, startAngle), false, false);
            figure.ArcTo(PointOnCircle(center, radius, startAngle + sweepAngle), new Size(radius, radius), 0, sweepAngle > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(center.X + (radius * Math.Cos(radians)), center.Y + (radius * Math.Sin(radians)));
    }

    private void DrawCenteredText(DrawingContext context, string text, double y, double size, FontWeight weight, Color color, double pixelsPerDip)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable"), FontStyles.Normal, weight, FontStretches.Normal),
            size, new SolidColorBrush(color), pixelsPerDip);
        context.DrawText(formatted, new Point((ActualWidth - formatted.Width) / 2, y));
    }

    private static void OnAccessibleValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _) =>
        ((TemperatureRing)dependencyObject).UpdateAccessibleName();

    private static void OnLocalizedTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _) =>
        ((TemperatureRing)dependencyObject).UpdateAccessibleName();

    private void UpdateAccessibleName() => AutomationProperties.SetName(this, string.Format(CultureInfo.CurrentCulture, AccessibleNameFormat, AccessibleValue));
}
