using LumaTherm.Core.Colors;
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;

namespace LumaTherm.Infrastructure.Lighting;

public sealed class WindowsLampArrayPlatform : ILampArrayPlatform
{
    private readonly LampArrayAvailabilityState _availability = new();
    private readonly DeviceWatcher _watcher;
    private readonly object _lifetimeLock = new();
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly Dictionary<string, WindowsLampArrayHandle> _handles = new(StringComparer.Ordinal);
    private bool _disposed;

    public WindowsLampArrayPlatform()
    {
        _watcher = DeviceInformation.CreateWatcher(LampArray.GetDeviceSelector());
        _watcher.Added += OnDeviceAdded;
        _watcher.Removed += OnDeviceRemoved;
        _watcher.Updated += OnDeviceUpdated;
        _watcher.Start();
    }

    public event EventHandler? DevicesChanged;

    public async Task<IReadOnlyList<ILampArrayHandle>> FindAllAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var snapshotGeneration = _availability.CaptureGeneration();
            var deviceInformation = await DeviceInformation
                .FindAllAsync(LampArray.GetDeviceSelector())
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            ThrowIfDisposed();
            var handles = new List<ILampArrayHandle>(deviceInformation.Count);

            foreach (var device in deviceInformation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _availability.ApplySnapshot(device.Id, device.IsEnabled, snapshotGeneration);
                WindowsLampArrayHandle? existing;
                lock (_lifetimeLock) _handles.TryGetValue(device.Id, out existing);
                if (existing is not null)
                {
                    handles.Add(existing);
                    continue;
                }

                var lampArray = await LampArray.FromIdAsync(device.Id)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                ThrowIfDisposed();
                if (lampArray is not null)
                {
                    var handle = new WindowsLampArrayHandle(
                        lampArray,
                        device.Name,
                        () => _availability.IsAvailable(device.Id));
                    lock (_lifetimeLock)
                    {
                        if (_availability.CaptureGeneration() == snapshotGeneration && handle.IsPresent)
                            _handles[device.Id] = handle;
                    }
                    handles.Add(handle);
                }
            }

            return handles;
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
            lock (_lifetimeLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _watcher.Added -= OnDeviceAdded;
                _watcher.Removed -= OnDeviceRemoved;
                _watcher.Updated -= OnDeviceUpdated;
                _handles.Clear();
                if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                {
                    _watcher.Stop();
                }
            }
        }
        finally
        {
            _operations.Release();
        }
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation device)
    {
        _availability.RecordWatcherUpdate(device.Id, device.IsEnabled);
        RaiseDevicesChanged();
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate device)
    {
        _availability.RecordWatcherUpdate(device.Id, false);
        lock (_lifetimeLock) _handles.Remove(device.Id);
        RaiseDevicesChanged();
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate device) => RaiseDevicesChanged();

    private void RaiseDevicesChanged()
    {
        lock (_lifetimeLock)
        {
            if (_disposed)
            {
                return;
            }
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class WindowsLampArrayHandle(
        LampArray lampArray,
        string name,
        Func<bool> getPresence) : ILampArrayHandle
    {
        public string Id => lampArray.DeviceId;
        public string Name { get; } = name;
        public int LampCount => lampArray.LampCount;
        public bool IsPresent => getPresence();
        public bool IsAvailable => IsPresent && lampArray.IsAvailable;

        public void Enable() => lampArray.IsEnabled = true;

        public void SetColor(RgbColor color)
        {
            try { lampArray.SetColor(Windows.UI.Color.FromArgb(255, color.R, color.G, color.B)); }
            catch (Exception) when (IsPresent && !IsAvailable)
            {
                throw new LumaTherm.Core.Lighting.LightingControlUnavailableException();
            }
        }

        public void Disable() => lampArray.IsEnabled = false;
    }
}
