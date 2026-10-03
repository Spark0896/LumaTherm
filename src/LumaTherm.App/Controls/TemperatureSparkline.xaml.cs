using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LumaTherm.App.ViewModels;
using LumaTherm.Core.Colors;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using UserControl = System.Windows.Controls.UserControl;
namespace LumaTherm.App.Controls;
public partial class TemperatureSparkline : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<TemperaturePoint>), typeof(TemperatureSparkline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));
    public static readonly DependencyProperty ProfileProperty = DependencyProperty.Register(nameof(Profile), typeof(ThermalProfile), typeof(TemperatureSparkline), new FrameworkPropertyMetadata(null));
    private INotifyCollectionChanged? _observedCollection;
    public TemperatureSparkline() { InitializeComponent(); Loaded += OnLoaded; Unloaded += OnUnloaded; }
    public IEnumerable<TemperaturePoint>? ItemsSource { get => (IEnumerable<TemperaturePoint>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public ThermalProfile? Profile { get => (ThermalProfile?)GetValue(ProfileProperty); set => SetValue(ProfileProperty, value); }
    internal IReadOnlyList<Point> ProjectedPoints => Project(ActualWidth, ActualHeight);
    private IReadOnlyList<Point> Project(double width, double height)
    {
        var readings = ItemsSource?.Where(point => double.IsFinite(point.Celsius)).OrderBy(point => point.Timestamp).ToArray() ?? [];
        if (readings.Length == 0) return [];
        var end = readings[^1].Timestamp;
        return readings.Where(point => end - point.Timestamp <= TimeSpan.FromSeconds(60)).Select(point => new Point(
            30 + Math.Clamp(1 - (end - point.Timestamp).TotalSeconds / 60, 0, 1) * Math.Max(0, width - 34),
            6 + (1 - Math.Clamp(point.Celsius / 120, 0, 1)) * Math.Max(0, height - 12))).ToArray();
    }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (ActualWidth <= 34 || ActualHeight <= 12) return;
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x39, 0x4B, 0x59)), 0.5);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var temperature = 0; temperature <= 120; temperature += 30)
        {
            var y = 6 + (1 - temperature / 120d) * (ActualHeight - 12);
            context.DrawLine(gridPen, new Point(30, y), new Point(ActualWidth - 4, y));
            var label = new FormattedText(temperature.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, new SolidColorBrush(Color.FromRgb(0xAE, 0xBA, 0xC4)), dpi);
            context.DrawText(label, new Point(0, y - label.Height / 2));
        }
        var points = ProjectedPoints;
        if (points.Count == 0) return;
        var stroke = new SolidColorBrush(Color.FromRgb(0x47, 0xB4, 0xFF));
        if (points.Count == 1) { context.DrawEllipse(stroke, null, points[0], 3, 3); return; }
        var line = new StreamGeometry();
        using (var figure = line.Open()) { figure.BeginFigure(points[0], false, false); foreach (var point in points.Skip(1)) figure.LineTo(point, true, false); }
        line.Freeze();
        var area = new StreamGeometry();
        using (var figure = area.Open())
        {
            figure.BeginFigure(points[0], true, true); foreach (var point in points.Skip(1)) figure.LineTo(point, true, false);
            figure.LineTo(new Point(points[^1].X, ActualHeight - 6), true, false); figure.LineTo(new Point(points[0].X, ActualHeight - 6), true, false);
        }
        area.Freeze();
        context.DrawGeometry(new LinearGradientBrush(Color.FromArgb(45, 0x47, 0xB4, 0xFF), Color.FromArgb(0, 0x47, 0xB4, 0xFF), 90), null, area);
        context.DrawGeometry(null, new Pen(stroke, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, line);
        context.DrawEllipse(stroke, null, points[^1], 3, 3);
    }
    private static void OnItemsSourceChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        var control = (TemperatureSparkline)obj; control.Detach(args.OldValue as INotifyCollectionChanged); if (control.IsLoaded) control.Attach(args.NewValue as INotifyCollectionChanged); control.InvalidateVisual();
    }
    private void Attach(INotifyCollectionChanged? collection)
    {
        if (collection is null || ReferenceEquals(_observedCollection, collection)) return;
        Detach(_observedCollection); _observedCollection = collection; collection.CollectionChanged += OnCollectionChanged;
    }
    private void Detach(INotifyCollectionChanged? collection)
    {
        if (collection is null || !ReferenceEquals(_observedCollection, collection)) return;
        collection.CollectionChanged -= OnCollectionChanged; _observedCollection = null;
    }
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => InvalidateVisual();
    private void OnLoaded(object sender, RoutedEventArgs args) => Attach(ItemsSource as INotifyCollectionChanged);
    private void OnUnloaded(object sender, RoutedEventArgs args) => Detach(_observedCollection);
}
