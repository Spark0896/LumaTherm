using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using LumaTherm.App.Controls;
using LumaTherm.App.ViewModels;
using LumaTherm.App.Views;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Sensors;
using LumaTherm.Core.Settings;

namespace LumaTherm.App.Tests.Ui;

[Collection(ThermalCoreUiCollection.Name)]
public sealed class ThermalCoreRuntimeTests
{
    private readonly ThermalCoreStaFixture _sta;

    public ThermalCoreRuntimeTests(ThermalCoreStaFixture sta) => _sta = sta;

    [Fact]
    public void Theme_ResolvesExactColorsAndFrozenBrushesAtRuntime() => _sta.Run(() =>
    {
        var expected = new Dictionary<string, string>
        {
            ["WindowBackground"] = "#FF171B20",
            ["RailBackground"] = "#FF111419",
            ["PanelBackground"] = "#FF20252B",
            ["PanelSecondary"] = "#FF1B2025",
            ["PrimaryText"] = "#FFEEF4F8",
            ["MutedText"] = "#FF7E8994",
            ["ColdColor"] = "#FF50C8FF",
            ["WarmColor"] = "#FFFFC64A",
            ["HotColor"] = "#FFFF565D",
            ["SuccessColor"] = "#FF55D69C",
        };

        foreach (var (key, value) in expected)
        {
            var color = Assert.IsType<Color>(Application.Current.Resources[key]);
            Assert.Equal(value, color.ToString());
            var brush = Assert.IsType<SolidColorBrush>(Application.Current.Resources[$"{key}Brush"]);
            Assert.Equal(color, brush.Color);
            Assert.True(brush.IsFrozen);
        }
    });

    [Fact]
    public void Logo_RuntimeDrawingKeepsExactSuppliedCircleAndBladeGeometry() => _sta.Run(() =>
    {
        var drawing = Assert.IsType<DrawingGroup>(Application.Current.Resources["LogoDrawing"]);
        var ring = Assert.IsType<EllipseGeometry>(Assert.IsType<GeometryDrawing>(drawing.Children[0]).Geometry);

        Assert.Equal(new Point(24, 24), ring.Center);
        Assert.Equal(20, ring.RadiusX);
        Assert.Equal(20, ring.RadiusY);
        Assert.Same(Application.Current.Resources["LogoBladeOneGeometry"], Assert.IsType<GeometryDrawing>(drawing.Children[1]).Geometry);
        Assert.Same(Application.Current.Resources["LogoBladeTwoGeometry"], Assert.IsType<GeometryDrawing>(drawing.Children[2]).Geometry);
        Assert.Same(Application.Current.Resources["LogoBladeThreeGeometry"], Assert.IsType<GeometryDrawing>(drawing.Children[3]).Geometry);
    });

    [Fact]
    public void Dashboard_LoadsCompiledXamlAndBindsPrimaryInteraction() => _sta.Run(() =>
    {
        using var vm = new MainViewModel(new FakeRuntime());
        var view = Arrange(new DashboardView { DataContext = vm }, 1104, 652);
        var toggle = Assert.IsAssignableFrom<ToggleButton>(view.FindName("ModeToggle"));
        var shell = new MainWindow { DataContext = vm };

        try
        {
            Assert.Same(vm.ToggleModeCommand, toggle.Command);
            Assert.Equal("Включить или выключить термосинхронизацию", AutomationProperties.GetName(toggle));
            Assert.NotEmpty(shell.IconOnlyButtons.Select(AutomationProperties.GetName));
            Assert.All(shell.IconOnlyButtons, button => Assert.Matches("[А-Яа-яЁё]", AutomationProperties.GetName(button)));
            Assert.All(
                new[] { shell.MinimizeButtonContent, shell.MaximizeButtonContent, shell.CloseButtonContent },
                icon => Assert.Equal("Segoe MDL2 Assets", icon.FontFamily.Source));
        }
        finally
        {
            shell.Close();
        }
    });

