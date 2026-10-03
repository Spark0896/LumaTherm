using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Infrastructure.Lighting;

namespace LumaTherm.Infrastructure.Tests.Lighting;

public sealed class LampArrayLightingControllerTests
{
    [Fact]
    public async Task DiscoverAsync_ReturnsAvailableAndUnavailableDeviceInformation()
    {
        var platform = new InMemoryLampArrayPlatform(
            new InMemoryLampArrayHandle("device-1", "Other Device", 8, true),
            new InMemoryLampArrayHandle("device-2", "GIGABYTE Device", 16, true),
            new InMemoryLampArrayHandle("device-3", "Unavailable Device", 32, false));
        await using var controller = new LampArrayLightingController(platform);

        var devices = await controller.DiscoverAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new LightingDeviceInfo("device-1", "Other Device", 8, true),
                new LightingDeviceInfo("device-2", "GIGABYTE Device", 16, true),
                new LightingDeviceInfo("device-3", "Unavailable Device", 32, false),
            ],
            devices);
    }

    [Fact]
    public async Task DiscoverAsync_CompletesBeforeAConcurrentDisposalReleasesThePlatform()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var handle = new InMemoryLampArrayHandle("device-1", "Other Device", 8, true);
        var platform = new InMemoryLampArrayPlatform(handle);
        platform.BlockFindAll();
        var controller = new LampArrayLightingController(platform);
        var discovery = controller.DiscoverAsync(timeout.Token);
        Task disposal;
        try
        {
            await platform.FindAllStarted.WaitAsync(timeout.Token);
            disposal = controller.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            Assert.Equal(0, platform.DisposeCount);
        }
        finally
        {
            platform.ReleaseFindAll();
        }

        var devices = await discovery.WaitAsync(timeout.Token);
        await disposal.WaitAsync(timeout.Token);
        Assert.Equal([new LightingDeviceInfo("device-1", "Other Device", 8, true)], devices);
        Assert.Equal(1, platform.DisposeCount);
    }

    [Fact]
    public async Task FocusLoss_RetainsTheEnabledDeviceAndResumesWithoutReconnecting()
    {
        var handle = new InMemoryLampArrayHandle("selected", "GIGABYTE Device", 8, true);
        await using var controller = new LampArrayLightingController(new InMemoryLampArrayPlatform(handle));
        await controller.ConnectAsync(null, TestContext.Current.CancellationToken);

        handle.IsAvailable = false;
        Assert.True(controller.IsConnected);
        Assert.Equal("selected", controller.ConnectedDevice?.Id);
        Assert.False(controller.ConnectedDevice?.IsAvailable);
        Assert.Equal(0, handle.DisableCount);

        handle.IsAvailable = true;
        await controller.SetColorAsync(new RgbColor(10, 20, 30), TestContext.Current.CancellationToken);
        Assert.Equal(1, handle.EnableCount);
        Assert.Equal([new RgbColor(10, 20, 30)], handle.Colors);
    }

    [Fact]
    public async Task ConnectAsync_RegistersAnEnabledDeviceWhileWindowsControlsItsLighting()
    {
        var handle = new InMemoryLampArrayHandle("selected", "GIGABYTE Device", 8, false);
        await using var controller = new LampArrayLightingController(new InMemoryLampArrayPlatform(handle));

        Assert.True(await controller.ConnectAsync(null, TestContext.Current.CancellationToken));
        Assert.True(controller.IsConnected);
        Assert.False(controller.ConnectedDevice?.IsAvailable);
        Assert.Equal(1, handle.EnableCount);
        Assert.Equal(0, handle.DisableCount);
    }

    [Fact]
    public async Task ConnectAsync_PrefersTheSavedDeviceIdAndRoutesColorOnlyToIt()
    {
        var saved = new InMemoryLampArrayHandle("saved-device", "Other Device", 8, true);
        var gigabyte = new InMemoryLampArrayHandle("gigabyte-device", "GIGABYTE Device", 16, true);
        var platform = new InMemoryLampArrayPlatform(saved, gigabyte);
        await using var controller = new LampArrayLightingController(platform);

        var connected = await controller.ConnectAsync("saved-device", TestContext.Current.CancellationToken);
        await controller.SetColorAsync(new RgbColor(11, 22, 33), TestContext.Current.CancellationToken);

        Assert.True(connected);
        Assert.Equal(new LightingDeviceInfo("saved-device", "Other Device", 8, true), controller.ConnectedDevice);
        Assert.Equal(1, saved.EnableCount);
        Assert.Equal([new RgbColor(11, 22, 33)], saved.Colors);
        Assert.Empty(gigabyte.Colors);
    }

    [Fact]
    public async Task ConnectAsync_UsesGigabyteNameBeforeHidAndFallbackDevices()
    {
        var fallback = new InMemoryLampArrayHandle("fallback", "Other Device", 8, true);
        var hid = new InMemoryLampArrayHandle("HID#VID_048D&PID_5702", "HID Lamp", 8, true);
        var gigabyte = new InMemoryLampArrayHandle("named", "GIGABYTE Device", 8, true);
        var platform = new InMemoryLampArrayPlatform(fallback, hid, gigabyte);
        await using var controller = new LampArrayLightingController(platform);

        var connected = await controller.ConnectAsync(null, TestContext.Current.CancellationToken);

        Assert.True(connected);
        Assert.Equal("named", controller.ConnectedDevice?.Id);
    }

    [Fact]
    public async Task ConnectAsync_UsesGigabyteHidBeforeFirstAvailableDevice()
    {
        var fallback = new InMemoryLampArrayHandle("fallback", "Other Device", 8, true);
        var hid = new InMemoryLampArrayHandle("HID#VID_048D&MI_00#PID_5702", "HID Lamp", 8, true);
        var platform = new InMemoryLampArrayPlatform(fallback, hid);
        await using var controller = new LampArrayLightingController(platform);

        var connected = await controller.ConnectAsync(null, TestContext.Current.CancellationToken);

        Assert.True(connected);
        Assert.Equal("HID#VID_048D&MI_00#PID_5702", controller.ConnectedDevice?.Id);
    }

    [Fact]
    public async Task ConnectAsync_UsesFirstAvailableDeviceAsTheFinalFallback()
    {
        var unavailable = new InMemoryLampArrayHandle("unavailable", "Unavailable", 8, false);
        var firstAvailable = new InMemoryLampArrayHandle("first", "First", 8, true);
        var secondAvailable = new InMemoryLampArrayHandle("second", "Second", 8, true);
        var platform = new InMemoryLampArrayPlatform(unavailable, firstAvailable, secondAvailable);
        await using var controller = new LampArrayLightingController(platform);

        var connected = await controller.ConnectAsync(null, TestContext.Current.CancellationToken);

        Assert.True(connected);
        Assert.Equal("first", controller.ConnectedDevice?.Id);
    }

    [Fact]
    public async Task ConnectAsync_ReturnsFalseWhenNoDeviceIsPresent()
    {
        var platform = new InMemoryLampArrayPlatform(
            new InMemoryLampArrayHandle("unavailable", "Unavailable", 8, false) { IsPresent = false });
        await using var controller = new LampArrayLightingController(platform);

        var connected = await controller.ConnectAsync(null, TestContext.Current.CancellationToken);

        Assert.False(connected);
        Assert.False(controller.IsConnected);
        Assert.Null(controller.ConnectedDevice);
    }

    [Fact]
    public async Task ConnectAsync_RejectsADeviceThatIsRemovedWhileEnableIsBlocked()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var enableEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueEnable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = new InMemoryLampArrayHandle("selected", "GIGABYTE Device", 8, true)
        {
            OnEnable = () =>
            {
                enableEntered.TrySetResult();
                continueEnable.Task.WaitAsync(timeout.Token).GetAwaiter().GetResult();
            },
        };
        var platform = new InMemoryLampArrayPlatform(handle);
        var controller = new LampArrayLightingController(platform);
        try
        {
            var connection = Task.Run(
                () => controller.ConnectAsync(null, timeout.Token),
                timeout.Token);
            try
            {
                await enableEntered.Task.WaitAsync(timeout.Token);

                handle.IsAvailable = false;
                handle.IsPresent = false;
            }
            finally
            {
                continueEnable.TrySetResult();
            }

            Assert.False(await connection.WaitAsync(timeout.Token));
            Assert.False(controller.IsConnected);
            Assert.Null(controller.ConnectedDevice);
            Assert.Equal(1, handle.DisableCount);
        }
        finally
        {
            continueEnable.TrySetResult();
            await controller.DisposeAsync().AsTask().WaitAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task ConnectAsync_RejectsAStaleHandleWhenRemovalAndReadditionOccurDuringEnable()
    {
        var handle = new InMemoryLampArrayHandle("selected", "GIGABYTE Device", 8, true);
        var platform = new InMemoryLampArrayPlatform(handle);
        handle.OnEnable = () =>
        {
            platform.Remove("selected");
            platform.Restore("selected");
        };
        await using var controller = new LampArrayLightingController(platform);

        var connected = await controller.ConnectAsync(null, TestContext.Current.CancellationToken);

        Assert.False(connected);
        Assert.False(controller.IsConnected);
        Assert.Null(controller.ConnectedDevice);
        Assert.Equal(1, handle.DisableCount);
    }

    [Fact]
    public async Task DevicesChanged_ClearsAConnectionWhenTheSelectedDeviceIsRemoved()
    {
        var handle = new InMemoryLampArrayHandle("selected", "GIGABYTE Device", 8, true);
        var platform = new InMemoryLampArrayPlatform(handle);
        await using var controller = new LampArrayLightingController(platform);
        var notifications = 0;
        controller.DevicesChanged += (_, _) => notifications++;
        await controller.ConnectAsync(null, TestContext.Current.CancellationToken);

        platform.Remove("selected");

        Assert.False(controller.IsConnected);
        Assert.Null(controller.ConnectedDevice);
        Assert.Equal(1, handle.DisableCount);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task ConnectAsync_DisablesTheOldHandleBeforeSwitchingDevices()
    {
        var operations = new List<string>();
        var first = new InMemoryLampArrayHandle("first", "GIGABYTE Device", 8, true, operations);
        var second = new InMemoryLampArrayHandle("second", "Other Device", 8, true, operations);
        var platform = new InMemoryLampArrayPlatform(first, second);
        await using var controller = new LampArrayLightingController(platform);
        await controller.ConnectAsync(null, TestContext.Current.CancellationToken);

        var connected = await controller.ConnectAsync("second", TestContext.Current.CancellationToken);

        Assert.True(connected);
        Assert.Equal(1, first.DisableCount);
        Assert.Equal(1, second.EnableCount);
        Assert.Equal("second", controller.ConnectedDevice?.Id);
        Assert.Equal(["enable:first", "disable:first", "enable:second"], operations);
    }

    [Fact]
    public async Task ReleaseAsync_IsIdempotent()
    {
        var handle = new InMemoryLampArrayHandle("selected", "GIGABYTE Device", 8, true);
        var platform = new InMemoryLampArrayPlatform(handle);
        await using var controller = new LampArrayLightingController(platform);
        await controller.ConnectAsync(null, TestContext.Current.CancellationToken);

        await controller.ReleaseAsync(TestContext.Current.CancellationToken);
        await controller.ReleaseAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, handle.DisableCount);
        Assert.False(controller.IsConnected);
    }

    [Fact]
    public async Task SetColorAsync_ThrowsWhenDisconnected()
    {
        var platform = new InMemoryLampArrayPlatform();
        await using var controller = new LampArrayLightingController(platform);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.SetColorAsync(new RgbColor(1, 2, 3), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAsync_ReleasesTheHandleAndPlatformOnlyOnce()
    {
        var handle = new InMemoryLampArrayHandle("selected", "GIGABYTE Device", 8, true);
        var platform = new InMemoryLampArrayPlatform(handle);
        var controller = new LampArrayLightingController(platform);
        await controller.ConnectAsync(null, TestContext.Current.CancellationToken);

        await controller.DisposeAsync();
        await controller.DisposeAsync();

        Assert.Equal(1, handle.DisableCount);
        Assert.Equal(1, platform.DisposeCount);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesThePlatformWhenDisablingTheHandleThrows()
    {
        var handle = new InMemoryLampArrayHandle("selected", "GIGABYTE Device", 8, true)
        {
            DisableException = new InvalidOperationException("disable failed"),
        };
        var platform = new InMemoryLampArrayPlatform(handle);
        var controller = new LampArrayLightingController(platform);
        await controller.ConnectAsync(null, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => controller.DisposeAsync().AsTask());

        Assert.Equal("disable failed", exception.Message);
        Assert.False(controller.IsConnected);
        Assert.Equal(1, platform.DisposeCount);
        await controller.DisposeAsync();
        Assert.Equal(1, platform.DisposeCount);
    }

    private sealed class InMemoryLampArrayPlatform(params InMemoryLampArrayHandle[] handles) : ILampArrayPlatform
    {
        private readonly IReadOnlyList<InMemoryLampArrayHandle> _handles = handles;
        private TaskCompletionSource? _findAllStarted;
        private TaskCompletionSource? _continueFindAll;

        public event EventHandler? DevicesChanged;

        public int DisposeCount { get; private set; }
        public Task FindAllStarted => _findAllStarted?.Task ?? Task.CompletedTask;

        public async Task<IReadOnlyList<ILampArrayHandle>> FindAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ILampArrayHandle> snapshot = [.. _handles];
            _findAllStarted?.TrySetResult();
            if (_continueFindAll is not null)
            {
                await _continueFindAll.Task.WaitAsync(cancellationToken);
            }

            return snapshot;
        }

        public void BlockFindAll()
        {
            _findAllStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _continueFindAll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void ReleaseFindAll() => _continueFindAll?.TrySetResult();

        public void Remove(string id)
        {
            var handle = _handles.Single(candidate => candidate.Id == id);
            handle.IsAvailable = false;
            handle.IsPresent = false;
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Restore(string id)
        {
            var handle = _handles.Single(candidate => candidate.Id == id);
            handle.IsAvailable = true;
            handle.IsPresent = true;
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class InMemoryLampArrayHandle(
        string id,
        string name,
        int lampCount,
        bool isAvailable,
        List<string>? operations = null) : ILampArrayHandle
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public int LampCount { get; } = lampCount;
        public bool IsAvailable { get; set; } = isAvailable;
        public bool IsPresent { get; set; } = true;
        public int EnableCount { get; private set; }
        public int DisableCount { get; private set; }
        public List<RgbColor> Colors { get; } = [];
        public Action? OnEnable { get; set; }
        public Exception? DisableException { get; set; }

        public void Enable()
        {
            EnableCount++;
            operations?.Add($"enable:{Id}");
            OnEnable?.Invoke();
        }

        public void SetColor(RgbColor color) => Colors.Add(color);

        public void Disable()
        {
            DisableCount++;
            operations?.Add($"disable:{Id}");
            if (DisableException is not null)
            {
                throw DisableException;
            }
        }
    }
}
