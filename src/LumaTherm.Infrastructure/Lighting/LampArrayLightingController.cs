using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using System.Runtime.ExceptionServices;

namespace LumaTherm.Infrastructure.Lighting;

public sealed class LampArrayLightingController : ILightingController
{
    private readonly ILampArrayPlatform _platform;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _stateLock = new();
    private ILampArrayHandle? _connectedHandle;
    private long _deviceGeneration;
    private bool _disposed;

    public LampArrayLightingController(ILampArrayPlatform platform)
    {
        _platform = platform;
        _platform.DevicesChanged += OnDevicesChanged;
    }

    public bool IsConnected
    {
        get
        {
            lock (_stateLock)
            {
                return _connectedHandle is { IsPresent: true };
            }
        }
    }

    public LightingDeviceInfo? ConnectedDevice
    {
        get
        {
            lock (_stateLock)
            {
                return _connectedHandle is { IsPresent: true } handle ? ToDeviceInfo(handle) : null;
            }
        }
    }

    public event EventHandler? DevicesChanged;

    public async Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var handles = await _platform.FindAllAsync(cancellationToken).ConfigureAwait(false);
            ThrowIfDisposed();
            return handles.Select(ToDeviceInfo).ToArray();
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task<bool> ConnectAsync(string? preferredDeviceId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            long generation;
            lock (_stateLock)
            {
                generation = _deviceGeneration;
            }

            var handles = await _platform.FindAllAsync(cancellationToken).ConfigureAwait(false);
            var present = handles.Where(handle => handle.IsPresent).ToArray();
            var selected = SelectHandle(present, preferredDeviceId);

            lock (_stateLock)
            {
                DisconnectCurrentHandle();
            }

            if (selected is null)
            {
                return false;
            }

            selected.Enable();
            lock (_stateLock)
            {
                if (_deviceGeneration == generation && selected.IsPresent)
                {
                    _connectedHandle = selected;
                    return true;
                }
            }

            selected.Disable();
            return false;
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task SetColorAsync(RgbColor color, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            lock (_stateLock)
            {
                if (_connectedHandle is not { IsPresent: true } handle)
                {
                    DisconnectCurrentHandle();
                    throw new InvalidOperationException("No LampArray device is connected.");
                }

                if (!handle.IsAvailable)
                {
                    throw new LightingControlUnavailableException();
                }

                handle.SetColor(color);
            }
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                DisconnectCurrentHandle();
            }
        }
        finally
        {
            _operations.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _platform.DevicesChanged -= OnDevicesChanged;
            Exception? failure = null;
            lock (_stateLock)
            {
                try
                {
                    DisconnectCurrentHandle();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            try
            {
                await _platform.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }

            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        finally
        {
            _operations.Release();
        }
    }

    private static ILampArrayHandle? SelectHandle(
        IReadOnlyList<ILampArrayHandle> available,
        string? preferredDeviceId)
    {
        if (preferredDeviceId is not null)
        {
            var preferred = available.FirstOrDefault(
                handle => string.Equals(handle.Id, preferredDeviceId, StringComparison.Ordinal));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        var controllable = available.Where(handle => handle.IsAvailable).ToArray();
        var candidates = controllable.Length > 0 ? controllable : available;
        return candidates.FirstOrDefault(
                   handle => string.Equals(handle.Name, "GIGABYTE Device", StringComparison.Ordinal))
               ?? candidates.FirstOrDefault(
                   handle => handle.Id.Contains("VID_048D", StringComparison.OrdinalIgnoreCase)
                             && handle.Id.Contains("PID_5702", StringComparison.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault();
    }

    private static LightingDeviceInfo ToDeviceInfo(ILampArrayHandle handle) =>
        new(handle.Id, handle.Name, handle.LampCount, handle.IsAvailable, handle.IsPresent);

    private void DisconnectCurrentHandle()
    {
        var handle = _connectedHandle;
        _connectedHandle = null;
        handle?.Disable();
    }

    private void OnDevicesChanged(object? sender, EventArgs eventArgs)
    {
        lock (_stateLock)
        {
            _deviceGeneration++;
            if (_connectedHandle is { IsPresent: false })
            {
                DisconnectCurrentHandle();
            }
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
