using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LumaTherm.App.Controls;
using LumaTherm.App.ViewModels;
using LumaTherm.App.Views;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Settings;

namespace LumaTherm.App.Tests.Ui;

[Collection(ThermalCoreUiCollection.Name)]
public sealed class LightingTestWindowTests(ThermalCoreStaFixture sta)
{
    [Fact]
    public void Window_UsesProfileEditorPreviewAndExactTemperatureRange() => sta.Run(() =>
    {
        var vm = new LightingTestViewModel(new FakeRuntime(), ThermalProfile.Default, (_, _) => Task.CompletedTask);
        var window = new LightingTestWindow(vm);
        try
        {
            Arrange(Assert.IsAssignableFrom<FrameworkElement>(window.Content), 760, 620);
            var slider = Assert.IsType<Slider>(window.FindName("TestTemperatureSlider"));
            var value = Assert.IsType<TextBlock>(window.FindName("TestTemperatureValue"));
            var preview = Assert.IsType<Border>(window.FindName("PreviewColor"));
            var editor = Assert.IsType<ThermalProfileEditor>(window.FindName("ProfileEditorControl"));

            Assert.Equal(0, slider.Minimum);
            Assert.Equal(120, slider.Maximum);
            Assert.True(slider.IsMoveToPointEnabled);
            Assert.True(slider.ActualHeight >= 40);
            slider.ApplyTemplate();
            var track = Assert.IsType<Track>(slider.Template.FindName("PART_Track", slider));
            Assert.True(track.Thumb.ActualWidth >= 24);
            Assert.True(track.Thumb.ActualHeight >= 28);
            AssertBinding(slider, Slider.ValueProperty, nameof(LightingTestViewModel.TestTemperature));
            AssertBinding(value, TextBlock.TextProperty, nameof(LightingTestViewModel.TestTemperature));
            AssertBinding(preview, Border.BackgroundProperty, nameof(LightingTestViewModel.PreviewColor));
            Assert.Same(vm.Editor, editor.DataContext);
            Assert.Equal(Application.Current.Resources["TestWindow.Title"], window.Title);
        }
        finally
        {
            window.CloseAfterCleanup();
        }
    });

