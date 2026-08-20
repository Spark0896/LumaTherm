using System.IO;
using System.Windows;
using LumaTherm.App.Services;
using LumaTherm.App.ViewModels;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Diagnostics;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Sensors;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;
using LumaTherm.Infrastructure.Diagnostics;
using LumaTherm.Infrastructure.Lighting;
using LumaTherm.Infrastructure.Sensors;
using LumaTherm.Infrastructure.Settings;
using LumaTherm.Infrastructure.System;

namespace LumaTherm.App.Composition;

public static class ProductionAppServices
{
    public static AppServices Create(System.Windows.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        IAppLogger? logger = null;
        ILightingController? lighting = null;
        ILightingDeviceDiscovery? lightingDiscovery = null;
        var localData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaTherm");

        return new AppServices(
            () => logger = new RollingFileLogger(Path.Combine(localData, "logs", "lumatherm.log")),
            appLogger => new SingleInstanceAdapter(new SingleInstanceCoordinator(errorSink: exception => WriteFailure(appLogger, "app.unhandled", exception))),
            () => new FileSessionSentinel(Path.Combine(localData, "session.lock")),
            () => new LoggingSettingsStore(new JsonSettingsStore(Path.Combine(localData, "settings.json"), TimeProvider.System), () => logger),
            (settings, store) =>
            {
                var sources = new ITemperatureSource[]
                {
                    new NvmlTemperatureSource(new NvmlApi(), TimeProvider.System),
                    new AfterburnerTemperatureSource(new MahmMemoryReader(), TimeProvider.System),
                };
                var platform = new WindowsLampArrayPlatform();
                lighting = new LampArrayLightingController(platform);
                lightingDiscovery = new LightingDeviceDiscoveryService(lighting);
                return new ThermalRuntime(
                    new TemperatureProvider(sources),
                    new ColorEngine(settings.Profile, settings.Profile.ColdTemperature),
                    lighting,
                    settings,
                    store,
                    TimeProvider.System);
            },
            _ => new LoggingStartupService(CreateStartupService(), () => logger),
            (runtime, startup, settings) => new WpfUiSession(runtime, startup, settings, lightingDiscovery ?? EmptyLightingDeviceDiscovery.Instance),
            (ui, runtime) => new WpfTraySession((WpfUiSession)ui, runtime, application, exception => WriteFailure(logger, "app.unhandled", exception)),
            runtime => new PowerEventService(new WindowsPowerEventSource(), runtime, exception => WriteFailure(logger, "runtime.power_failed", exception)),
            (_, _) => new ReadOnlyDiscoverySession(lighting ?? throw new InvalidOperationException("Lighting composition is unavailable."), () => logger),
            new WpfAppDispatcher(application.Dispatcher));
    }

