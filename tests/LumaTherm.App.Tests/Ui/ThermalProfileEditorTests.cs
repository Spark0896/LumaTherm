using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LumaTherm.App.Controls;
using LumaTherm.App.ViewModels;
using LumaTherm.Core.Colors;

namespace LumaTherm.App.Tests.Ui;

[Collection(ThermalCoreUiCollection.Name)]
public sealed class ThermalProfileEditorTests
{
    private readonly ThermalCoreStaFixture _sta;

    public ThermalProfileEditorTests(ThermalCoreStaFixture sta) => _sta = sta;

    [Fact]
    public void CompiledControl_RendersOneMarkerPerPointAtActualTemperatureOffsets() => _sta.Run(() =>
    {
        var editor = Editor((0, 0, 0, 255), (30, 0, 255, 255), (120, 255, 0, 0));
        var control = Arrange(new ThermalProfileEditor { DataContext = editor }, 760, 180);
        var track = Assert.IsType<Border>(control.FindName("GradientTrack"));
        var markers = Assert.IsType<ItemsControl>(control.FindName("PointMarkers"));

        Assert.Equal(editor.Points.Count, markers.Items.Count);
        Assert.Equal(190, control.TemperatureToPosition(30, 760));
        Assert.Equal(60, control.PositionToTemperature(380, 760));
        foreach (var point in editor.Points)
        {
            var container = Assert.IsType<ContentPresenter>(markers.ItemContainerGenerator.ContainerFromItem(point));
            var left = Canvas.GetLeft(container);
            Assert.Equal(point.Temperature / 120d, (left + 14) / markers.ActualWidth, 3);
        }

        Assert.True(Canvas.GetLeft(Assert.IsType<ContentPresenter>(markers.ItemContainerGenerator.ContainerFromItem(editor.Points[1])))
                    < Canvas.GetLeft(Assert.IsType<ContentPresenter>(markers.ItemContainerGenerator.ContainerFromItem(editor.Points[2]))));
        Assert.Equal(3, Assert.IsType<LinearGradientBrush>(track.Background).GradientStops.Count);
        Assert.Equal([0d, 0.25, 1d], Assert.IsType<LinearGradientBrush>(track.Background).GradientStops.Select(stop => stop.Offset));
    });

    [Fact]
    public void DoubleClickInsertion_DelegatesRoundedTrackTemperatureToEditor() => _sta.Run(() =>
    {
        var editor = new ThermalProfileEditorViewModel(ThermalProfile.Default);
        var control = Arrange(new ThermalProfileEditor { DataContext = editor }, 760, 180);

        control.AddPointAtPosition(383, 760);

        Assert.Contains(editor.Points, point => point.Temperature == 60.5);
        Assert.True(editor.Points.Single(point => point.Temperature == 60.5).IsSelected);
    });

