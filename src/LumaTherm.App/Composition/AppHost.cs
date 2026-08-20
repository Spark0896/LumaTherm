using LumaTherm.Core.Diagnostics;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;

namespace LumaTherm.App.Composition;

public sealed class AppHost : IAsyncDisposable
{
    private const string CrashRecoveryWarning = "Предыдущий сеанс завершился аварийно. Управление подсветкой выключено до ручного включения.";
    private readonly object _sync = new();
    private readonly AppServices _services;
    private readonly bool _autostart;
    private readonly string[] _unknownArguments;
    private readonly Action _requestExit;
    private IAppLogger? _logger;
    private IAppInstanceCoordinator? _singleInstance;
    private ISessionSentinel? _sentinel;
    private ISettingsStore? _settingsStore;
    private IThermalRuntime? _runtime;
    private IStartupService? _startup;
    private IAppUiSession? _ui;
    private IAppTraySession? _tray;
    private IAsyncDisposable? _power;
    private IAppDiscoverySession? _discovery;
    private Task<bool>? _startTask;
    private Task? _stopTask;
    private bool _sentinelBegun;
    private bool _cleanupCompleted;
    private string? _lastSensorSource;
    private string? _lastLampId;
    private RuntimeStatus? _lastRuntimeStatus;
    private bool? _lastModeEnabled;

