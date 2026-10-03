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
    public void DenseProfileLabelsRemainReadableAtCompactWidth() => _sta.Run(() =>
    {
        var profile = ThermalProfile.Create(new[] { 20d, 64, 65, 66, 85 }.Select(temperature => new ThermalPoint(temperature, new RgbColor(0, 255, 0))), 0.8);
        var bar = Arrange(new ThermalGradientBar { Profile = profile }, 240, 100);
        _ = RenderChecksum(bar, 240, 100);
        Assert.InRange(bar.RenderedLabelBounds.Count, 2, 4);
        for (var index = 1; index < bar.RenderedLabelBounds.Count; index++)
            Assert.True(bar.RenderedLabelBounds[index].Left >= bar.RenderedLabelBounds[index - 1].Right + 9);
    });

    [Fact]
    public void Theme_ResolvesExactColorsAndFrozenBrushesAtRuntime() => _sta.Run(() =>
    {
        var expected = new Dictionary<string, string>
        {
            ["WindowBackground"] = "#FF131D25",
            ["RailBackground"] = "#FF14212B",
            ["PanelBackground"] = "#FF202D37",
            ["PanelSecondary"] = "#FF182630",
            ["PrimaryText"] = "#FFF4F7FA",
            ["SecondaryText"] = "#FFD5DDE3",
            ["MutedText"] = "#FFAEBAC4",
            ["ColdColor"] = "#FF47B4FF",
            ["WarmColor"] = "#FF3CFF00",
            ["HotColor"] = "#FFFF5D65",
            ["SuccessColor"] = "#FF61DDA6",
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
    public void Logo_UsesTwoArcticPeaks() => _sta.Run(() =>
    {
        var drawing = Assert.IsType<DrawingGroup>(Application.Current.Resources["LogoDrawing"]);
        Assert.Equal(2, drawing.Children.Count);
        Assert.Same(Application.Current.Resources["LogoBladeOneGeometry"], ((GeometryDrawing)drawing.Children[0]).Geometry);
        Assert.Same(Application.Current.Resources["LogoBladeTwoGeometry"], ((GeometryDrawing)drawing.Children[1]).Geometry);
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
            Assert.Equal(Application.Current.Resources["Accessibility.ToggleThermalSync"], AutomationProperties.GetName(toggle));
            Assert.NotEmpty(shell.IconOnlyButtons.Select(AutomationProperties.GetName));
            Assert.All(
                new[] { shell.MinimizeButton, shell.MaximizeButton, shell.CloseButton },
                button => Assert.NotNull(Assert.IsType<Path>(button.Content).Data));
            var description = Assert.IsType<TextBlock>(shell.FindName("AppDescriptionText"));
            Assert.Same(Application.Current.Resources["MutedTextBrush"], description.Foreground);
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

        Assert.Equal(241, bar.RenderedStops.Count);
        Assert.Equal(["35°", "65°", "85°"], bar.RenderedLabels);
        Assert.Equal([Color.FromRgb(0x50, 0xC8, 0xFF), Color.FromRgb(0xFF, 0xC6, 0x4A), Color.FromRgb(0xFF, 0x56, 0x5D)], new[] { bar.RenderedStops[0].Color, bar.RenderedStops[144].Color, bar.RenderedStops[^1].Color });
        Assert.NotEqual(BlankChecksum(600, 100), RenderChecksum(bar, 600, 100));
    });

    [Fact]
    public void GradientBar_RendersEveryPointFromItsProfile() => _sta.Run(() =>
    {
        var profile = ThermalProfile.Create(
            [new(20, new(0, 0, 255)), new(40, new(0, 255, 255)), new(60, new(0, 255, 0)), new(80, new(255, 0, 0))], 0.8);
        var bar = Arrange(new ThermalGradientBar { Profile = profile }, 600, 100);

        Assert.Equal(241, bar.RenderedStops.Count);
        Assert.Equal([0d, 1d / 3, 2d / 3, 1d], new[] { bar.RenderedStops[0].Offset, bar.RenderedStops[80].Offset, bar.RenderedStops[160].Offset, bar.RenderedStops[240].Offset });
        Assert.Equal([Color.FromRgb(0, 0, 255), Color.FromRgb(0, 255, 255), Color.FromRgb(0, 255, 0), Color.FromRgb(255, 0, 0)], new[] { bar.RenderedStops[0].Color, bar.RenderedStops[80].Color, bar.RenderedStops[160].Color, bar.RenderedStops[240].Color });
    });

    [Fact]
    public void GradientBar_PositionsProfileLabelsAtTheirTemperatureOffsets() => _sta.Run(() =>
    {
        var profile = ThermalProfile.Create(
            [new(20, new(0, 0, 255)), new(30, new(0, 255, 255)), new(80, new(255, 0, 0))], 0.8);
        var bar = Arrange(new ThermalGradientBar { Profile = profile }, 600, 100);

        Assert.Equal([0d, 1d / 6, 1d], bar.RenderedLabelOffsets);
    });

    [Fact]
    public void GradientBar_LegacyEqualOuterTemperaturesKeepLabelOffsetsAndRenderingFinite() => _sta.Run(() =>
    {
        var bar = Arrange(new ThermalGradientBar
        {
            ColdTemperature = 40,
            WarmTemperature = 40,
            HotTemperature = 40,
        }, 600, 100);

        Assert.All(bar.RenderedLabelOffsets, offset => Assert.True(double.IsFinite(offset)));
        Assert.NotEqual(BlankChecksum(600, 100), RenderChecksum(bar, 600, 100));
    });



    [Fact]
    public void Sparkline_UsesActualTimeSpacingAndStableTemperatureScale() => _sta.Run(() =>
    {
        var sparkline = Arrange(new TemperatureSparkline { ItemsSource = new[] {
            new TemperaturePoint(DateTimeOffset.UnixEpoch, 0),
            new TemperaturePoint(DateTimeOffset.UnixEpoch.AddSeconds(15), 60),
            new TemperaturePoint(DateTimeOffset.UnixEpoch.AddSeconds(60), 120),
        } }, 634, 132);
        Assert.Equal(new Point(30, 126), sparkline.ProjectedPoints[0]);
        Assert.Equal(new Point(180, 66), sparkline.ProjectedPoints[1]);
        Assert.Equal(new Point(630, 6), sparkline.ProjectedPoints[2]);
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
        Assert.Equal(sparkline.FindResource("Dashboard.History"), AutomationProperties.GetName(sparkline));
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
    [InlineData(800, true)]
    public void Dashboard_ReflowsAtEightHundredTwentyPixels(double width, bool compact) => _sta.Run(() =>
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
    [InlineData(819, 552, true)]
    [InlineData(820, 652, false)]
    [InlineData(821, 652, false)]
    [InlineData(740, 552, true)]
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
        view.HistorySparkline.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
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

        var summary = Assert.IsType<TextBlock>(view.FindName("ProfilePointsSummaryText")).Text;
        Assert.Contains("35° #006BFF", summary);
        Assert.Contains("65° #3CFF00", summary);
        Assert.Contains("85° #FF0000", summary);
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
            Assert.Equal(Color.FromRgb(0xB0, 0xBB, 0xC4), Assert.IsType<SolidColorBrush>(dot.Fill).Color);

            runtime.Publish(Snapshot(RuntimeStatus.Active, 68, new RgbColor(0xFF, 0xC6, 0x4A)));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

            Assert.Equal(Color.FromRgb(0x61, 0xDD, 0xA6), Assert.IsType<SolidColorBrush>(dot.Fill).Color);
        }
        finally
        {
            shell.Close();
        }
    });

    [Fact]
    public void NavigationSelectionIsVisibleAndFollowsTheActiveView() => _sta.Run(() =>
    {
        using var vm = new MainViewModel(new FakeRuntime());
        var shell = new MainWindow { DataContext = vm };
        try
        {
            Arrange(Assert.IsType<Border>(shell.Content), 1180, 720);
            var workspace = Assert.IsType<Grid>(shell.FindName("WorkspaceSurface"));
            Assert.Same(Application.Current.Resources["WindowBackgroundBrush"], workspace.Background);
            var indicator = Assert.IsType<Border>(shell.FindName("HomeSelectionIndicator"));
            Assert.Equal(2, indicator.Width);
            Assert.Equal(26, indicator.Height);
            Assert.Same(Application.Current.Resources["ColdColorBrush"], indicator.Background);
            Assert.Null(indicator.Effect);
            var settingsIndicator = Assert.IsType<Border>(shell.FindName("SettingsSelectionIndicator"));
            Assert.Equal(Visibility.Collapsed, settingsIndicator.Visibility);
            Assert.IsType<Button>(shell.FindName("SettingsButton")).Command.Execute(null);
            Assert.Equal(Visibility.Collapsed, indicator.Visibility);
            Assert.Equal(Visibility.Visible, settingsIndicator.Visibility);
        }
        finally { shell.Close(); }
    });

    [Fact]
    public void Sparkline_RendersBlueAreaBelowActualLine() => _sta.Run(() =>
    {
        var points = new ObservableCollection<TemperaturePoint>
        {
            new(DateTimeOffset.UnixEpoch, 60),
            new(DateTimeOffset.UnixEpoch.AddSeconds(30), 60),
            new(DateTimeOffset.UnixEpoch.AddSeconds(60), 60),
        };
        var sparkline = Arrange(new TemperatureSparkline { ItemsSource = points }, 520, 107);

        var belowLine = RenderPixel(sparkline, 520, 107, 260, 80);

        Assert.True(belowLine.A > 0);
        Assert.True(belowLine.B > belowLine.R);
        Assert.True(belowLine.B > belowLine.G);
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
        public Task UpdatePreferencesAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
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
        public Task UpdatePreferencesAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
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