    private static IStartupService CreateStartupService()
    {
        var executablePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "LumaTherm.exe");
        return new StartupService(
            new WindowsPackageIdentityProbe(),
            new PackagedStartupService(new WindowsStartupTaskPlatform()),
            new RegistryStartupService(new WindowsRegistryRunPlatform(), executablePath));
    }

    private static void WriteFailure(IAppLogger? logger, string eventName, Exception exception)
    {
        try { logger?.Write(AppLogLevel.Error, eventName, exception.Message, exception); }
        catch { }
    }

    private sealed class SingleInstanceAdapter(SingleInstanceCoordinator coordinator) : IAppInstanceCoordinator
    {
        public event EventHandler? ActivationRequested
        {
            add => coordinator.ActivationRequested += value;
            remove => coordinator.ActivationRequested -= value;
        }
        public Task<bool> TryAcquireAsync(CancellationToken cancellationToken) => coordinator.TryAcquireAsync(cancellationToken);
        public Task SignalActivationAsync(CancellationToken cancellationToken) => coordinator.SignalActivationAsync(cancellationToken);
        public ValueTask DisposeAsync() => coordinator.DisposeAsync();
    }

    private sealed class WpfUiSession : IAppUiSession
    {
        private readonly MainViewModel _mainViewModel;
        private readonly SettingsViewModel _settingsViewModel;
        public WpfUiSession(IThermalRuntime runtime, IStartupService startup, AppSettings settings, ILightingDeviceDiscovery discovery)
        {
            _settingsViewModel = new SettingsViewModel(runtime, startup, settings, new ColorPickerService(), discovery);
            _mainViewModel = new MainViewModel(runtime, SynchronizationContext.Current);
            _mainViewModel.SynchronizeProfile(_settingsViewModel);
            ClosePolicy = new WindowClosePolicy(() => _settingsViewModel.LiveSettings.MinimizeToTray);
            Window = new MainWindow
            {
                DataContext = _mainViewModel,
                SettingsDataContext = _settingsViewModel,
                ClosePolicy = ClosePolicy,
            };
            System.Windows.Application.Current.MainWindow = Window;
        }

        public MainWindow Window { get; }
        public WindowClosePolicy ClosePolicy { get; }
        public SettingsViewModel Settings => _settingsViewModel;
        public void Show() => Window.Show();
        public void ShowRestoreActivate()
        {
            var adapter = new WpfTrayWindow(Window);
            if (!adapter.IsVisible) adapter.Show();
            if (adapter.IsMinimized) adapter.Restore();
            adapter.Activate();
            adapter.BringToFront();
        }
        public void ShowForegroundError(string message) => System.Windows.MessageBox.Show(Window, message, "LumaTherm", MessageBoxButton.OK, MessageBoxImage.Error);
        public void Dispose()
        {
            _mainViewModel.Dispose();
            ClosePolicy.RequestExplicitExit();
            Window.Close();
        }
    }

    private sealed class WpfTraySession : IAppTraySession
    {
        private readonly TrayIconService _service;
        public WpfTraySession(WpfUiSession ui, IThermalRuntime runtime, System.Windows.Application application, Action<Exception> failure)
        {
            _service = new TrayIconService(
                new NotifyIconTrayPlatform(),
                new WpfTrayWindow(ui.Window),
                new WpfTrayApplication(application),
                runtime,
                () => runtime.SetModeEnabledAsync(!runtime.CurrentSettings.IsModeEnabled, CancellationToken.None),
                ui.ClosePolicy,
                () => ui.Settings.LiveSettings.NotificationsEnabled,
                failure);
        }
        public void ShowRecoveryWarning(string message) => _service.ShowRecoveryWarning(message);
        public void ShowBackgroundError(string message) => _service.ShowNotification("LumaTherm · Ошибка", message);
        public ValueTask DisposeAsync() => _service.DisposeAsync();
    }

    private sealed class ReadOnlyDiscoverySession(ILightingController lighting, Func<IAppLogger?> logger) : IAppDiscoverySession
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                var devices = await lighting.DiscoverAsync(cancellationToken).ConfigureAwait(false);
                logger()?.Write(AppLogLevel.Information, devices.Count > 0 ? "lamp.connected" : "lamp.disconnected", $"LampArray discovery found {devices.Count} device(s).");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger()?.Write(AppLogLevel.Warning, "lamp.disconnected", "LampArray discovery failed.", exception);
            }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class LoggingSettingsStore(ISettingsStore inner, Func<IAppLogger?> logger) : ISettingsStore
    {
        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) => inner.LoadAsync(cancellationToken);
        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            await inner.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            logger()?.Write(AppLogLevel.Information, "settings.saved", "Settings saved.");
        }
    }

    private sealed class LoggingStartupService(IStartupService inner, Func<IAppLogger?> logger) : IStartupService
    {
        public Task<bool> GetEnabledAsync(CancellationToken cancellationToken) => inner.GetEnabledAsync(cancellationToken);
        public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            await inner.SetEnabledAsync(enabled, cancellationToken).ConfigureAwait(false);
            logger()?.Write(AppLogLevel.Information, "startup.changed", "Startup setting changed.", data: new Dictionary<string, object?> { ["enabled"] = enabled });
        }
    }
}
