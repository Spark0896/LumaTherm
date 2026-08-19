using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        var toggle = Assert.IsType<Button>(view.FindName("ModeToggle"));
        var shell = new MainWindow { DataContext = vm };

        try
        {
            Assert.Same(vm.ToggleModeCommand, toggle.Command);
            Assert.Equal("Включить или выключить термосинхронизацию", AutomationProperties.GetName(toggle));
            Assert.NotEmpty(shell.IconOnlyButtons.Select(AutomationProperties.GetName));
            Assert.All(shell.IconOnlyButtons, button => Assert.Matches("[А-Яа-яЁё]", AutomationProperties.GetName(button)));
        }
        finally
        {
            shell.Close();
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
        var oldItems = new ObservableCollection<TemperaturePoint>();
        var currentItems = new ObservableCollection<TemperaturePoint>();
        var sparkline = Arrange(new TemperatureSparkline { ItemsSource = oldItems }, 520, 107);
        sparkline.ItemsSource = currentItems;
        var before = sparkline.ObservedCollectionChangeCount;

        oldItems.Add(new(DateTimeOffset.UnixEpoch, 20));
        Assert.Equal(before, sparkline.ObservedCollectionChangeCount);
        currentItems.Add(new(DateTimeOffset.UnixEpoch, 60));
        Assert.Equal(before + 1, sparkline.ObservedCollectionChangeCount);
        var onePoint = RenderChecksum(sparkline, 520, 107);
        currentItems.Add(new(DateTimeOffset.UnixEpoch.AddSeconds(1), 60));
        currentItems.Add(new(DateTimeOffset.UnixEpoch.AddSeconds(2), 60));
        var flat = RenderChecksum(sparkline, 520, 107);

        Assert.NotEqual(onePoint, flat);
        Assert.Equal("График температуры GPU за последнюю минуту", AutomationProperties.GetName(sparkline));
        sparkline.ItemsSource = Array.Empty<TemperaturePoint>();
        _ = RenderChecksum(sparkline, 520, 107);
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

    [Fact]
    public void RealMainViewModelUpdateChangesVisibleDashboardProjection() => _sta.Run(() =>
    {
        var runtime = new FakeRuntime();
        using var vm = new MainViewModel(runtime, new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var view = Arrange(new DashboardView { DataContext = vm }, 1104, 652);

        runtime.Publish(Snapshot(RuntimeStatus.Active, 68, new RgbColor(0xFF, 0xC6, 0x4A)));
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

        Assert.Equal("68°C", view.CurrentTemperatureText.Text);
        Assert.Equal("#FFC64A", view.CurrentColorText.Text);
        Assert.Equal("Режим активен", view.ModeStatusText.Text);
        Assert.Equal(68, view.TemperatureRing.Temperature);
        Assert.Same(vm.History, view.HistorySparkline.ItemsSource);
        Assert.Single(vm.History);
    });

    [Fact]
    public void Dashboard_ProfileStopsDisplayHexColors() => _sta.Run(() =>
    {
        using var vm = new MainViewModel(new FakeRuntime());
        var view = Arrange(new DashboardView { DataContext = vm }, 1104, 652);

        Assert.Equal("#50C8FF", Assert.IsType<TextBlock>(view.FindName("ColdColorText")).Text);
        Assert.Equal("#FFC64A", Assert.IsType<TextBlock>(view.FindName("WarmColorText")).Text);
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
            var application = new LumaTherm.App.App();
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
