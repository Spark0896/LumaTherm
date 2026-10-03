using System.IO;
using System.Windows;
using LumaTherm.App.Localization;
using LumaTherm.App.Services;
using LumaTherm.App.ViewModels;
using LumaTherm.App.Views;
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
using LumaTherm.Infrastructure.Updates;

namespace LumaTherm.App.Composition;

public static class ProductionAppServices
{
    public static AppServices Create(System.Windows.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        IAppLogger? logger = null;
        ILightingController? lighting = null;
        ILightingDeviceDiscovery? lightingDiscovery = null;
        var localization = new LocalizationService(application.Resources);
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
            (runtime, startup, settings) =>
            {
                localization.Apply(settings.Language);
                return new WpfUiSession(runtime, startup, settings, lightingDiscovery ?? EmptyLightingDeviceDiscovery.Instance, localization);
            },
            (ui, runtime) => new WpfTraySession((WpfUiSession)ui, runtime, application, localization, exception => WriteFailure(logger, "app.unhandled", exception)),
            runtime => new PowerEventService(new WindowsPowerEventSource(), runtime, exception => WriteFailure(logger, "runtime.power_failed", exception)),
            (_, _) => new ReadOnlyDiscoverySession(lighting ?? throw new InvalidOperationException("Lighting composition is unavailable."), () => logger),
            new WpfAppDispatcher(application.Dispatcher),
            localization.Get);
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

    internal sealed class WpfUiSession : IAppUiSession
    {
        private readonly IThermalRuntime _runtime;
        private readonly MainViewModel _mainViewModel;
        private readonly SettingsViewModel _settingsViewModel;
        private readonly AboutViewModel _aboutViewModel;
        private readonly GitHubReleaseFeed _releaseFeed;
        private readonly ILocalizationService _localization;
        private LightingTestViewModel? _lightingTestViewModel;
        private LightingTestWindow? _lightingTestWindow;
        private Task? _disposeTask;
        private bool _isDisposing;
        public WpfUiSession(IThermalRuntime runtime, IStartupService startup, AppSettings settings, ILightingDeviceDiscovery discovery, ILocalizationService? localization = null)
        {
            _runtime = runtime;
            _localization = localization ?? new LocalizationService(System.Windows.Application.Current.Resources);
            if (localization is null) _localization.Apply(settings.Language);
            _settingsViewModel = new SettingsViewModel(runtime, startup, settings, new ColorPickerService(), discovery, _localization);
            _mainViewModel = new MainViewModel(runtime, SynchronizationContext.Current, _localization);
            _releaseFeed = new GitHubReleaseFeed();
            _aboutViewModel = new AboutViewModel(_releaseFeed, new LinkLauncher(), _localization);
            _mainViewModel.SynchronizeProfile(_settingsViewModel);
            ClosePolicy = new WindowClosePolicy(() => _settingsViewModel.LiveSettings.MinimizeToTray);
            Window = new MainWindow
            {
                DataContext = _mainViewModel,
                SettingsDataContext = _settingsViewModel,
                AboutDataContext = _aboutViewModel,
                ClosePolicy = ClosePolicy,
            };
            System.Windows.Application.Current.MainWindow = Window;
            Window.Closed += OnMainWindowClosed;
            Window.Activated += OnMainWindowActivated;
            _settingsViewModel.LightingTestRequested += OnLightingTestRequested;
        }

        public MainWindow Window { get; }
        public WindowClosePolicy ClosePolicy { get; }
        public SettingsViewModel Settings => _settingsViewModel;
        public void Show() => Window.Show();
        private void OnMainWindowClosed(object? sender, EventArgs args)
        {
            // With explicit shutdown, closing the last window otherwise leaves
            // a headless process and a tray icon pointing to a disposed window.
            if (!_isDisposing)
            {
                if (System.Windows.Application.Current is App app) app.RequestShutdown();
                else System.Windows.Application.Current.Shutdown();
            }
        }
        private async void OnMainWindowActivated(object? sender, EventArgs args)
        {
            if (_isDisposing) return;
            try { await _settingsViewModel.RefreshAutostartAfterActivationAsync(); }
            catch (Exception) { /* Initialization reports read failures without interrupting the window. */ }
        }
        public void ShowRestoreActivate()
        {
            var adapter = new WpfTrayWindow(Window);
            if (!adapter.IsVisible) adapter.Show();
            if (adapter.IsMinimized) adapter.Restore();
            adapter.Activate();
            adapter.BringToFront();
        }
        public void ShowForegroundError(string message) => System.Windows.MessageBox.Show(Window, message, "LumaTherm", MessageBoxButton.OK, MessageBoxImage.Error);
        public ValueTask DisposeAsync()
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }

