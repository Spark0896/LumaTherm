using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using LumaTherm.App.Converters;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using LumaTherm.App.ViewModels;

namespace LumaTherm.App.Controls;

public partial class ThermalProfileEditor : UserControl
{
    internal const double MinimumTemperature = 0;
    internal const double MaximumTemperature = 120;

    private readonly HashSet<ThermalPointEditorViewModel> _subscribedPoints = [];
    private ThermalProfileEditorViewModel? _subscribedEditor;
    private ThermalProfileEditorViewModel? _editor;
    private Guid? _draggedPointId;
    private UIElement? _dragCaptureElement;

    public ThermalProfileEditor()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    internal UIElement? DragCaptureElement => _dragCaptureElement;

    internal double TemperatureToPosition(double temperature, double trackWidth)
    {
        if (!double.IsFinite(temperature) || !double.IsFinite(trackWidth) || trackWidth <= 0)
        {
            return 0;
        }

        return Math.Clamp(temperature, MinimumTemperature, MaximumTemperature) / MaximumTemperature * trackWidth;
    }

    internal double PositionToTemperature(double position, double trackWidth)
    {
        if (!double.IsFinite(position) || !double.IsFinite(trackWidth) || trackWidth <= 0)
        {
            return MinimumTemperature;
        }

        var clampedPosition = Math.Clamp(position, 0, trackWidth);
        return Math.Round(clampedPosition / trackWidth * MaximumTemperature, 1);
    }

    internal void AddPointAtPosition(double position, double trackWidth)
    {
        if (_editor is null)
        {
            return;
        }

        try
        {
            _editor.AddAt(PositionToTemperature(position, trackWidth));
        }
        catch (ArgumentException)
        {
            // The editor owns spacing validation; an occupied slot is a harmless UI no-op.
        }
    }

    internal void BeginPointDrag(Guid id, UIElement captureElement)
    {
        ArgumentNullException.ThrowIfNull(captureElement);
        if (_editor is null || _editor.Points.All(point => point.Id != id))
        {
            return;
        }

        EndPointDrag();
        if (Mouse.Capture(captureElement, CaptureMode.Element))
        {
            _editor.Select(id);
            _draggedPointId = id;
            _dragCaptureElement = captureElement;
        }
    }

    internal void MoveDraggedPointAtPosition(double position, double trackWidth)
    {
        if (_editor is null || _draggedPointId is not Guid id)
        {
            return;
        }

        try
        {
            _editor.Move(id, PositionToTemperature(position, trackWidth));
        }
        catch (ArgumentException)
        {
            // The view model remains authoritative for valid spacing and bounds.
        }
    }

    internal void EndPointDrag()
    {
        var captured = _dragCaptureElement;
        _dragCaptureElement = null;
        _draggedPointId = null;
        if (captured is not null && ReferenceEquals(Mouse.Captured, captured))
        {
            Mouse.Capture(null);
        }
    }

    internal bool HandlePointKey(Guid id, Key key)
    {
        if (_editor is null)
        {
            return false;
        }

        var point = _editor.Points.SingleOrDefault(candidate => candidate.Id == id);
        if (point is null)
        {
            return false;
        }

        _editor.Select(id);
        if (key == Key.Delete)
        {
            var previousCount = _editor.Points.Count;
            _editor.Remove(id);
            return _editor.Points.Count < previousCount;
        }

        var change = key switch
        {
            Key.Left or Key.Down => -1d,
            Key.Right or Key.Up => 1d,
            _ => 0d,
        };
        if (change == 0)
        {
            return false;
        }

        try
        {
            _editor.Move(id, Math.Clamp(point.Temperature + change, MinimumTemperature, MaximumTemperature));
        }
        catch (ArgumentException)
        {
            // The key was handled even if no valid neighboring slot exists.
        }

        return true;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        EndPointDrag();
        DetachEditor();
        _editor = e.NewValue as ThermalProfileEditorViewModel;
        if (IsLoaded)
        {
            AttachEditor(_editor);
        }

        RefreshGradient();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachEditor(_editor);
        RefreshGradient();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        EndPointDrag();
        DetachEditor();
    }

