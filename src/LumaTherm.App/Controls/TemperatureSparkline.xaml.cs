using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LumaTherm.App.ViewModels;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using UserControl = System.Windows.Controls.UserControl;

namespace LumaTherm.App.Controls;

public partial class TemperatureSparkline : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable<TemperaturePoint>), typeof(TemperatureSparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    private INotifyCollectionChanged? _observedCollection;

    public TemperatureSparkline() => InitializeComponent();

    public IEnumerable<TemperaturePoint>? ItemsSource
    {
        get => (IEnumerable<TemperaturePoint>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    internal int ObservedCollectionChangeCount { get; private set; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)), 1);
        for (var y = ActualHeight - 1; y > 0; y -= 35) drawingContext.DrawLine(gridPen, new Point(0, y), new Point(ActualWidth, y));

        var points = ItemsSource?.Where(point => double.IsFinite(point.Celsius)).ToArray() ?? [];
        if (points.Length == 0) return;

        var minimum = points.Min(point => point.Celsius) - 2;
        var maximum = points.Max(point => point.Celsius) + 2;
        var range = Math.Max(1, maximum - minimum);
        Point Project(int index) => new(
            points.Length == 1 ? ActualWidth / 2 : index * ActualWidth / (points.Length - 1),
            5 + ((maximum - points[index].Celsius) / range * Math.Max(0, ActualHeight - 10)));

        var stroke = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromRgb(0x50, 0xC8, 0xFF), 0),
                new(Color.FromRgb(0xFF, 0xC6, 0x4A), 0.68),
                new(Color.FromRgb(0xFF, 0x56, 0x5D), 1),
            }, new Point(0, 0.5), new Point(1, 0.5));

        if (points.Length == 1)
        {
            drawingContext.DrawEllipse(stroke, null, Project(0), 3, 3);
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Project(0), false, false);
            for (var i = 1; i < points.Length; i++) context.LineTo(Project(i), true, false);
        }
        geometry.Freeze();
        var pen = new Pen(stroke, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static void OnItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (TemperatureSparkline)dependencyObject;
        control.Detach(args.OldValue as INotifyCollectionChanged);
        control.Attach(args.NewValue as INotifyCollectionChanged);
        control.InvalidateVisual();
    }

    private void Attach(INotifyCollectionChanged? collection)
    {
        _observedCollection = collection;
        if (_observedCollection is not null) _observedCollection.CollectionChanged += OnCollectionChanged;
    }

    private void Detach(INotifyCollectionChanged? collection)
    {
        if (collection is not null) collection.CollectionChanged -= OnCollectionChanged;
        if (ReferenceEquals(_observedCollection, collection)) _observedCollection = null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        ObservedCollectionChangeCount++;
        InvalidateVisual();
    }
}