    [Fact]
    public void ModeToggle_ExposesAutomationToggleStateAndPreservesCommandAccess() => _sta.Run(() =>
    {
        var runtime = new FakeRuntime();
        using var vm = new MainViewModel(runtime);
        var view = Arrange(new DashboardView { DataContext = vm }, 1104, 652);
        var toggle = Assert.IsAssignableFrom<ToggleButton>(view.FindName("ModeToggle"));
        var peer = Assert.IsAssignableFrom<ToggleButtonAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(toggle));
        var provider = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));

        Assert.Equal(ToggleState.Off, provider.ToggleState);
        Assert.Same(vm.ToggleModeCommand, toggle.Command);
        Assert.True(toggle.Focusable);
        Assert.True(KeyboardNavigation.GetIsTabStop(toggle));

        runtime.Publish(Snapshot(RuntimeStatus.Active, 68, new RgbColor(0xFF, 0xC6, 0x4A)));
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

        Assert.True(toggle.IsChecked);
        Assert.Equal(ToggleState.On, provider.ToggleState);
    });

    [Fact]
    public void ModeToggle_AutomationActivationWaitsForAuthoritativeRuntimeSnapshot() => _sta.Run(() =>
    {
        var runtime = new ControlledModeRuntime();
        using var vm = new MainViewModel(runtime, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var view = Arrange(new DashboardView { DataContext = vm }, 1104, 652);
        var toggle = Assert.IsAssignableFrom<ToggleButton>(view.FindName("ModeToggle"));
        var peer = Assert.IsAssignableFrom<ToggleButtonAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(toggle));
        var provider = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            provider.Toggle();

            Assert.False(toggle.IsChecked);
            Assert.Equal(ToggleState.Off, provider.ToggleState);
            Assert.Equal(1, runtime.ModeRequestCount);

            Assert.False(toggle.IsEnabled);
            Assert.Throws<ElementNotEnabledException>(() => provider.Toggle());
            Assert.Equal(1, runtime.ModeRequestCount);
            Assert.Equal(ToggleState.Off, provider.ToggleState);

            runtime.CompleteRequest();
            PumpUntil(() => !vm.ToggleModeCommand.IsExecuting);
            Assert.Equal(ToggleState.Off, provider.ToggleState);

            runtime.Publish(Snapshot(RuntimeStatus.Active, 68, new RgbColor(0xFF, 0xC6, 0x4A)));
            PumpUntil(() => provider.ToggleState == ToggleState.On);

            Assert.True(toggle.IsChecked);
            Assert.Equal(ToggleState.On, provider.ToggleState);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    });

    [Fact]
    public void ModeToggle_FailedAutomationActivationStaysOffAndReportsFailure() => _sta.Run(() =>
    {
        var runtime = new ControlledModeRuntime();
        using var vm = new MainViewModel(runtime, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var view = Arrange(new DashboardView { DataContext = vm }, 1104, 652);
        var toggle = Assert.IsAssignableFrom<ToggleButton>(view.FindName("ModeToggle"));
        var peer = Assert.IsAssignableFrom<ToggleButtonAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(toggle));
        var provider = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            provider.Toggle();
            runtime.FailRequest(new InvalidOperationException("runtime rejected mode request"));
            PumpUntil(() => !vm.ToggleModeCommand.IsExecuting && view.ModeStatusText.Text == "Не удалось изменить режим.");

            Assert.False(toggle.IsChecked);
            Assert.Equal(ToggleState.Off, provider.ToggleState);
            Assert.Equal(1, runtime.ModeRequestCount);
            Assert.Equal("Не удалось изменить режим.", view.ModeStatusText.Text);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    });

    [Theory]
    [InlineData(20)]
    [InlineData(35)]
    [InlineData(65)]
    [InlineData(85)]
    [InlineData(105)]
    public void TemperatureRing_RendersEveryBoundaryAndShowsSourceValue(double temperature) => _sta.Run(() =>
    {
        var ring = Arrange(new TemperatureRing
        {
            Temperature = temperature,
            DisplayColor = new RgbColor(0xFF, 0xC6, 0x4A),
        }, 190, 190);

        var checksum = RenderChecksum(ring, 190, 190);

        Assert.NotEqual(BlankChecksum(190, 190), checksum);
        Assert.Contains(temperature.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), ring.AccessibleValue);
    });

    [Fact]
    public void TemperatureRing_DependencyPropertiesChangeRenderedPixels() => _sta.Run(() =>
    {
        var ring = Arrange(new TemperatureRing { Temperature = 35, DisplayColor = new RgbColor(0x50, 0xC8, 0xFF) }, 190, 190);
        var cold = RenderChecksum(ring, 190, 190);
        ring.Temperature = 85;
        ring.DisplayColor = new RgbColor(0xFF, 0x56, 0x5D);
        ring.UpdateLayout();
        var hot = RenderChecksum(ring, 190, 190);

        Assert.NotEqual(cold, hot);
    });

    [Fact]
    public void GradientBar_RendersThreeStopsAndBoundLabels() => _sta.Run(() =>
    {
        var bar = Arrange(new ThermalGradientBar
        {
            ColdTemperature = 35,
            WarmTemperature = 65,
            HotTemperature = 85,
            ColdColor = new RgbColor(0x50, 0xC8, 0xFF),
            WarmColor = new RgbColor(0xFF, 0xC6, 0x4A),
            HotColor = new RgbColor(0xFF, 0x56, 0x5D),
        }, 600, 100);

        Assert.Equal(3, bar.RenderedStops.Count);
        Assert.Equal(["35° Холодно", "65° Тепло", "85° Пик"], bar.RenderedLabels);
        Assert.Equal([Color.FromRgb(0x50, 0xC8, 0xFF), Color.FromRgb(0xFF, 0xC6, 0x4A), Color.FromRgb(0xFF, 0x56, 0x5D)], bar.RenderedStops.Select(stop => stop.Color));
        Assert.NotEqual(BlankChecksum(600, 100), RenderChecksum(bar, 600, 100));
    });

    [Fact]
    public void Sparkline_TracksCurrentCollectionOnlyAndHandlesDegenerateHistories() => _sta.Run(() =>
    {
        var oldItems = new SubscriberCountingSequence();
        var currentItems = new SubscriberCountingSequence();
        var sparkline = Arrange(new TemperatureSparkline { ItemsSource = oldItems }, 520, 107);
        sparkline.ItemsSource = currentItems;
        sparkline.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.Equal(0, oldItems.SubscriberCount);
        Assert.Equal(1, currentItems.SubscriberCount);

        oldItems.Add(new(DateTimeOffset.UnixEpoch, 20));
        currentItems.Add(new(DateTimeOffset.UnixEpoch, 60));
        var onePoint = RenderChecksum(sparkline, 520, 107);
        currentItems.Add(new(DateTimeOffset.UnixEpoch.AddSeconds(1), 60));
        currentItems.Add(new(DateTimeOffset.UnixEpoch.AddSeconds(2), 60));
        var flat = RenderChecksum(sparkline, 520, 107);

        Assert.NotEqual(onePoint, flat);
        Assert.Equal("График температуры GPU за последнюю минуту", AutomationProperties.GetName(sparkline));
        sparkline.ItemsSource = Array.Empty<TemperaturePoint>();
        _ = RenderChecksum(sparkline, 520, 107);
    });

    [Fact]
    public void Sparkline_UnloadedDetachesAndLoadedReattachesExactlyOnce() => _sta.Run(() =>
    {
        var items = new SubscriberCountingSequence();
        var sparkline = new TemperatureSparkline { ItemsSource = items };
        var host = new Border { Child = sparkline };
        var window = new Window { Content = host, Width = 540, Height = 140, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Assert.Equal(1, items.SubscriberCount);

            host.Child = null;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Assert.Equal(0, items.SubscriberCount);
            items.Add(new(DateTimeOffset.UnixEpoch, 50));
            Assert.Equal(0, items.SubscriberCount);

            host.Child = sparkline;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Assert.Equal(1, items.SubscriberCount);
            items.Add(new(DateTimeOffset.UnixEpoch.AddSeconds(1), 51));
            Assert.Equal(1, items.SubscriberCount);
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(1104, false)]
    [InlineData(900, true)]
    public void Dashboard_ReflowsAtNineHundredEightyPixels(double width, bool compact) => _sta.Run(() =>
    {
        using var vm = new MainViewModel(new FakeRuntime());
        var view = Arrange(new DashboardView { DataContext = vm }, width, compact ? 1050 : 652);

        Assert.Equal(compact, view.IsCompactLayout);
        Assert.True(view.ModeToggle.ActualWidth > 0);
        Assert.True(view.ModeToggle.TransformToAncestor(view).TransformBounds(new Rect(view.ModeToggle.RenderSize)).Right <= view.ActualWidth + 0.5);
        Assert.Equal(compact ? 1 : 2, view.ActiveTopColumnCount);
        Assert.Equal(compact ? 1 : 2, view.ActiveBottomColumnCount);
    });

    [Theory]
    [InlineData(979, 552, true)]
    [InlineData(980, 652, false)]
    [InlineData(981, 652, false)]
    [InlineData(884, 552, true)]
    public void Dashboard_BoundaryAndMinimumClientKeepEveryPersistentElementInLayout(double width, double height, bool compact) => _sta.Run(() =>
    {
        using var vm = new MainViewModel(new FakeRuntime());
        var view = Arrange(new DashboardView { DataContext = vm }, width, height);

        Assert.Equal(compact, view.IsCompactLayout);
        foreach (var name in new[] { "RingPanel", "ProfilePanel", "MonitorPanel", "BehaviorPanel", "ModeToggle" })
        {
            var element = Assert.IsAssignableFrom<FrameworkElement>(view.FindName(name));
            Assert.Equal(Visibility.Visible, element.Visibility);
            Assert.True(element.ActualWidth > 0, $"{name} has no width.");
            Assert.True(element.ActualHeight > 0, $"{name} has no height.");
            var bounds = element.TransformToAncestor(view).TransformBounds(new Rect(element.RenderSize));
            Assert.True(bounds.Left >= -0.5, $"{name} starts outside the client: {bounds}.");
            Assert.True(bounds.Right <= width + 0.5, $"{name} clips horizontally: {bounds}.");
            Assert.True(bounds.Top >= -0.5, $"{name} starts above the scroll extent: {bounds}.");
            Assert.True(bounds.Bottom <= view.DashboardScroller.ExtentHeight + 0.5, $"{name} ends beyond the scroll extent: {bounds}.");
        }

        if (compact)
        {
            Assert.True(view.DashboardScroller.ExtentHeight > view.DashboardScroller.ViewportHeight);
            Assert.True(view.DashboardScroller.ScrollableHeight > 0);
        }
        else
        {
            Assert.Equal(0, view.DashboardScroller.ScrollableHeight);
        }
    });

    [Fact]
    public void RealMainViewModelUpdateChangesVisibleDashboardProjection() => _sta.Run(() =>
    {
        var runtime = new FakeRuntime();
        using var vm = new MainViewModel(runtime, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var view = Arrange(new DashboardView { DataContext = vm }, 1104, 652);
        var gradient = Assert.IsType<ThermalGradientBar>(view.FindName("ProfileGradientBar"));
        var gradientWidth = Math.Max(1, (int)Math.Round(gradient.ActualWidth));
        var gradientHeight = Math.Max(1, (int)Math.Round(gradient.ActualHeight));
        var historyWidth = Math.Max(1, (int)Math.Round(view.HistorySparkline.ActualWidth));
        var historyHeight = Math.Max(1, (int)Math.Round(view.HistorySparkline.ActualHeight));
        var gradientBefore = RenderChecksum(gradient, gradientWidth, gradientHeight);
        var historyBefore = RenderChecksum(view.HistorySparkline, historyWidth, historyHeight);

        runtime.Publish(Snapshot(RuntimeStatus.Active, 68, new RgbColor(0xFF, 0xC6, 0x4A)));
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

        var gradientAfter = RenderChecksum(gradient, gradientWidth, gradientHeight);
        var historyAfter = RenderChecksum(view.HistorySparkline, historyWidth, historyHeight);

        Assert.Equal("68°C", view.CurrentTemperatureText.Text);
        Assert.Equal("#FFC64A", view.CurrentColorText.Text);
        Assert.Equal("Режим активен", view.ModeStatusText.Text);
        Assert.Equal(68, view.TemperatureRing.Temperature);
        Assert.Same(vm.History, view.HistorySparkline.ItemsSource);
        Assert.Single(vm.History);
        Assert.NotEqual(gradientBefore, gradientAfter);
        Assert.NotEqual(historyBefore, historyAfter);
    });

    [Fact]
    public void Dashboard_ProfileStopsDisplayHexColors() => _sta.Run(() =>
    {
        using var vm = new MainViewModel(new FakeRuntime());
        var view = Arrange(new DashboardView { DataContext = vm }, 1104, 652);

        Assert.Equal("#50C8FF", Assert.IsType<TextBlock>(view.FindName("ColdColorText")).Text);
        Assert.Equal("#FFB000", Assert.IsType<TextBlock>(view.FindName("WarmColorText")).Text);
        Assert.Equal("#FF565D", Assert.IsType<TextBlock>(view.FindName("HotColorText")).Text);
    });

    [Fact]
    public void Shell_DeviceIndicatorChangesFromUnavailableToConnected() => _sta.Run(() =>
    {
        var runtime = new FakeRuntime();
        using var vm = new MainViewModel(runtime);
        var shell = new MainWindow { DataContext = vm };
        try
        {
            Arrange(Assert.IsType<Border>(shell.Content), 1180, 720);
            var dot = Assert.IsType<System.Windows.Shapes.Ellipse>(shell.FindName("DeviceStatusDot"));
            Assert.Equal(Color.FromRgb(0x66, 0x72, 0x7C), Assert.IsType<SolidColorBrush>(dot.Fill).Color);

            runtime.Publish(Snapshot(RuntimeStatus.Active, 68, new RgbColor(0xFF, 0xC6, 0x4A)));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            Assert.Equal(Color.FromRgb(0x55, 0xD6, 0x9C), Assert.IsType<SolidColorBrush>(dot.Fill).Color);
        }
        finally
        {
            shell.Close();
        }
    });

    [Fact]
    public void ApprovedVisualLayersResolveAsCompiledWpfElementsAndBrushes() => _sta.Run(() =>
    {
        using var vm = new MainViewModel(new FakeRuntime());
        var shell = new MainWindow { DataContext = vm };
        try
        {
            Arrange(Assert.IsType<Border>(shell.Content), 1180, 720);
            var workspace = Assert.IsType<Grid>(shell.FindName("WorkspaceSurface"));
            var workspaceBrush = Assert.IsType<LinearGradientBrush>(workspace.Background);
            Assert.Equal(Color.FromRgb(0x1B, 0x20, 0x25), workspaceBrush.GradientStops[0].Color);
            Assert.Equal(Color.FromRgb(0x15, 0x19, 0x1D), workspaceBrush.GradientStops[1].Color);
            Assert.Equal(0.65, workspaceBrush.GradientStops[1].Offset);

            var indicator = Assert.IsType<Border>(shell.FindName("HomeSelectionIndicator"));
            Assert.Equal(3, indicator.Width);
            Assert.Equal(24, indicator.Height);
            Assert.Equal(Color.FromRgb(0x50, 0xC8, 0xFF), Assert.IsType<SolidColorBrush>(indicator.Background).Color);
            var glow = Assert.IsType<DropShadowEffect>(indicator.Effect);
            Assert.Equal(Color.FromRgb(0x50, 0xC8, 0xFF), glow.Color);
            Assert.Equal(0, glow.ShadowDepth);
            Assert.Equal(12, glow.BlurRadius);
            var settingsIndicator = Assert.IsType<Border>(shell.FindName("SettingsSelectionIndicator"));
            Assert.Equal(Visibility.Collapsed, settingsIndicator.Visibility);
            Assert.IsType<Button>(shell.FindName("SettingsButton")).Command.Execute(null);
            Assert.Equal(Visibility.Collapsed, indicator.Visibility);
            Assert.Equal(Visibility.Visible, settingsIndicator.Visibility);

            var dashboard = Arrange(new DashboardView { DataContext = vm }, 1104, 652);
            var temperatureGlow = Assert.IsType<Ellipse>(dashboard.FindName("TemperatureCardGlow"));
            var radial = Assert.IsType<RadialGradientBrush>(temperatureGlow.Fill);
            Assert.Equal(Color.FromArgb(0x26, 0xFF, 0xC6, 0x4A), radial.GradientStops[0].Color);
            Assert.Equal(0, radial.GradientStops[^1].Color.A);
        }
        finally
        {
            shell.Close();
        }
    });

    [Theory]
    [InlineData(1104, 720, 0.182294282442410, -0.195720540466015, 0.817705717557590, 1.195720540466020)]
    [InlineData(884, 620, 0.170739926983209, -0.170459986143814, 0.829260073016791, 1.170459986143810)]
    public void WorkspaceGradient_UsesCssOneHundredFortyFiveDegreeGeometry(
        double width,
        double height,
        double expectedStartX,
        double expectedStartY,
        double expectedEndX,
        double expectedEndY) => _sta.Run(() =>
    {
        using var vm = new MainViewModel(new FakeRuntime());
        var shell = new MainWindow { DataContext = vm };
        try
        {
            var workspace = Assert.IsType<Grid>(shell.FindName("WorkspaceSurface"));
            Arrange(workspace, width, height);
            var brush = Assert.IsType<LinearGradientBrush>(workspace.Background);

            Assert.Equal(expectedStartX, brush.StartPoint.X, 12);
            Assert.Equal(expectedStartY, brush.StartPoint.Y, 12);
            Assert.Equal(expectedEndX, brush.EndPoint.X, 12);
            Assert.Equal(expectedEndY, brush.EndPoint.Y, 12);

            var physicalX = (brush.EndPoint.X - brush.StartPoint.X) * width;
            var physicalY = (brush.EndPoint.Y - brush.StartPoint.Y) * height;
            Assert.Equal(0.573576436351046, physicalX / Math.Sqrt((physicalX * physicalX) + (physicalY * physicalY)), 12);
            Assert.Equal(0.819152044288992, physicalY / Math.Sqrt((physicalX * physicalX) + (physicalY * physicalY)), 12);
            Assert.Equal(Color.FromRgb(0x1B, 0x20, 0x25), brush.GradientStops[0].Color);
            Assert.Equal(Color.FromRgb(0x15, 0x19, 0x1D), brush.GradientStops[1].Color);
            Assert.Equal(0.65, brush.GradientStops[1].Offset);
        }
        finally
        {
            shell.Close();
        }
    });

    [Fact]
    public void Sparkline_RendersAmberAreaBelowActualLine() => _sta.Run(() =>
    {
        var points = new ObservableCollection<TemperaturePoint>
        {
            new(DateTimeOffset.UnixEpoch, 60),
            new(DateTimeOffset.UnixEpoch.AddSeconds(1), 60),
            new(DateTimeOffset.UnixEpoch.AddSeconds(2), 60),
        };
        var sparkline = Arrange(new TemperatureSparkline { ItemsSource = points }, 520, 107);

        var belowLine = RenderPixel(sparkline, 520, 107, 260, 80);

        Assert.True(belowLine.A > 0);
        Assert.True(belowLine.R > belowLine.B);
        Assert.True(belowLine.G > belowLine.B);
    });

    private static T Arrange<T>(T element, double width, double height) where T : FrameworkElement
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        return element;
    }

    private static string RenderChecksum(FrameworkElement element, int width, int height)
    {
        Arrange(element, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return Convert.ToHexString(SHA256.HashData(pixels));
    }

    private static string BlankChecksum(int width, int height) => Convert.ToHexString(SHA256.HashData(new byte[width * height * 4]));

    private static Color RenderPixel(FrameworkElement element, int width, int height, int x, int y)
    {
        Arrange(element, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Yield();
        }

        Assert.True(condition(), "Condition did not become true before the finite dispatcher deadline.");
    }

    private static RuntimeSnapshot Snapshot(RuntimeStatus status, double temperature, RgbColor color) => new(
        status,
        new TemperatureReading(temperature, "NVIDIA", "NVIDIA GeForce RTX 5070", DateTimeOffset.UtcNow),
        color,
        ThermalRange.Warm,
        new LightingDeviceInfo("lamp", "GIGABYTE Device", 4, true),
        null,
        DateTimeOffset.UtcNow,
        true);

    private sealed class FakeRuntime : IThermalRuntime
    {
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged;
        public RuntimeSnapshot CurrentSnapshot { get; private set; } = new(RuntimeStatus.Disabled, null, null, null, null, null, DateTimeOffset.MinValue, false);
        public AppSettings CurrentSettings { get; private set; } = AppSettings.Default;

        public void Publish(RuntimeSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }

        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            CurrentSettings = CurrentSettings with { IsModeEnabled = enabled };
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ControlledModeRuntime : IThermalRuntime
    {
        private readonly TaskCompletionSource _modeRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<RuntimeSnapshot>? SnapshotChanged;
        public RuntimeSnapshot CurrentSnapshot { get; private set; } = new(RuntimeStatus.Disabled, null, null, null, null, null, DateTimeOffset.MinValue, false);
        public AppSettings CurrentSettings { get; private set; } = AppSettings.Default;
        public int ModeRequestCount { get; private set; }

        public void Publish(RuntimeSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }

        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            ModeRequestCount++;
            CurrentSettings = CurrentSettings with { IsModeEnabled = enabled };
            return _modeRequest.Task;
        }

        public void CompleteRequest() => _modeRequest.TrySetResult();
        public void FailRequest(Exception exception) => _modeRequest.TrySetException(exception);
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SubscriberCountingSequence : IEnumerable<TemperaturePoint>, System.Collections.Specialized.INotifyCollectionChanged
    {
        private readonly List<TemperaturePoint> _items = [];
        private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _collectionChanged;

        public event System.Collections.Specialized.NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add { _collectionChanged += value; SubscriberCount++; }
            remove { _collectionChanged -= value; SubscriberCount--; }
        }

        public int SubscriberCount { get; private set; }

        public void Add(TemperaturePoint point)
        {
            _items.Add(point);
            _collectionChanged?.Invoke(this, new(System.Collections.Specialized.NotifyCollectionChangedAction.Add, point));
        }

        public IEnumerator<TemperaturePoint> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ThermalCoreUiCollection : ICollectionFixture<ThermalCoreStaFixture>
{
    public const string Name = "Thermal Core compiled UI";
}

public sealed class ThermalCoreStaFixture : IDisposable
{
    private readonly BlockingCollection<Action> _queue = [];
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private Exception? _startupFailure;
    private string _stage = "not started";
    private bool _disposed;

    public ThermalCoreStaFixture()
    {
        _thread = new Thread(ThreadMain) { IsBackground = true, Name = "LumaTherm WPF tests" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException($"STA host did not become ready; last stage: {_stage}.");
        if (_startupFailure is not null)
            throw new InvalidOperationException($"STA host startup failed at {_stage}.", _startupFailure);
    }

    public void Run(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Exception? failure = null;
        using var complete = new ManualResetEventSlim();
        _queue.Add(() =>
        {
            try
            {
                _stage = "executing test action";
                action();
                _stage = "test action completed";
            }
            catch (Exception exception) { failure = exception; }
            finally { complete.Set(); }
        });
        if (!complete.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException($"STA action did not complete; last stage: {_stage}.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    public void Dispose()
    {
        if (_disposed) return;
        Run(() => Application.Current.Shutdown());
        _disposed = true;
        _queue.CompleteAdding();
        if (!_thread.Join(TimeSpan.FromSeconds(10)))
            throw new TimeoutException($"STA host did not terminate; last stage: {_stage}.");
        _queue.Dispose();
        _ready.Dispose();
    }

    private void ThreadMain()
    {
        try
        {
            _stage = "constructing Application";
            var application = new LumaTherm.App.App(startHost: false);
            _stage = "initializing Application resources";
            application.InitializeComponent();
            _stage = "STA queue ready";
            _ready.Set();
            foreach (var action in _queue.GetConsumingEnumerable()) action();
            _stage = "STA queue stopped";
        }
        catch (Exception exception)
        {
            _startupFailure = exception;
            _stage = "STA startup failed";
            _ready.Set();
        }
    }

}