    private void AttachEditor(ThermalProfileEditorViewModel? editor)
    {
        if (editor is null)
        {
            return;
        }

        if (!ReferenceEquals(_subscribedEditor, editor))
        {
            DetachEditor();
            _subscribedEditor = editor;
            editor.Points.CollectionChanged += OnPointsChanged;
        }

        SyncPointSubscriptions();
    }

    private void DetachEditor()
    {
        if (_subscribedEditor is not null)
        {
            _subscribedEditor.Points.CollectionChanged -= OnPointsChanged;
        }

        foreach (var point in _subscribedPoints)
        {
            point.PropertyChanged -= OnPointPropertyChanged;
        }

        _subscribedPoints.Clear();
        _subscribedEditor = null;
    }

    private void OnPointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncPointSubscriptions();
        RefreshGradient();
    }

    private void SyncPointSubscriptions()
    {
        if (_subscribedEditor is null)
        {
            return;
        }

        var currentPoints = _subscribedEditor.Points.ToHashSet();
        foreach (var removedPoint in _subscribedPoints.Where(point => !currentPoints.Contains(point)).ToArray())
        {
            removedPoint.PropertyChanged -= OnPointPropertyChanged;
            _subscribedPoints.Remove(removedPoint);
        }

        foreach (var addedPoint in currentPoints.Where(point => !_subscribedPoints.Contains(point)))
        {
            addedPoint.PropertyChanged += OnPointPropertyChanged;
            _subscribedPoints.Add(addedPoint);
        }
    }

    private void OnPointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ThermalPointEditorViewModel.Temperature) or nameof(ThermalPointEditorViewModel.Color))
        {
            RefreshGradient();
        }
    }

    private void RefreshGradient()
    {
        if (GradientTrack is null)
        {
            return;
        }

        if (_editor is null || _editor.Points.Count == 0)
        {
            GradientTrack.Background = System.Windows.Media.Brushes.Transparent;
            return;
        }

        var stops = new GradientStopCollection();
        foreach (var point in _editor.Points)
        {
            stops.Add(new GradientStop(point.Color.ToMediaColor(), point.Temperature / MaximumTemperature));
        }

        GradientTrack.Background = new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5));
    }

    private void GradientTrack_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        AddPointAtPosition(e.GetPosition(GradientTrack).X, GradientTrack.ActualWidth);
        e.Handled = true;
    }

    private void PointMarker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: ThermalPointEditorViewModel point } marker)
        {
            return;
        }

        marker.Focus();
        BeginPointDrag(point.Id, marker);
        e.Handled = true;
    }

    private void PointMarker_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedPointId is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        MoveDraggedPointAtPosition(e.GetPosition(GradientTrack).X, GradientTrack.ActualWidth);
        e.Handled = true;
    }

    private void PointMarker_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedPointId is null)
        {
            return;
        }

        EndPointDrag();
        e.Handled = true;
    }

    private void PointMarker_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (ReferenceEquals(sender, _dragCaptureElement))
        {
            _dragCaptureElement = null;
            _draggedPointId = null;
        }
    }

    private void PointMarker_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is Button { DataContext: ThermalPointEditorViewModel point } && HandlePointKey(point.Id, e.Key))
        {
            e.Handled = true;
        }
    }
}

public sealed class TemperatureToCanvasLeftConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double temperature || values[1] is not double width || width <= 0)
        {
            return 0d;
        }

        const double markerRadius = 14;
        return Math.Clamp(temperature, ThermalProfileEditor.MinimumTemperature, ThermalProfileEditor.MaximumTemperature)
            / ThermalProfileEditor.MaximumTemperature * width - markerRadius;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