    [Fact]
    public void Window_SelectedPointEditorExposesMouseReachableTemperatureAndColorControls() => sta.Run(() =>
    {
        var vm = new LightingTestViewModel(new FakeRuntime(), ThermalProfile.Default, (_, _) => Task.CompletedTask);
        var window = new LightingTestWindow(vm);
        try
        {
            Arrange(Assert.IsAssignableFrom<FrameworkElement>(window.Content), 760, 650);
            var temperature = Assert.IsType<TextBox>(window.FindName("SelectedPointTemperatureEditor"));
            var color = Assert.IsType<Button>(window.FindName("SelectedPointColorButton"));

            AssertBinding(temperature, TextBox.TextProperty, nameof(LightingTestViewModel.SelectedPointTemperature));
            Assert.Same(vm.PickSelectedColorCommand, color.Command);
            Assert.True(temperature.IsHitTestVisible);
            Assert.True(color.IsHitTestVisible);
            Assert.True(temperature.Focusable);
            Assert.True(color.Focusable);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(temperature)));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(color)));
        }
        finally
        {
            window.CloseAfterCleanup();
        }
    });

    [Fact]
    public void Window_VisibleActionsUseLocalizedResourcesAndAccessibleNames() => sta.Run(() =>
    {
        var vm = new LightingTestViewModel(new FakeRuntime(), ThermalProfile.Default, (_, _) => Task.CompletedTask);
        var window = new LightingTestWindow(vm);
        try
        {
            foreach (var (name, resource) in new[]
            {
                ("ApplyButton", "TestWindow.Apply"),
                ("CancelButton", "TestWindow.Cancel"),
            })
            {
                var button = Assert.IsType<Button>(window.FindName(name));
                Assert.Equal(Application.Current.Resources[resource], button.Content);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                Assert.True(button.Focusable);
            }

            var apply = Assert.IsType<Button>(window.FindName("ApplyButton"));
            var cancel = Assert.IsType<Button>(window.FindName("CancelButton"));
            var close = Assert.IsType<Button>(window.FindName("CloseButton"));
            Assert.Same(Application.Current.Resources["PrimaryActionButtonStyle"], apply.Style);
            Assert.Same(Application.Current.Resources["ActionButtonStyle"], cancel.Style);
            Assert.Same(Application.Current.Resources["WindowButtonStyle"], close.Style);
            Assert.NotNull(Assert.IsType<Path>(close.Content).Data);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(close)));
            Assert.True(close.Focusable);
        }
        finally
        {
            window.CloseAfterCleanup();
        }
    });

    [Fact]
    public void FirstClose_IsCancelledUntilAsyncCleanupCompletesThenReissued() => sta.Run(() =>
    {
        var runtime = new FakeRuntime(blockDispose: true);
        var vm = new LightingTestViewModel(runtime, ThermalProfile.Default, (_, _) => Task.CompletedTask);
        vm.OpenAsync().GetAwaiter().GetResult();
        var window = new LightingTestWindow(vm) { ShowInTaskbar = false };
        window.Show();

        window.Close();

        Assert.True(window.IsVisible);
        Assert.True(runtime.Session.DisposeStarted);
        Assert.False(runtime.Session.Disposed);

        runtime.Session.CompleteDispose();
        PumpUntil(() => !window.IsVisible);
        Assert.False(window.IsLoaded);
        Assert.True(runtime.Session.Disposed);
        Assert.Equal(1, runtime.Session.DisposeCalls);
    });

    [Fact]
    public void PrepareCloseAsync_WhileBeginIsBlocked_AwaitsLateSessionDisposalBeforeShutdownMayClose() => sta.Run(() =>
    {
        var runtime = new FakeRuntime(blockBegin: true);
        var vm = new LightingTestViewModel(runtime, ThermalProfile.Default, (_, _) => Task.CompletedTask);
        var opening = vm.OpenAsync();
        Assert.True(runtime.BeginStarted.Task.IsCompleted);
        var window = new LightingTestWindow(vm) { ShowInTaskbar = false };
        window.Show();

        var cleanup = window.PrepareCloseAsync();

        Assert.False(cleanup.IsCompleted);
        Assert.True(window.IsVisible);
        Assert.False(runtime.Session.Disposed);

        runtime.CompleteBegin();
        PumpUntil(() => cleanup.IsCompleted);
        cleanup.GetAwaiter().GetResult();
        opening.GetAwaiter().GetResult();
        Assert.True(runtime.Session.Disposed);
        Assert.Equal(1, runtime.Session.DisposeCalls);

        window.CloseAfterCleanup();
        Assert.False(window.IsVisible);
    });

    [Fact]
    public void ShutdownPump_AfterObservationTimeout_ContinuesUntilCleanupCompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        sta.Run(() =>
        {
            var cleanup = Task.Delay(75, cancellationToken);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var priorContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try
            {
                LumaTherm.App.App.WaitWithDispatcherPumpUntilCompleted(
                    cleanup,
                    TimeSpan.FromMilliseconds(5));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(priorContext);
            }

            elapsed.Stop();
            Assert.True(cleanup.IsCompletedSuccessfully);
            Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(50));
        });
    }

    private static void AssertBinding(FrameworkElement element, DependencyProperty property, string path)
    {
        var expression = BindingOperations.GetBindingExpression(element, property);
        Assert.NotNull(expression);
        Assert.Equal(path, expression.ParentBinding.Path.Path);
    }

    private static T Arrange<T>(T element, double width, double height) where T : FrameworkElement
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        return element;
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

    private sealed class FakeRuntime(bool blockDispose = false, bool blockBegin = false) : IThermalRuntime
    {
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged { add { } remove { } }
        public RuntimeSnapshot CurrentSnapshot { get; } = new(RuntimeStatus.Disabled, null, null, null, null, null, DateTimeOffset.MinValue, false);
        public AppSettings CurrentSettings { get; } = AppSettings.Default;
        public FakeSession Session { get; } = new(blockDispose);
        public TaskCompletionSource BeginStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ILightingTestSession>? _begin = blockBegin
            ? new(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
        public Task<ILightingTestSession> BeginLightingTestAsync(CancellationToken cancellationToken)
        {
            BeginStarted.TrySetResult();
            return _begin?.Task ?? Task.FromResult<ILightingTestSession>(Session);
        }
        public void CompleteBegin() => _begin?.TrySetResult(Session);
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdatePreferencesAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSession(bool blockDispose) : ILightingTestSession
    {
        private readonly TaskCompletionSource _dispose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool DisposeStarted { get; private set; }
        public bool Disposed { get; private set; }
        public int DisposeCalls { get; private set; }
        public Task SetTemperatureAsync(double celsius, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetProfileAsync(ThermalProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            DisposeStarted = true;
            DisposeCalls++;
            if (blockDispose) await _dispose.Task;
            Disposed = true;
        }

        public void CompleteDispose() => _dispose.TrySetResult();
    }
}