        private async Task DisposeCoreAsync()
        {
            _isDisposing = true;
            Window.Closed -= OnMainWindowClosed;
            Window.Activated -= OnMainWindowActivated;
            _settingsViewModel.LightingTestRequested -= OnLightingTestRequested;
            var lightingTestWindow = _lightingTestWindow;
            if (lightingTestWindow is not null)
            {
                try
                {
                    await lightingTestWindow.PrepareCloseAsync();
                }
                catch (Exception)
                {
                    // Cleanup has completed its disposal attempt; the ViewModel retains the failure.
                }

                lightingTestWindow.CloseAfterCleanup();
                if (ReferenceEquals(_lightingTestWindow, lightingTestWindow))
                {
                    _lightingTestWindow = null;
                    _lightingTestViewModel = null;
                }
            }

            _settingsViewModel.Dispose();
            _mainViewModel.Dispose();
            _aboutViewModel.Dispose();
            _releaseFeed.Dispose();
            ClosePolicy.RequestExplicitExit();
            Window.Close();
        }

        private async void OnLightingTestRequested(object? sender, EventArgs args)
        {
            if (!Window.CanStartLightingTest)
            {
                Window.ShowSettingsCommand.Execute(null);
                return;
            }
            if (_lightingTestWindow is { } existing)
            {
                if (existing.IsVisible)
                {
                    existing.Activate();
                }
                return;
            }

            ThermalProfile profile;
            try
            {
                profile = _settingsViewModel.ProfileEditor.BuildProfile(_settingsViewModel.SmoothingSeconds);
            }
            catch (ArgumentException)
            {
                ShowForegroundError(_localization.Get("Validation.Profile"));
                return;
            }

            var viewModel = new LightingTestViewModel(
                _runtime,
                profile,
                SaveLightingTestProfileAsync,
                _localization,
                new ColorPickerService());
            var window = new LightingTestWindow(viewModel) { Owner = Window };
            _lightingTestViewModel = viewModel;
            _lightingTestWindow = window;
            try
            {
                await viewModel.OpenAsync();
                if (ReferenceEquals(_lightingTestWindow, window) && !_isDisposing)
                {
                    window.ShowDialog();
                }
            }
            catch (Exception)
            {
                try
                {
                    await viewModel.CloseAsync();
                }
                catch (Exception)
                {
                }

                window.CloseAfterCleanup();
                if (!_isDisposing)
                {
                    ShowForegroundError(_localization.Get("TestWindow.OpenFailed"));
                }
            }
            finally
            {
                if (ReferenceEquals(_lightingTestWindow, window))
                {
                    _lightingTestWindow = null;
                    _lightingTestViewModel = null;
                }
            }
        }

        private Task SaveLightingTestProfileAsync(ThermalProfile profile, CancellationToken cancellationToken) =>
            _settingsViewModel.ApplyLightingTestProfileAsync(profile, cancellationToken);

    }

    private sealed class WpfTraySession : IAppTraySession
    {
        private readonly TrayIconService _service;
        private readonly ILocalizationService _localization;
        public WpfTraySession(WpfUiSession ui, IThermalRuntime runtime, System.Windows.Application application, ILocalizationService localization, Action<Exception> failure)
        {
            _localization = localization;
            var window = new WpfTrayWindow(ui.Window);
            var trayApplication = new WpfTrayApplication(application);
            Func<AppSettings> currentSettings = () => ui.Settings.LiveSettings;
            Func<Task> toggleMode = () => runtime.SetModeEnabledAsync(!runtime.CurrentSettings.IsModeEnabled, CancellationToken.None);
            Func<bool> notificationsEnabled = () => ui.Settings.LiveSettings.NotificationsEnabled;
            var ownedPlatform = new NotifyIconTrayPlatform();

            _service = new TrayIconService(
                ownedPlatform,
                window,
                trayApplication,
                runtime,
                localization,
                currentSettings,
                ui.Settings,
                toggleMode,
                ui.ClosePolicy,
                notificationsEnabled,
                failure);
        }
        public void ShowRecoveryWarning(string message) => _service.ShowRecoveryWarning(message);
        public void ShowBackgroundError(string message) => _service.ShowNotification(
            $"{_localization.Get("App.Name")} · {_localization.Get("Notification.Error")}",
            message);
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
