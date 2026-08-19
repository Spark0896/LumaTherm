using System.Collections.Concurrent;
using LumaTherm.Core.Colors;
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;

namespace LumaTherm.Infrastructure.Lighting;

public sealed class WindowsLampArrayPlatform : ILampArrayPlatform
{
    private readonly ConcurrentDictionary<string, bool> _availability = new(StringComparer.Ordinal);
    private readonly DeviceWatcher _watcher;
    private readonly object _lifetimeLock = new();
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
        var deviceInformation = await DeviceInformation
            .FindAllAsync(LampArray.GetDeviceSelector())
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        var handles = new List<ILampArrayHandle>(deviceInformation.Count);

        foreach (var device in deviceInformation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _availability[device.Id] = device.IsEnabled;
            var lampArray = await LampArray.FromIdAsync(device.Id)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            if (lampArray is not null)
            {
                handles.Add(new WindowsLampArrayHandle(
                    lampArray,
                    device.Name,
                    () => _availability.GetValueOrDefault(device.Id)));
            }
        }

        return handles;
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifetimeLock)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _watcher.Added -= OnDeviceAdded;
            _watcher.Removed -= OnDeviceRemoved;
            _watcher.Updated -= OnDeviceUpdated;
            if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                _watcher.Stop();
            }
        }

        return ValueTask.CompletedTask;
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation device)
    {
        _availability[device.Id] = device.IsEnabled;
        RaiseDevicesChanged();
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate device)
    {
        _availability[device.Id] = false;
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
        Func<bool> getAvailability) : ILampArrayHandle
    {
        public string Id => lampArray.DeviceId;
        public string Name { get; } = name;
        public int LampCount => lampArray.LampCount;
        public bool IsAvailable => getAvailability();

        public void Enable() => lampArray.IsEnabled = true;

        public void SetColor(RgbColor color) =>
            lampArray.SetColor(Windows.UI.Color.FromArgb(255, color.R, color.G, color.B));

        public void Disable() => lampArray.IsEnabled = false;
    }
}
