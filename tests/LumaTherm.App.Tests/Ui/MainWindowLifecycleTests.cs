using System.Windows;
using System.Windows.Threading;
using LumaTherm.App.Composition;
using LumaTherm.App.Localization;
using LumaTherm.App.Services;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Sensors;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;
using LumaTherm.Infrastructure.Lighting;

namespace LumaTherm.App.Tests.Ui;

[Collection(ThermalCoreUiCollection.Name)]
public sealed class MainWindowLifecycleTests(ThermalCoreStaFixture sta)
{
    [Fact]
    public void CloseAndMinimize_HideToTrayUntilExplicitExit()
    {
        sta.Run(() =>
        {
            var policy = new WindowClosePolicy(() => true);
            var window = new MainWindow { ClosePolicy = policy };
            window.Show();

            window.Close();
            Assert.False(window.IsVisible);
            Assert.True(window.IsLoaded);

            window.Show();
            window.WindowState = WindowState.Minimized;
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.False(window.IsVisible);
            Assert.True(window.IsLoaded);

            policy.RequestExplicitExit();
            window.Show();
            window.Close();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.False(window.IsLoaded);
        });
    }

    [Fact]
    public void ProductionLifecycle_MinimizeAndDeactivateRetainRuntimeAndLampArrayOwnershipUntilShutdown()
    {
        sta.Run(() =>
        {
            var settings = AppSettings.Default with { Language = AppLanguage.English };
            var settingsStore = new FixedSettingsStore(settings);
            var lampHandle = new TrackingLampHandle();
            var lampPlatform = new TrackingLampPlatform(lampHandle);
            var lighting = new LampArrayLightingController(lampPlatform);
            var innerRuntime = new ThermalRuntime(
                new TemperatureProvider([new EmptyTemperatureSource()]),
                new ColorEngine(settings.Profile, settings.Profile.ColdTemperature),
                lighting,
                settings,
                settingsStore,
                TimeProvider.System);
            var runtime = new TrackingRuntime(innerRuntime);
            ProductionAppServices.WpfUiSession? ui = null;
            TrayIconService? tray = null;
            try
            {
                Assert.True(lighting.ConnectAsync(null, CancellationToken.None).GetAwaiter().GetResult());
                ui = new ProductionAppServices.WpfUiSession(
                    runtime,
                    new FixedStartupService(),
                    settings,
                    new LightingDeviceDiscoveryService(lighting));
                var trayPlatform = new TrackingTrayPlatform();
                var application = new TrackingTrayApplication();
                var localization = new LocalizationService(Application.Current.Resources);
                localization.Apply(settings.Language);
                tray = new TrayIconService(
                    trayPlatform,
                    new WpfTrayWindow(ui.Window),
                    application,
                    runtime,
                    localization,
                    () => ui.Settings.LiveSettings,
                    ui.Settings,
                    () => runtime.SetModeEnabledAsync(!runtime.CurrentSettings.IsModeEnabled, CancellationToken.None),
                    ui.ClosePolicy,
                    () => ui.Settings.LiveSettings.NotificationsEnabled);
                ui.Show();

                SimulateDeactivated(ui.Window);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

                Assert.True(ui.Window.IsVisible);
                Assert.True(ui.Window.IsLoaded);
                Assert.Equal(0, runtime.StopCalls);
                Assert.Equal(0, runtime.DisposeCalls);
                Assert.Equal(0, lampHandle.DisableCalls);
                Assert.Equal(0, lampPlatform.DisposeCalls);

                ui.Window.WindowState = WindowState.Minimized;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

                Assert.False(ui.Window.IsVisible);
                Assert.True(ui.Window.IsLoaded);
                Assert.Equal(0, runtime.StopCalls);
                Assert.Equal(0, runtime.DisposeCalls);
                Assert.Equal(0, lampHandle.DisableCalls);
                Assert.Equal(0, lampPlatform.DisposeCalls);

                trayPlatform.RaiseExit();
                WaitWithUiPump(application.ShutdownRequested.Task);

                Assert.Equal(1, runtime.StopCalls);
                Assert.Equal(0, runtime.DisposeCalls);
                Assert.Equal(1, lampHandle.DisableCalls);
                Assert.Equal(1, lampPlatform.DisposeCalls);
            }
            finally
            {
                if (tray is not null) WaitWithUiPump(tray.DisposeAsync().AsTask());
                if (ui is not null) WaitWithUiPump(ui.DisposeAsync().AsTask());
                WaitWithUiPump(runtime.DisposeAsync().AsTask());
            }

            Assert.Equal(1, runtime.DisposeCalls);
        });
    }

