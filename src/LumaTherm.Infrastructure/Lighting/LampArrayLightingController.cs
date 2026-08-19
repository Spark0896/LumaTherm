using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;

namespace LumaTherm.Infrastructure.Lighting;

public sealed class LampArrayLightingController : ILightingController
{
    private readonly ILampArrayPlatform _platform;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _stateLock = new();
    private ILampArrayHandle? _connectedHandle;
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
                return _connectedHandle is { IsAvailable: true };
            }
        }
    }

    public LightingDeviceInfo? ConnectedDevice
    {
        get
        {
            lock (_stateLock)
            {
                return _connectedHandle is { IsAvailable: true } handle ? ToDeviceInfo(handle) : null;
            }
        }
    }

    public event EventHandler? DevicesChanged;

    public async Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var handles = await _platform.FindAllAsync(cancellationToken).ConfigureAwait(false);
        return handles.Where(handle => handle.IsAvailable).Select(ToDeviceInfo).ToArray();
    }

    public async Task<bool> ConnectAsync(string? preferredDeviceId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var handles = await _platform.FindAllAsync(cancellationToken).ConfigureAwait(false);
            var available = handles.Where(handle => handle.IsAvailable).ToArray();
            var selected = SelectHandle(available, preferredDeviceId);

            lock (_stateLock)
            {
                DisconnectCurrentHandle();
                if (selected is null)
                {
                    return false;
                }

                selected.Enable();
                _connectedHandle = selected;
                return true;
            }
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
                if (_connectedHandle is not { IsAvailable: true } handle)
                {
                    DisconnectCurrentHandle();
                    throw new InvalidOperationException("No LampArray device is connected.");
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
            lock (_stateLock)
            {
                DisconnectCurrentHandle();
            }

            await _platform.DisposeAsync().ConfigureAwait(false);
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

        return available.FirstOrDefault(
                   handle => string.Equals(handle.Name, "GIGABYTE Device", StringComparison.Ordinal))
               ?? available.FirstOrDefault(
                   handle => handle.Id.Contains("VID_048D", StringComparison.OrdinalIgnoreCase)
                             && handle.Id.Contains("PID_5702", StringComparison.OrdinalIgnoreCase))
               ?? available.FirstOrDefault();
    }

    private static LightingDeviceInfo ToDeviceInfo(ILampArrayHandle handle) =>
        new(handle.Id, handle.Name, handle.LampCount, handle.IsAvailable);

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
            if (_connectedHandle is { IsAvailable: false })
            {
                DisconnectCurrentHandle();
            }
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
