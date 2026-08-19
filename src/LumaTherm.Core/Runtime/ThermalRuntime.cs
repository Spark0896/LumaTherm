using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Sensors;
using LumaTherm.Core.Settings;

namespace LumaTherm.Core.Runtime;

public sealed class ThermalRuntime : IThermalRuntime
{
    private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SensorInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MissingHoldDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan[] MissingRetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(15),
    ];

    private readonly ITemperatureProvider _temperatureProvider;
    private readonly ILightingController _lightingController;
    private readonly ISettingsStore _settingsStore;
    private readonly TimeProvider _timeProvider;
    private readonly LightingCommandGate _lightingGate = new(RenderInterval);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _processGate = new(1, 1);
    private readonly AsyncLocal<bool> _insideBackgroundLoop = new();

    private ColorEngine _colorEngine;
    private AppSettings _settings;
    private RuntimeSnapshot _currentSnapshot;
    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private DateTimeOffset? _lastSensorPollAt;
    private DateTimeOffset? _lastRenderAt;
    private DateTimeOffset? _firstMissingAt;
    private DateTimeOffset? _nextSensorAttemptAt;
    private TemperatureReading? _targetReading;
    private ThermalRange? _targetRange;
    private RgbColor? _displayedColor;
    private int _missingRetryIndex;
    private bool _releasedForMissing;
    private bool _releasePending;
    private bool _suspended;
    private bool _stopped;
    private bool _sourcesDisposed;

    public ThermalRuntime(
        ITemperatureProvider temperatureProvider,
        ColorEngine colorEngine,
        ILightingController lightingController,
        AppSettings settings,
        ISettingsStore settingsStore,
        TimeProvider timeProvider)
    {
        _temperatureProvider = temperatureProvider ?? throw new ArgumentNullException(nameof(temperatureProvider));
        _colorEngine = colorEngine ?? throw new ArgumentNullException(nameof(colorEngine));
        _lightingController = lightingController ?? throw new ArgumentNullException(nameof(lightingController));
        _settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Validate();
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _currentSnapshot = CreateSnapshot(
            _settings.IsModeEnabled ? RuntimeStatus.Connecting : RuntimeStatus.Disabled,
            null,
            null,
            null,
            null);
    }

    public event EventHandler<RuntimeSnapshot>? SnapshotChanged;

    public RuntimeSnapshot CurrentSnapshot => Volatile.Read(ref _currentSnapshot);
    public AppSettings CurrentSettings => Volatile.Read(ref _settings);

    public async Task<RuntimeSnapshot> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        RuntimeSnapshot snapshot;
        await _processGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            snapshot = await ProcessOnceCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _processGate.Release();
        }

        Publish(snapshot);
        return snapshot;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        RuntimeSnapshot? snapshot = null;
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfStopped();
            if (_suspended)
            {
                snapshot = CreateSnapshot(RuntimeStatus.Suspended, _targetReading, _displayedColor, _targetRange, null);
            }
            else if (!_settings.IsModeEnabled)
            {
                snapshot = CreateSnapshot(RuntimeStatus.Disabled, null, null, null, null);
            }
            else if (_loopTask is null)
            {
                StartLoopNoLock();
                snapshot = CreateSnapshot(RuntimeStatus.Connecting, _targetReading, _displayedColor, _targetRange, null);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (snapshot is not null)
        {
            Publish(snapshot);
        }
    }

    public async Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        RuntimeSnapshot? snapshot = null;
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfStopped();
            var modeChanged = _settings.IsModeEnabled != enabled;
            if (!modeChanged && (enabled || !_releasePending))
            {
                return;
            }

            if (modeChanged)
            {
                var updated = (_settings with { IsModeEnabled = enabled }).Validate();
                await _settingsStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _settings, updated);
                if (!enabled)
                {
                    _releasePending = true;
                }
            }

            try
            {
                if (!enabled)
                {
                    snapshot = await DisableCommittedModeAsync().ConfigureAwait(false);
                }
                else if (_suspended)
                {
                    snapshot = CreateSnapshot(RuntimeStatus.Suspended, _targetReading, _displayedColor, _targetRange, null);
                }
                else
                {
                    StartLoopNoLock();
                    snapshot = CreateSnapshot(RuntimeStatus.Connecting, _targetReading, _displayedColor, _targetRange, null);
                }
            }
            catch (Exception exception)
            {
                snapshot = CreateFaultSnapshot(exception);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (snapshot is not null)
        {
            Publish(snapshot);
        }
    }

    public async Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var validated = settings.Validate();
        RuntimeSnapshot snapshot;
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfStopped();
            var previousSettings = _settings;
            var settingsChanged = validated != previousSettings;
            if (!settingsChanged && !_releasePending)
            {
                return;
            }

            var modeChanged = validated.IsModeEnabled != previousSettings.IsModeEnabled;
            if (settingsChanged)
            {
                await _settingsStore.SaveAsync(validated, cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _settings, validated);
                if (modeChanged && !validated.IsModeEnabled)
                {
                    _releasePending = true;
                }
            }

            try
            {
                var profileChanged = validated.Profile != previousSettings.Profile;
                if (!validated.IsModeEnabled && (modeChanged || _releasePending))
                {
                    snapshot = await DisableCommittedModeAsync(profileChanged ? validated.Profile : null).ConfigureAwait(false);
                }
                else
                {
                    await _processGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        if (profileChanged)
                        {
                            _colorEngine.UpdateProfile(validated.Profile);
                            _targetRange = _targetReading is null ? null : _colorEngine.Classify(_targetReading.Celsius);
                        }

                        if (modeChanged && !_suspended)
                        {
                            StartLoopNoLock();
                            snapshot = CreateSnapshot(RuntimeStatus.Connecting, _targetReading, _displayedColor, _targetRange, null);
                        }
                        else
                        {
                            snapshot = CreateSnapshot(StatusForCurrentState(), _targetReading, _displayedColor, _targetRange, null);
                        }
                    }
                    finally
                    {
                        _processGate.Release();
                    }
                }
            }
            catch (Exception exception)
            {
                snapshot = CreateFaultSnapshot(exception);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }

        Publish(snapshot);
    }

    public async Task SuspendAsync(CancellationToken cancellationToken)
    {
        RuntimeSnapshot? snapshot = null;
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfStopped();
            if (_suspended && !_releasePending)
            {
                return;
            }

            if (!_suspended)
            {
                _suspended = true;
                await StopLoopNoLockAsync().ConfigureAwait(false);
            }

            await _processGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                var releaseMessage = await TryReleaseLightingAsync().ConfigureAwait(false);
                snapshot = CreateSnapshot(RuntimeStatus.Suspended, _targetReading, null, _targetRange, releaseMessage);
            }
            finally
            {
                _processGate.Release();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (snapshot is not null)
        {
            Publish(snapshot);
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        RuntimeSnapshot? snapshot = null;
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfStopped();
            if (!_suspended)
            {
                return;
            }

            _suspended = false;
            if (_settings.IsModeEnabled)
            {
                _lightingGate.Reset();
                StartLoopNoLock();
                snapshot = CreateSnapshot(RuntimeStatus.Connecting, _targetReading, _displayedColor, _targetRange, null);
            }
            else
            {
                snapshot = CreateSnapshot(RuntimeStatus.Disabled, null, null, null, null);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (snapshot is not null)
        {
            Publish(snapshot);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        RuntimeSnapshot? snapshot = null;
        List<Exception>? failures = null;
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            await StopLoopNoLockAsync().ConfigureAwait(false);
            await _processGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                try
                {
                    await ReleaseLightingAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    (failures ??= []).Add(exception);
                }

                if (!_sourcesDisposed)
                {
                    _sourcesDisposed = true;
                    try
                    {
                        await _temperatureProvider.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        (failures ??= []).Add(exception);
                    }

                    try
                    {
                        await _lightingController.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        (failures ??= []).Add(exception);
                    }
                }

                snapshot = CreateSnapshot(RuntimeStatus.Disabled, null, null, null, null);
            }
            finally
            {
                _processGate.Release();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (snapshot is not null)
        {
            Publish(snapshot);
        }

        if (failures is { Count: 1 })
        {
            throw failures[0];
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(failures);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<RuntimeSnapshot> ProcessOnceCoreAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (_stopped || !_settings.IsModeEnabled)
        {
            return CreateSnapshot(RuntimeStatus.Disabled, null, null, null, null);
        }

        if (_suspended)
        {
            return CreateSnapshot(RuntimeStatus.Suspended, _targetReading, _displayedColor, _targetRange, null);
        }

        var renderElapsed = _lastRenderAt is { } lastRender ? now - lastRender : RenderInterval;
        if (renderElapsed < TimeSpan.Zero)
        {
            renderElapsed = TimeSpan.Zero;
        }

        _lastRenderAt = now;
        string? sensorMessage = null;
        if (ShouldPollSensor(now))
        {
            _lastSensorPollAt = now;
            TemperatureReading? reading;
            try
            {
                reading = await _temperatureProvider.TryReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                reading = null;
                sensorMessage = exception.Message;
            }

            if (reading is null)
            {
                RegisterMissingReading(now);
            }
            else
            {
                RegisterValidReading(reading);
            }
        }

        if (_firstMissingAt is { } missingSince)
        {
            if (now - missingSince >= MissingHoldDuration)
            {
                string? releaseMessage = null;
                if (!_releasedForMissing)
                {
                    _releasedForMissing = true;
                    try
                    {
                        await ReleaseLightingAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        releaseMessage = exception.Message;
                    }
                }

                return CreateSnapshot(
                    RuntimeStatus.SensorUnavailable,
                    _targetReading,
                    null,
                    _targetRange,
                    releaseMessage ?? sensorMessage ?? "Temperature sources are unavailable.");
            }

            if (_displayedColor is not null)
            {
                return CreateSnapshot(RuntimeStatus.HoldingLastColor, _targetReading, _displayedColor, _targetRange, sensorMessage);
            }

            return CreateSnapshot(RuntimeStatus.SensorUnavailable, null, null, null, sensorMessage ?? "Temperature sources are unavailable.");
        }

        if (_targetReading is null)
        {
            return CreateSnapshot(RuntimeStatus.SensorUnavailable, null, null, null, sensorMessage ?? "Temperature sources are unavailable.");
        }

        var nextColor = _colorEngine.Step(_targetReading.Celsius, renderElapsed);
        try
        {
            if (!_lightingController.IsConnected)
            {
                if (!await _lightingController.ConnectAsync(_settings.PreferredLightingDeviceId, cancellationToken).ConfigureAwait(false))
                {
                    return CreateSnapshot(RuntimeStatus.LightingUnavailable, _targetReading, _displayedColor, _targetRange, "No lighting device is available.");
                }

                _lightingGate.Reset();
            }

            if (_lightingGate.ShouldSend(nextColor, now))
            {
                try
                {
                    await _lightingController.SetColorAsync(nextColor, cancellationToken).ConfigureAwait(false);
                    _displayedColor = nextColor;
                }
                catch
                {
                    _lightingGate.Reset();
                    throw;
                }
            }

            _releasePending = false;
            return CreateSnapshot(RuntimeStatus.Active, _targetReading, _displayedColor, _targetRange, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _lightingGate.Reset();
            return CreateSnapshot(RuntimeStatus.LightingUnavailable, _targetReading, _displayedColor, _targetRange, exception.Message);
        }
    }

    private bool ShouldPollSensor(DateTimeOffset now)
    {
        if (_lastSensorPollAt is null)
        {
            return true;
        }

        if (_firstMissingAt is not null)
        {
            return _nextSensorAttemptAt is { } next && now >= next;
        }

        return now - _lastSensorPollAt >= SensorInterval;
    }

    private void RegisterMissingReading(DateTimeOffset now)
    {
        _firstMissingAt ??= now;
        var delayIndex = Math.Min(_missingRetryIndex, MissingRetryDelays.Length - 1);
        _nextSensorAttemptAt = now + MissingRetryDelays[delayIndex];
        if (_missingRetryIndex < MissingRetryDelays.Length - 1)
        {
            _missingRetryIndex++;
        }
    }

    private void RegisterValidReading(TemperatureReading reading)
    {
        _targetReading = reading;
        _targetRange = _colorEngine.Classify(reading.Celsius);
        _firstMissingAt = null;
        _nextSensorAttemptAt = null;
        _missingRetryIndex = 0;
        _releasedForMissing = false;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(RenderInterval, _timeProvider);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                _insideBackgroundLoop.Value = true;
                try
                {
                    try
                    {
                        await ProcessOnceAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        Publish(CreateSnapshot(RuntimeStatus.Faulted, _targetReading, _displayedColor, _targetRange, exception.Message));
                    }
                }
                finally
                {
                    _insideBackgroundLoop.Value = false;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void StartLoopNoLock()
    {
        if (_loopTask is not null)
        {
            return;
        }

        _loopCancellation = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_loopCancellation.Token);
    }

    private async Task StopLoopNoLockAsync()
    {
        var cancellation = _loopCancellation;
        var loop = _loopTask;
        _loopCancellation = null;
        _loopTask = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        if (loop is not null && _insideBackgroundLoop.Value)
        {
            _ = DisposeCancellationWhenLoopCompletesAsync(loop, cancellation);
            return;
        }

        try
        {
            if (loop is not null)
            {
                await loop.ConfigureAwait(false);
            }
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task<RuntimeSnapshot> DisableCommittedModeAsync(ThermalProfile? updatedProfile = null)
    {
        Exception? loopStopFailure = null;
        try
        {
            await StopLoopNoLockAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            loopStopFailure = exception;
        }

        await _processGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            var releaseMessage = await TryReleaseLightingAsync().ConfigureAwait(false);
            ResetMonitoringState();
            if (updatedProfile is not null)
            {
                _colorEngine.UpdateProfile(updatedProfile);
            }

            return loopStopFailure is null
                ? CreateSnapshot(RuntimeStatus.Disabled, null, null, null, releaseMessage)
                : CreateFaultSnapshot(loopStopFailure, releaseMessage);
        }
        finally
        {
            _processGate.Release();
        }
    }

    private static async Task DisposeCancellationWhenLoopCompletesAsync(Task loop, CancellationTokenSource cancellation)
    {
        try
        {
            await loop.ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task ReleaseLightingAsync(CancellationToken cancellationToken)
    {
        await _lightingController.ReleaseAsync(cancellationToken).ConfigureAwait(false);
        _lightingGate.Reset();
        _displayedColor = null;
    }

    private async Task<string?> TryReleaseLightingAsync()
    {
        try
        {
            await ReleaseLightingAsync(CancellationToken.None).ConfigureAwait(false);
            _releasePending = false;
            return null;
        }
        catch (Exception exception)
        {
            _releasePending = true;
            return exception.Message;
        }
    }

    private void ResetMonitoringState()
    {
        _lastSensorPollAt = null;
        _lastRenderAt = null;
        _firstMissingAt = null;
        _nextSensorAttemptAt = null;
        _targetReading = null;
        _targetRange = null;
        _displayedColor = null;
        _lightingGate.Reset();
        _missingRetryIndex = 0;
        _releasedForMissing = false;
    }

    private RuntimeStatus StatusForCurrentState()
    {
        if (_suspended)
        {
            return RuntimeStatus.Suspended;
        }

        if (!_settings.IsModeEnabled || _stopped)
        {
            return RuntimeStatus.Disabled;
        }

        return CurrentSnapshot.Status;
    }

    private RuntimeSnapshot CreateSnapshot(
        RuntimeStatus status,
        TemperatureReading? temperature,
        RgbColor? color,
        ThermalRange? range,
        string? message)
    {
        return new RuntimeSnapshot(
            status,
            temperature,
            color,
            range,
            _lightingController.ConnectedDevice,
            message,
            _timeProvider.GetUtcNow(),
            _settings.IsModeEnabled);
    }

    private RuntimeSnapshot CreateFaultSnapshot(Exception exception, string? secondaryMessage = null)
    {
        var previous = CurrentSnapshot;
        DateTimeOffset timestamp;
        try { timestamp = _timeProvider.GetUtcNow(); }
        catch (Exception) { timestamp = previous.Timestamp; }
        LightingDeviceInfo? device;
        try { device = _lightingController.ConnectedDevice; }
        catch (Exception) { device = previous.LightingDevice; }
        var message = secondaryMessage is null ? exception.Message : $"{exception.Message} {secondaryMessage}";
        return new RuntimeSnapshot(RuntimeStatus.Faulted, _targetReading, _displayedColor, _targetRange, device, message, timestamp, CurrentSettings.IsModeEnabled);
    }

    private void Publish(RuntimeSnapshot snapshot)
    {
        Volatile.Write(ref _currentSnapshot, snapshot);
        var handlers = SnapshotChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<RuntimeSnapshot> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, snapshot);
            }
            catch (Exception)
            {
            }
        }
    }

    private void ThrowIfStopped()
    {
        if (_stopped)
        {
            throw new ObjectDisposedException(nameof(ThermalRuntime));
        }
    }
}