    public AppHost(AppServices services, IEnumerable<string> arguments, Action requestExit)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        ArgumentNullException.ThrowIfNull(arguments);
        var parsedArguments = arguments.ToArray();
        _autostart = parsedArguments.Any(argument => string.Equals(argument, "--autostart", StringComparison.OrdinalIgnoreCase));
        _unknownArguments = parsedArguments.Where(argument => !string.Equals(argument, "--autostart", StringComparison.OrdinalIgnoreCase)).ToArray();
        _requestExit = requestExit ?? throw new ArgumentNullException(nameof(requestExit));
    }

    public IAppTraySession? Tray => _tray;
    public IAppUiSession? Ui => _ui;

    public Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_stopTask is not null) return Task.FromException<bool>(new InvalidOperationException("LumaTherm is stopping or stopped."));
            return _startTask ??= StartCoreAsync(cancellationToken);
        }
    }

    private async Task<bool> StartCoreAsync(CancellationToken cancellationToken)
    {
        _logger = _services.CreateLogger();
        for (var index = 0; index < _unknownArguments.Length; index++)
        {
            SafeLog(AppLogLevel.Warning, "app.argument_ignored", "Unsupported command-line argument was ignored.", data: new Dictionary<string, object?> { ["argumentIndex"] = index });
        }
        try
        {
            _singleInstance = _services.CreateSingleInstance(_logger);
            if (!await _singleInstance.TryAcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                await _singleInstance.SignalActivationAsync(cancellationToken).ConfigureAwait(false);
                await _singleInstance.DisposeAsync().ConfigureAwait(false);
                _singleInstance = null;
                DisposeLogger();
                lock (_sync) _cleanupCompleted = true;
                _requestExit();
                return false;
            }
        }
        catch (Exception ownershipFailure)
        {
            var cleanupFailures = new List<Exception>();
            if (_singleInstance is not null)
            {
                await AttemptAsync(() => _singleInstance.DisposeAsync().AsTask(), cleanupFailures).ConfigureAwait(false);
                _singleInstance = null;
            }
            Attempt(DisposeLogger, cleanupFailures);
            lock (_sync) _cleanupCompleted = true;
            if (cleanupFailures.Count == 0) throw;
            throw new AggregateException([ownershipFailure, .. cleanupFailures]);
        }

        _singleInstance.ActivationRequested += OnActivationRequested;
        try
        {
            _sentinel = _services.CreateSentinel();
            var priorCrash = await _sentinel.BeginAsync(cancellationToken).ConfigureAwait(false);
            _sentinelBegun = true;
            _settingsStore = _services.CreateSettingsStore();
            var load = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            var settings = load.Settings.Validate();
            _runtime = _services.CreateRuntime(settings, _settingsStore);
            _runtime.SnapshotChanged += OnRuntimeSnapshotChanged;
            CaptureRuntimeState(_runtime.CurrentSnapshot);
            if (priorCrash && settings.IsModeEnabled)
            {
                await _runtime.UpdateSettingsAsync(settings with { IsModeEnabled = false }, cancellationToken).ConfigureAwait(false);
                settings = _runtime.CurrentSettings;
            }

            _startup = _services.CreateStartupService(_runtime);
            _ui = _services.CreateUi(_runtime, _startup, settings);
            if (!_autostart) _ui.Show();
            _tray = _services.CreateTray(_ui, _runtime);
            _power = _services.CreatePower(_runtime);
            var warning = priorCrash ? CrashRecoveryWarning : load.RecoveryMessage;
            if (!string.IsNullOrWhiteSpace(warning)) _tray.ShowRecoveryWarning(warning);
            _discovery = _services.CreateDiscovery(_runtime, _ui);
            await _discovery.StartAsync(cancellationToken).ConfigureAwait(false);
            await _runtime.StartAsync(cancellationToken).ConfigureAwait(false);
            SafeLog(AppLogLevel.Information, "app.start", "LumaTherm started.", data: new Dictionary<string, object?>
            {
                ["autostart"] = _autostart,
                ["modeEnabled"] = _runtime.CurrentSettings.IsModeEnabled,
            });
            return true;
        }
        catch (Exception startupFailure)
        {
            SafeLog(AppLogLevel.Error, "app.start", "LumaTherm startup failed.", startupFailure);
            var cleanupFailures = await CleanupAsync(removeSentinel: false).ConfigureAwait(false);
            lock (_sync)
            {
                _cleanupCompleted = true;
                _stopTask ??= Task.CompletedTask;
            }
            if (cleanupFailures.Count == 0) throw;
            throw new AggregateException([startupFailure, .. cleanupFailures]);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return _stopTask ??= StopCoreAsync();
        }
    }

    public void LogUnhandled(Exception exception, bool foreground, bool notify = true)
    {
        ArgumentNullException.ThrowIfNull(exception);
        SafeLog(AppLogLevel.Error, "app.unhandled", "Unhandled application exception.", exception);
        if (!notify) return;
        if (foreground) _ui?.ShowForegroundError("Произошла непредвиденная ошибка. LumaTherm безопасно завершит работу.");
        else _tray?.ShowBackgroundError("Произошла фоновая ошибка. Управление подсветкой будет безопасно остановлено.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task StopCoreAsync()
    {
        Task<bool>? startTask;
        lock (_sync) startTask = _startTask;
        if (startTask is not null)
        {
            try { await startTask.ConfigureAwait(false); }
            catch { }
        }
        lock (_sync)
        {
            if (_cleanupCompleted) return;
        }
        var failures = await CleanupAsync(removeSentinel: true).ConfigureAwait(false);
        lock (_sync) _cleanupCompleted = true;
        if (failures.Count == 1) throw failures[0];
        if (failures.Count > 1) throw new AggregateException(failures);
    }

    private async Task<List<Exception>> CleanupAsync(bool removeSentinel)
    {
        var failures = new List<Exception>();
        await AttemptAsync(() => _runtime?.StopAsync(CancellationToken.None) ?? Task.CompletedTask, failures).ConfigureAwait(false);
        if (_runtime is not null) _runtime.SnapshotChanged -= OnRuntimeSnapshotChanged;
        await AttemptAsync(() => _discovery?.DisposeAsync().AsTask() ?? Task.CompletedTask, failures).ConfigureAwait(false);
        await AttemptAsync(() => _power?.DisposeAsync().AsTask() ?? Task.CompletedTask, failures).ConfigureAwait(false);
        await AttemptAsync(() => _tray?.DisposeAsync().AsTask() ?? Task.CompletedTask, failures).ConfigureAwait(false);
        Attempt(() => _ui?.Dispose(), failures);
        if (_singleInstance is not null)
        {
            _singleInstance.ActivationRequested -= OnActivationRequested;
            await AttemptAsync(() => _singleInstance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        SafeLog(failures.Count == 0 ? AppLogLevel.Information : AppLogLevel.Error, "app.stop", failures.Count == 0 ? "LumaTherm stopped." : "LumaTherm stop was incomplete.");
        Attempt(DisposeLogger, failures);
        if (removeSentinel && failures.Count == 0 && _sentinelBegun && _sentinel is not null)
        {
            await AttemptAsync(() => _sentinel.CompleteAsync(CancellationToken.None), failures).ConfigureAwait(false);
        }
        return failures;
    }

    private void OnActivationRequested(object? sender, EventArgs args) => _services.DispatchToUi(() => _ui?.ShowRestoreActivate());

    private void OnRuntimeSnapshotChanged(object? sender, RuntimeSnapshot snapshot)
    {
        string? sensorEvent = null;
        string? lampEvent = null;
        string? runtimeEvent = null;
        lock (_sync)
        {
            if (snapshot.Temperature is { } temperature && !string.Equals(_lastSensorSource, temperature.SourceName, StringComparison.Ordinal))
            {
                _lastSensorSource = temperature.SourceName;
                sensorEvent = "sensor.selected";
            }
            else if (snapshot.Status == RuntimeStatus.SensorUnavailable && _lastRuntimeStatus != RuntimeStatus.SensorUnavailable)
            {
                sensorEvent = "sensor.failed";
            }

            var lampId = snapshot.LightingDevice?.Id;
            if (!string.Equals(_lastLampId, lampId, StringComparison.Ordinal))
            {
                lampEvent = lampId is null ? "lamp.disconnected" : "lamp.connected";
                _lastLampId = lampId;
            }

            if (_lastModeEnabled != snapshot.IsModeEnabled)
            {
                runtimeEvent = snapshot.IsModeEnabled ? "runtime.enabled" : "runtime.disabled";
                _lastModeEnabled = snapshot.IsModeEnabled;
            }
            _lastRuntimeStatus = snapshot.Status;
        }

        if (sensorEvent is not null) SafeLog(sensorEvent == "sensor.failed" ? AppLogLevel.Warning : AppLogLevel.Information, sensorEvent, snapshot.Message ?? snapshot.Temperature?.SourceName ?? "Sensor state changed.");
        if (lampEvent is not null) SafeLog(AppLogLevel.Information, lampEvent, snapshot.LightingDevice?.Name ?? "LampArray released or unavailable.");
        if (runtimeEvent is not null) SafeLog(AppLogLevel.Information, runtimeEvent, "Runtime mode changed.");
    }

    private void CaptureRuntimeState(RuntimeSnapshot snapshot)
    {
        _lastSensorSource = snapshot.Temperature?.SourceName;
        _lastLampId = snapshot.LightingDevice?.Id;
        _lastRuntimeStatus = snapshot.Status;
        _lastModeEnabled = snapshot.IsModeEnabled;
    }

    private void SafeLog(AppLogLevel level, string eventName, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null)
    {
        try { _logger?.Write(level, eventName, message, exception, data); }
        catch { }
    }

    private void DisposeLogger()
    {
        if (_logger is IDisposable disposable) disposable.Dispose();
        _logger = null;
    }

    private static async Task AttemptAsync(Func<Task> action, List<Exception> failures)
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception exception) { failures.Add(exception); }
    }

    private static void Attempt(Action action, List<Exception> failures)
    {
        try { action(); }
        catch (Exception exception) { failures.Add(exception); }
    }
}