    [Fact]
    public void Dragging_UsesPointerCaptureAndDelegatesMovementToEditor() => _sta.Run(() =>
    {
        var editor = new ThermalProfileEditorViewModel(ThermalProfile.Default);
        var moved = editor.Points[1];
        var control = new ThermalProfileEditor { DataContext = editor };
        var window = new Window
        {
            Content = control,
            Width = 760,
            Height = 180,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };
        try
        {
            window.Show();
            control.UpdateLayout();
            var marker = Marker(control, moved);

            marker.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                Source = marker,
            });
            control.MoveDraggedPointAtPosition(570, 760);

            Assert.Same(marker, Mouse.Captured);
            Assert.Same(marker, control.DragCaptureElement);
            Assert.Equal(90, moved.Temperature);

            marker.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                Source = marker,
            });

            Assert.Null(Mouse.Captured);
            Assert.Null(control.DragCaptureElement);
        }
        finally
        {
            Mouse.Capture(null);
            window.Close();
        }
    });

    [Fact]
    public void DeleteKey_RemovesSelectedMarkerThroughEditorButKeepsMinimumProfile() => _sta.Run(() =>
    {
        var editor = new ThermalProfileEditorViewModel(ThermalProfile.Default);
        var control = new ThermalProfileEditor { DataContext = editor };
        var removed = editor.Points[1];
        var window = new Window { Content = control, Width = 760, Height = 180, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        try
        {
            window.Show();
            control.UpdateLayout();

            RaiseKey(Marker(control, removed), Key.Delete);

            Assert.DoesNotContain(editor.Points, point => point.Id == removed.Id);
            control.UpdateLayout();
            RaiseKey(Marker(control, editor.Points[0]), Key.Delete);
            Assert.Equal(2, editor.Points.Count);
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(Key.Left, 49)]
    [InlineData(Key.Down, 49)]
    [InlineData(Key.Right, 51)]
    [InlineData(Key.Up, 51)]
    public void ArrowKeys_MoveMarkerByOneDegreeThroughEditor(Key key, double expected) => _sta.Run(() =>
    {
        var editor = new ThermalProfileEditorViewModel(ThermalProfile.Default);
        var moved = editor.AddAt(50);
        var control = new ThermalProfileEditor { DataContext = editor };
        var window = new Window { Content = control, Width = 760, Height = 180, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        try
        {
            window.Show();
            control.UpdateLayout();

            RaiseKey(Marker(control, moved), key);

            Assert.Equal(expected, moved.Temperature);
            Assert.True(moved.IsSelected);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Markers_AreFocusableNamedAndExposeSelectionForAccessibility() => _sta.Run(() =>
    {
        var editor = new ThermalProfileEditorViewModel(ThermalProfile.Default);
        editor.Select(editor.Points[1].Id);
        var control = Arrange(new ThermalProfileEditor { DataContext = editor }, 760, 180);
        var markers = Assert.IsType<ItemsControl>(control.FindName("PointMarkers"));

        foreach (var point in editor.Points)
        {
            var marker = Marker(control, point);
            Assert.True(marker.Focusable);
            Assert.Contains(point.Temperature.ToString("0.#"), AutomationProperties.GetName(marker));
            Assert.Equal(point.IsSelected ? "Selected" : "", AutomationProperties.GetItemStatus(marker));
            Assert.NotNull(marker.FocusVisualStyle);
        }

        Assert.Equal("Thermal profile temperature scale", AutomationProperties.GetName(control));
        Assert.True(markers.IsHitTestVisible);
    });

    [Fact]
    public void BeginDrag_WhenPointerCaptureFails_DoesNotLeaveMovableDragState() => _sta.Run(() =>
    {
        Mouse.Capture(null);
        var editor = new ThermalProfileEditorViewModel(ThermalProfile.Default);
        var moved = editor.Points[1];
        var control = Arrange(new ThermalProfileEditor { DataContext = editor }, 760, 180);
        var detachedMarker = Marker(control, moved);

        control.BeginPointDrag(moved.Id, detachedMarker);
        control.MoveDraggedPointAtPosition(570, 760);

        Assert.Null(Mouse.Captured);
        Assert.Null(control.DragCaptureElement);
        Assert.Equal(65, moved.Temperature);
    });

    [Fact]
    public void DataContextReplacement_ReleasesActivePointerCaptureAndDragState() => _sta.Run(() =>
    {
        var editor = new ThermalProfileEditorViewModel(ThermalProfile.Default);
        var replacement = new ThermalProfileEditorViewModel(ThermalProfile.Default);
        var control = new ThermalProfileEditor { DataContext = editor };
        var window = new Window { Content = control, Width = 760, Height = 180, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        try
        {
            window.Show();
            control.UpdateLayout();
            var marker = Marker(control, editor.Points[1]);
            control.BeginPointDrag(editor.Points[1].Id, marker);
            Assert.Same(marker, Mouse.Captured);

            control.DataContext = replacement;

            Assert.Null(Mouse.Captured);
            Assert.Null(control.DragCaptureElement);
        }
        finally
        {
            Mouse.Capture(null);
            window.Close();
        }
    });

    [Fact]
    public void UnloadedControl_DetachesPointSubscriptionsAndLoadedControlReattaches() => _sta.Run(() =>
    {
        var editor = new ThermalProfileEditorViewModel(ThermalProfile.Default);
        var control = new ThermalProfileEditor { DataContext = editor };
        var track = Assert.IsType<Border>(control.FindName("GradientTrack"));
        var window = new Window { Content = control, Width = 760, Height = 180, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        try
        {
            window.Show();
            control.UpdateLayout();
            var point = editor.Points[1];
            var beforeEdit = track.Background;
            point.Color = new RgbColor(1, 2, 3);
            Assert.NotSame(beforeEdit, track.Background);
            var beforeUnload = track.Background;

            control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, control));
            point.Color = new RgbColor(4, 5, 6);
            Assert.Same(beforeUnload, track.Background);

            control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, control));
            point.Color = new RgbColor(7, 8, 9);
            Assert.NotSame(beforeUnload, track.Background);
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    });

    [Fact]
    public void Reset_RemovedPointsNoLongerRefreshTheGradient() => _sta.Run(() =>
    {
        var editor = new ThermalProfileEditorViewModel(ThermalProfile.Default);
        var removedPoint = editor.Points[1];
        var control = new ThermalProfileEditor { DataContext = editor };
        var track = Assert.IsType<Border>(control.FindName("GradientTrack"));
        var window = new Window { Content = control, Width = 760, Height = 180, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        try
        {
            window.Show();
            control.UpdateLayout();
            editor.ResetToDefault();
            var afterReset = track.Background;

            removedPoint.Color = new RgbColor(1, 2, 3);

            Assert.Same(afterReset, track.Background);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void NullEditor_ClearsPreviouslyRenderedGradient() => _sta.Run(() =>
    {
        var control = Arrange(
            new ThermalProfileEditor { DataContext = new ThermalProfileEditorViewModel(ThermalProfile.Default) },
            760,
            180);
        var track = Assert.IsType<Border>(control.FindName("GradientTrack"));
        Assert.IsType<LinearGradientBrush>(track.Background);

        control.DataContext = null;

        Assert.Same(Brushes.Transparent, track.Background);
    });

    [Fact]
    public void UnloadedControl_ReleasesActivePointerCapture() => _sta.Run(() =>
    {
        var editor = new ThermalProfileEditorViewModel(ThermalProfile.Default);
        var control = new ThermalProfileEditor { DataContext = editor };
        var window = new Window { Content = control, Width = 760, Height = 180, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        try
        {
            window.Show();
            control.UpdateLayout();
            var marker = Marker(control, editor.Points[1]);
            control.BeginPointDrag(editor.Points[1].Id, marker);
            Assert.Same(marker, Mouse.Captured);

            control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, control));

            Assert.Null(Mouse.Captured);
            Assert.Null(control.DragCaptureElement);
        }
        finally
        {
            Mouse.Capture(null);
            window.Close();
        }
    });

    private static Button Marker(ThermalProfileEditor control, ThermalPointEditorViewModel point)
    {
        var markers = Assert.IsType<ItemsControl>(control.FindName("PointMarkers"));
        markers.UpdateLayout();
        var container = Assert.IsType<ContentPresenter>(markers.ItemContainerGenerator.ContainerFromItem(point));
        container.ApplyTemplate();
        return Assert.IsType<Button>(VisualTreeChild(container));
    }

    private static DependencyObject VisualTreeChild(DependencyObject parent)
    {
        var child = VisualTreeHelper.GetChild(parent, 0);
        return child is ContentPresenter presenter && VisualTreeHelper.GetChildrenCount(presenter) > 0
            ? VisualTreeChild(presenter)
            : child;
    }

    private static ThermalProfileEditorViewModel Editor(params (double Temperature, byte R, byte G, byte B)[] points) =>
        new(ThermalProfile.Create(points.Select(point => new ThermalPoint(point.Temperature, new RgbColor(point.R, point.G, point.B))), 0.8));

    private static T Arrange<T>(T element, double width, double height) where T : FrameworkElement
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        return element;
    }

    private static void RaiseKey(Button marker, Key key)
    {
        var source = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(marker));
        marker.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
            Source = marker,
        });
    }
}
