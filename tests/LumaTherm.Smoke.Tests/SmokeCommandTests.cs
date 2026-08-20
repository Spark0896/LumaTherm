using System.Reflection;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Sensors;

namespace LumaTherm.Smoke.Tests;

public sealed class SmokeCommandTests
{
    [Fact]
    public async Task Sensor_PrintsReadingWithoutCreatingLighting()
    {
        var normal = new FakeTemperatureProvider(new TemperatureReading(61.5, "NVML", "NVIDIA GeForce RTX 5070", DateTimeOffset.UnixEpoch));
        var fallback = new FakeTemperatureProvider(null);
        var lights = new FakeLightingController();

        var result = await RunAsync(["sensor"], normal, fallback, lights);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("NVML", result.Output, StringComparison.Ordinal);
        Assert.Contains("NVIDIA GeForce RTX 5070", result.Output, StringComparison.Ordinal);
        Assert.Contains("61.5", result.Output, StringComparison.Ordinal);
        Assert.Equal(1, normal.ReadCount);
        Assert.Equal(0, fallback.ReadCount);
        Assert.Equal(0, lights.DiscoverCount);
        Assert.Equal(0, lights.ConnectCount);
        Assert.Empty(lights.Colors);
    }

    [Fact]
    public async Task Sensor_SkipNvmlUsesOnlyAfterburnerFallback()
    {
        var normal = new FakeTemperatureProvider(new TemperatureReading(61.5, "NVML", "NVIDIA GeForce RTX 5070", DateTimeOffset.UnixEpoch));
        var fallback = new FakeTemperatureProvider(new TemperatureReading(62, "MSI Afterburner", "NVIDIA GeForce RTX 5070", DateTimeOffset.UnixEpoch));
        var lights = new FakeLightingController();

        var result = await RunAsync(["sensor", "--skip-nvml"], normal, fallback, lights);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("MSI Afterburner", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("NVML", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, normal.ReadCount);
        Assert.Equal(1, fallback.ReadCount);
        Assert.Equal(0, lights.DiscoverCount);
        Assert.Empty(lights.Colors);
    }

    [Fact]
    public async Task Lights_PrintsDiscoveredDevicesWithoutTakingControl()
    {
        var lights = new FakeLightingController(
            new LightingDeviceInfo("HID#VID_048D&PID_5702", "GIGABYTE Device", 12, true),
            new LightingDeviceInfo("other", "Other Device", 4, false));

        var result = await RunAsync(["lights"], new FakeTemperatureProvider(null), new FakeTemperatureProvider(null), lights);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("GIGABYTE Device", result.Output, StringComparison.Ordinal);
        Assert.Contains("HID#VID_048D&PID_5702", result.Output, StringComparison.Ordinal);
        Assert.Contains("12", result.Output, StringComparison.Ordinal);
        Assert.Contains("available", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, lights.DiscoverCount);
        Assert.Equal(0, lights.ConnectCount);
        Assert.Empty(lights.Colors);
        Assert.Equal(0, lights.ReleaseCount);
    }

    [Fact]
    public async Task Cycle_WithoutConfirmationRejectsBeforeAnyLightWrite()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true));

        var result = await RunAsync(["cycle"], new FakeTemperatureProvider(null), new FakeTemperatureProvider(null), lights);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("--confirm-light-write", result.Output, StringComparison.Ordinal);
        Assert.Equal(0, lights.ConnectCount);
        Assert.Empty(lights.Colors);
        Assert.Equal(0, lights.ReleaseCount);
    }

    [Fact]
    public async Task ConfirmedCycle_WritesTheThreeSafetyColorsInOrderAndReleases()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true));

        var result = await RunAsync(["cycle", "--confirm-light-write", "--non-interactive"], new FakeTemperatureProvider(null), new FakeTemperatureProvider(null), lights);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["#50C8FF", "#FFC64A", "#FF565D"], lights.Colors.Select(color => color.ToHex()));
        Assert.Equal(1, lights.ReleaseCount);
    }

    [Fact]
    public async Task ConfirmedCycle_ReleasesOwnershipWhenTheSecondWriteFails()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true)) { FailOnWrite = 2 };

        var result = await RunAsync(["cycle", "--confirm-light-write", "--non-interactive"], new FakeTemperatureProvider(null), new FakeTemperatureProvider(null), lights);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["#50C8FF", "#FFC64A"], lights.Colors.Select(color => color.ToHex()));
        Assert.Equal(1, lights.ReleaseCount);
    }

    [Fact]
    public async Task ConfirmedSimulationUsesSmoothedProductionColorsAndReleasesOwnership()
    {
        var lights = new FakeLightingController(new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true));

        var result = await RunAsync(["simulate", "--from", "35", "--to", "85", "--seconds", "1", "--confirm-light-write", "--non-interactive"], new FakeTemperatureProvider(null), new FakeTemperatureProvider(null), lights);

        Assert.Equal(0, result.ExitCode);
        Assert.True(lights.Colors.Count >= 2);
        Assert.Equal("#50C8FF", lights.Colors[0].ToHex());
        Assert.NotEqual("#FF565D", lights.Colors[1].ToHex());
        Assert.Equal(1, lights.ReleaseCount);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string[] arguments,
        ITemperatureProvider normal,
        ITemperatureProvider fallback,
        ILightingController lights)
    {
        var assembly = Assembly.Load("LumaTherm.Smoke");
        var commandType = assembly.GetType("LumaTherm.Smoke.SmokeCommand")
            ?? throw new InvalidOperationException("SmokeCommand does not exist yet.");
        var method = commandType.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("SmokeCommand.RunAsync does not exist yet.");
        using var output = new StringWriter();
        var task = (Task<int>)method.Invoke(null, [arguments, normal, fallback, lights, output, new StringReader("YES"), TestContext.Current.CancellationToken])!;
        return (await task, output.ToString());
    }

    private sealed class FakeTemperatureProvider(TemperatureReading? reading) : ITemperatureProvider
    {
        public int ReadCount { get; private set; }

        public ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return ValueTask.FromResult(reading);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeLightingController(params LightingDeviceInfo[] devices) : ILightingController
    {
        private readonly IReadOnlyList<LightingDeviceInfo> _devices = devices;

        public int DiscoverCount { get; private set; }
        public int ConnectCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public int FailOnWrite { get; init; }
        public List<RgbColor> Colors { get; } = [];
        public bool IsConnected { get; private set; }
        public LightingDeviceInfo? ConnectedDevice => IsConnected ? _devices.FirstOrDefault(device => device.IsAvailable) : null;
        public event EventHandler? DevicesChanged
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiscoverCount++;
            return Task.FromResult(_devices);
        }

        public Task<bool> ConnectAsync(string? preferredDeviceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCount++;
            IsConnected = _devices.Any(device => device.IsAvailable);
            return Task.FromResult(IsConnected);
        }

        public Task SetColorAsync(RgbColor color, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Colors.Add(color);
            if (Colors.Count == FailOnWrite)
            {
                throw new InvalidOperationException("Write failed.");
            }

            return Task.CompletedTask;
        }

        public Task ReleaseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCount++;
            IsConnected = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