    private static void SimulateDeactivated(Window window) =>
        typeof(Window).GetMethod("OnDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, [EventArgs.Empty]);

    private static void WaitWithUiPump(Task task)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => frame.Continue = false;
        task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.True(task.IsCompleted, "UI operation did not complete within the bounded dispatcher pump.");
        task.GetAwaiter().GetResult();
    }

    private sealed class TrackingRuntime(IThermalRuntime inner) : IThermalRuntime
    {
        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged
        {
            add => inner.SnapshotChanged += value;
            remove => inner.SnapshotChanged -= value;
        }
        public RuntimeSnapshot CurrentSnapshot => inner.CurrentSnapshot;
        public AppSettings CurrentSettings => inner.CurrentSettings;
        public Task StartAsync(CancellationToken cancellationToken) => inner.StartAsync(cancellationToken);
        public Task<ILightingTestSession> BeginLightingTestAsync(CancellationToken cancellationToken) =>
            inner.BeginLightingTestAsync(cancellationToken);
        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken) =>
            inner.SetModeEnabledAsync(enabled, cancellationToken);
        public Task UpdatePreferencesAsync(AppSettings preferences, CancellationToken cancellationToken) =>
            inner.UpdatePreferencesAsync(preferences, cancellationToken);
        public Task SuspendAsync(CancellationToken cancellationToken) => inner.SuspendAsync(cancellationToken);
        public Task ResumeAsync(CancellationToken cancellationToken) => inner.ResumeAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            return inner.StopAsync(cancellationToken);
        }
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return inner.DisposeAsync();
        }
    }

    private sealed class EmptyTemperatureSource : ITemperatureSource
    {
        public string Name => "empty";
        public ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<TemperatureReading?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FixedSettingsStore(AppSettings settings) : ISettingsStore
    {
        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsLoadResult(settings));
        public Task SaveAsync(AppSettings value, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedStartupService : IStartupService
    {
        public Task<bool> GetEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TrackingLampPlatform(TrackingLampHandle handle) : ILampArrayPlatform
    {
        public int DisposeCalls { get; private set; }
        public event EventHandler? DevicesChanged { add { } remove { } }
        public Task<IReadOnlyList<ILampArrayHandle>> FindAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ILampArrayHandle>>([handle]);
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingLampHandle : ILampArrayHandle
    {
        public string Id => "lamp";
        public string Name => "Test LampArray";
        public int LampCount => 12;
        public bool IsAvailable => true;
        public bool IsPresent => true;
        public int DisableCalls { get; private set; }
        public void Enable() { }
        public void SetColor(RgbColor color) { }
        public void Disable() => DisableCalls++;
    }

    private sealed class TrackingTrayPlatform : ITrayIconPlatform
    {
        public event EventHandler? LeftClick { add { } remove { } }
        public event EventHandler? DoubleClick { add { } remove { } }
        public event EventHandler<TrayCommandKind>? CommandRequested;
        public bool Visible { get; set; }
        public TrayMenuState? MenuState { get; set; }
        public void ShowNotification(string title, string message) { }
        public void Dispose() { }
        public void RaiseExit() => CommandRequested?.Invoke(this, TrayCommandKind.Exit);
    }

    private sealed class TrackingTrayApplication : ITrayApplication
    {
        public TaskCompletionSource ShutdownRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void RequestShutdown() => ShutdownRequested.TrySetResult();
    }
}
