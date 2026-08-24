using System.ComponentModel;
using System.Threading.Channels;
using LumaTherm.App.Localization;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Settings;

namespace LumaTherm.App.Services;

public sealed class TrayIconService : IAsyncDisposable
{
    public const int TooltipLimit = 63;
    private readonly object _sync = new();
    private readonly ITrayIconPlatform _platform;
    private readonly ITrayWindow _window;
    private readonly ITrayApplication _application;
    private readonly IThermalRuntime _runtime;
    private readonly ILocalizationService _localization;
    private readonly Func<AppSettings> _currentSettings;
    private readonly INotifyPropertyChanged _settingsChangeSource;
    private readonly Func<Task> _toggleModeAsync;
    private readonly WindowClosePolicy _closePolicy;
    private readonly Func<bool> _notificationsEnabled;
    private readonly Action<Exception>? _errorSink;
    private readonly Channel<Func<Task>> _operations;
    private readonly Task _operationConsumer;
    private RuntimeStatus _previousStatus;
    private int _exitRequested;
    private bool _recoveryWarningShown;
    private bool _platformDisposed;
    private bool _subscriptionsAttached;
    private bool _disposed;

    public TrayIconService(
        ITrayIconPlatform platform,
        ITrayWindow window,
        ITrayApplication application,
        IThermalRuntime runtime,
        ILocalizationService localization,
        Func<AppSettings> currentSettings,
        INotifyPropertyChanged settingsChangeSource,
        Func<Task> toggleModeAsync,
        WindowClosePolicy closePolicy,
        Func<bool> notificationsEnabled,
        Action<Exception>? errorSink = null)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _toggleModeAsync = toggleModeAsync ?? throw new ArgumentNullException(nameof(toggleModeAsync));
        _currentSettings = currentSettings ?? throw new ArgumentNullException(nameof(currentSettings));
        _settingsChangeSource = settingsChangeSource ?? throw new ArgumentNullException(nameof(settingsChangeSource));
        _closePolicy = closePolicy ?? throw new ArgumentNullException(nameof(closePolicy));
        _notificationsEnabled = notificationsEnabled ?? throw new ArgumentNullException(nameof(notificationsEnabled));
        _errorSink = errorSink;
        _operations = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        _operationConsumer = Task.CompletedTask;
        try
        {
            AttachSubscriptions();
            _previousStatus = _runtime.CurrentSnapshot.Status;
            ApplySnapshot(_runtime.CurrentSnapshot, notify: false);
            _platform.Visible = true;
            _operationConsumer = ConsumeOperationsAsync();
        }
        catch
        {
            RollbackInitialization();
            throw;
        }
    }

    public static TrayMenuState BuildMenuState(
        RuntimeSnapshot snapshot,
        AppSettings settings,
        ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(localization);
        var temperature = snapshot.Temperature is null ? "—" : $"{snapshot.Temperature.Celsius:0}°C";
        var tooltip = TruncateTooltip(
            $"{localization.Get("App.Name")} · {temperature} · {GetStatusLabel(snapshot.Status, localization)}",
            TooltipLimit);
        var entries = new List<TrayMenuEntry>();
        if (settings.TrayMenu.ShowTemperature)
        {
            entries.Add(new TrayMenuEntry(TrayCommandKind.Temperature, temperature, false));
        }
        if (settings.TrayMenu.ShowOpenCommand)
        {
            entries.Add(new TrayMenuEntry(TrayCommandKind.Open, localization.Get("Tray.Open"), true));
        }
        if (settings.TrayMenu.ShowModeToggle)
        {
            var toggleKey = snapshot.IsModeEnabled ? "Tray.DisableMode" : "Tray.EnableMode";
            entries.Add(new TrayMenuEntry(TrayCommandKind.ToggleMode, localization.Get(toggleKey), true));
        }
        entries.Add(new TrayMenuEntry(TrayCommandKind.Exit, localization.Get("Tray.Exit"), true));

        var device = snapshot.LightingDevice is null
            ? localization.Get("Runtime.LightingNotFound")
            : $"{snapshot.LightingDevice.Name} · {localization.Get(snapshot.LightingDevice.IsAvailable ? "Tray.Available" : "Tray.Unavailable")}";
        return new TrayMenuState(tooltip, device, entries);
    }

    public static string GetStatusLabel(RuntimeStatus status, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(localization);
        var key = status switch
        {
            RuntimeStatus.Disabled => "Tray.StatusDisabled",
            RuntimeStatus.Connecting => "Tray.StatusConnecting",
            RuntimeStatus.Active => "Tray.StatusActive",
            RuntimeStatus.HoldingLastColor => "Tray.StatusHolding",
            RuntimeStatus.SensorUnavailable => "Tray.StatusSensorUnavailable",
            RuntimeStatus.LightingUnavailable => "Tray.StatusLightingUnavailable",
            RuntimeStatus.Suspended => "Tray.StatusSuspended",
            RuntimeStatus.Faulted => "Tray.StatusFaulted",
            _ => "Tray.StatusUnknown",
        };
        return localization.Get(key);
    }

    public static string TruncateTooltip(string value, int maximumLength = TooltipLimit)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);
        if (value.Length <= maximumLength)
        {
            return value;
        }

        var length = maximumLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }

    public void ShowWindow()
    {
        lock (_sync)
        {
            if (_disposed || Volatile.Read(ref _exitRequested) != 0)
            {
                return;
            }
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }
        if (_window.IsMinimized)
        {
            _window.Restore();
        }
        _window.Activate();
        _window.BringToFront();
    }

    public void ShowNotification(string title, string message)
    {
        lock (_sync)
        {
            if (_disposed || _platformDisposed || !_notificationsEnabled())
            {
                return;
            }

            _platform.ShowNotification(title, message);
        }
    }

    public void ShowRecoveryWarning(string message)
    {
        lock (_sync)
        {
            if (_recoveryWarningShown || _disposed || _platformDisposed || !_notificationsEnabled())
            {
                return;
            }

            _recoveryWarningShown = true;
            _platform.ShowNotification($"{_localization.Get("App.Name")} · {_localization.Get("Notification.Recovery")}", message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DetachSubscriptions();
            _operations.Writer.TryComplete();
        }

        await _operationConsumer;
        DisposePlatform();
    }

    private void AttachSubscriptions()
    {
        _subscriptionsAttached = true;
        _platform.LeftClick += OnShowRequested;
        _platform.DoubleClick += OnShowRequested;
        _platform.CommandRequested += OnCommandRequested;
        _localization.LanguageChanged += OnLanguageChanged;
        _runtime.SnapshotChanged += OnSnapshotChanged;
        _settingsChangeSource.PropertyChanged += OnSettingsChanged;
    }

    private void DetachSubscriptions()
    {
        if (!_subscriptionsAttached)
        {
            return;
        }

        _subscriptionsAttached = false;
        _platform.LeftClick -= OnShowRequested;
        _platform.DoubleClick -= OnShowRequested;
        _platform.CommandRequested -= OnCommandRequested;
        _localization.LanguageChanged -= OnLanguageChanged;
        _runtime.SnapshotChanged -= OnSnapshotChanged;
        _settingsChangeSource.PropertyChanged -= OnSettingsChanged;
    }

    private void RollbackInitialization()
    {
        lock (_sync)
        {
            _disposed = true;
            DetachSubscriptions();
            _operations.Writer.TryComplete();
        }

        DisposePlatform();
    }

    private void OnShowRequested(object? sender, EventArgs args) => Enqueue(() =>
    {
        ShowWindow();
        return Task.CompletedTask;
    });

    private void OnCommandRequested(object? sender, TrayCommandKind kind)
    {
        switch (kind)
        {
            case TrayCommandKind.Open:
                OnShowRequested(sender, EventArgs.Empty);
                break;
            case TrayCommandKind.ToggleMode when Volatile.Read(ref _exitRequested) == 0:
                Enqueue(_toggleModeAsync);
                break;
            case TrayCommandKind.Exit when Interlocked.CompareExchange(ref _exitRequested, 1, 0) == 0:
                Enqueue(ExitAsync);
                break;
        }
    }

    private void OnSnapshotChanged(object? sender, RuntimeSnapshot snapshot) => ApplySnapshot(snapshot, notify: true);

    private void OnLanguageChanged(object? sender, EventArgs args) => ApplySnapshot(_runtime.CurrentSnapshot, notify: false);

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (null or "" or "CurrentSettings" or "LiveSettings"))
        {
            return;
        }

        var previousLanguage = _localization.CurrentLanguage;
        _localization.Apply(_currentSettings().Language);
        if (_localization.CurrentLanguage == previousLanguage)
        {
            ApplySnapshot(_runtime.CurrentSnapshot, notify: false);
        }
    }

    private void ApplySnapshot(RuntimeSnapshot snapshot, bool notify)
    {
        lock (_sync)
        {
            if (_disposed || _platformDisposed || Volatile.Read(ref _exitRequested) != 0)
            {
                return;
            }

            _platform.MenuState = BuildMenuState(snapshot, _currentSettings(), _localization);
            if (notify && ShouldNotify(snapshot))
            {
                _platform.ShowNotification(
                    $"{_localization.Get("App.Name")} · {_localization.Get("Notification.Warning")}",
                    NotificationMessage(snapshot.Status));
            }
            _previousStatus = snapshot.Status;
        }
    }

    private bool ShouldNotify(RuntimeSnapshot snapshot)
    {
        if (!_notificationsEnabled() || !snapshot.IsModeEnabled || snapshot.Status == _previousStatus)
        {
            return false;
        }

        return snapshot.Status is RuntimeStatus.SensorUnavailable or RuntimeStatus.LightingUnavailable or RuntimeStatus.Faulted;
    }

    private string NotificationMessage(RuntimeStatus status) => _localization.Get(status switch
    {
        RuntimeStatus.SensorUnavailable => "Notification.SensorUnavailable",
        RuntimeStatus.LightingUnavailable => "Notification.LightingUnavailable",
        _ => "Notification.BackgroundError",
    });

    private void Enqueue(Func<Task> operation)
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _operations.Writer.TryWrite(operation);
            }
        }
    }

    private async Task ConsumeOperationsAsync()
    {
        await foreach (var operation in _operations.Reader.ReadAllAsync())
        {
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }
    }

    private async Task ExitAsync()
    {
        _closePolicy.RequestExplicitExit();
        try
        {
            await _runtime.StopAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
        finally
        {
            try
            {
                DisposePlatform();
            }
            finally
            {
                try
                {
                    _application.RequestShutdown();
                }
                catch (Exception exception)
                {
                    ReportFailure(exception);
                }
            }
        }
    }

    private void DisposePlatform()
    {
        lock (_sync)
        {
            if (_platformDisposed)
            {
                return;
            }

            _platformDisposed = true;
        }

        try
        {
            _platform.Visible = false;
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }

        try
        {
            _platform.Dispose();
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private void ReportFailure(Exception exception)
    {
        try
        {
            _errorSink?.Invoke(exception);
        }
        catch
        {
        }
    }
}
