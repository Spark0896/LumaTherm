using LumaTherm.App.ViewModels;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Sensors;
using LumaTherm.Core.Settings;

namespace LumaTherm.App.Tests.ViewModels;

public sealed class MainViewModelTests
{
    [Fact]
    public void SnapshotUpdate_ProjectsTemperatureColorAndStatus()
    {
        var runtime = new FakeThermalRuntime();
        using var vm = new MainViewModel(runtime);

        runtime.Publish(Snapshot(RuntimeStatus.Active, 68, new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero)));

        Assert.Equal("68°C", vm.TemperatureText);
        Assert.Equal("Режим активен", vm.StatusText);
        Assert.Equal("#FFC64A", vm.CurrentColorHex);
        Assert.Equal("GPU 0", vm.GpuName);
        Assert.Equal("NVML", vm.SensorSource);
        Assert.Equal("Desk lamp", vm.LightingDeviceName);
        Assert.True(vm.IsModeEnabled);
        Assert.Single(vm.History);
    }

    [Fact]
    public void History_IsLimitedToOneHundredTwentySamples()
    {
        var runtime = new FakeThermalRuntime();
        using var vm = new MainViewModel(runtime);
        var start = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 130; i++)
        {
            runtime.Publish(Snapshot(RuntimeStatus.Active, 50 + i % 20, start.AddMilliseconds(i * 500)));
        }

        Assert.Equal(120, vm.History.Count);
        Assert.Equal(start.AddSeconds(5), vm.History[0].Timestamp);
        Assert.Equal(start.AddSeconds(64.5), vm.History[^1].Timestamp);
    }

    [Fact]
    public void RepeatedRenderSnapshot_DoesNotDuplicateHistoryButRefreshesDisplay()
    {
        var runtime = new FakeThermalRuntime();
        using var vm = new MainViewModel(runtime);
        var sampleTime = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        runtime.Publish(Snapshot(RuntimeStatus.Active, 67, sampleTime, new RgbColor(1, 2, 3)));
        runtime.Publish(Snapshot(RuntimeStatus.Active, 68, sampleTime, new RgbColor(4, 5, 6)));

        Assert.Single(vm.History);
        Assert.Equal(68, vm.CurrentTemperature);
        Assert.Equal("#040506", vm.CurrentColorHex);
    }

    [Theory]
    [InlineData(RuntimeStatus.Disabled, "Режим выключен")]
    [InlineData(RuntimeStatus.Connecting, "Подключение…")]
    [InlineData(RuntimeStatus.Active, "Режим активен")]
    [InlineData(RuntimeStatus.HoldingLastColor, "Сохранение последнего цвета")]
    [InlineData(RuntimeStatus.SensorUnavailable, "Датчик температуры недоступен")]
    [InlineData(RuntimeStatus.LightingUnavailable, "Подсветка недоступна")]
    [InlineData(RuntimeStatus.Suspended, "Работа приостановлена")]
    [InlineData(RuntimeStatus.Faulted, "Ошибка работы")]
    public void SnapshotUpdate_MapsEveryRuntimeStatus(RuntimeStatus status, string expected)
    {
        var runtime = new FakeThermalRuntime();
        using var vm = new MainViewModel(runtime);

        runtime.Publish(Snapshot(status, null, new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero)));

        Assert.Equal(expected, vm.StatusText);
    }

    [Fact]
    public async Task ToggleMode_UpdatesVisibleStateAfterRuntimeSucceeds()
    {
        var runtime = new FakeThermalRuntime();
        using var vm = new MainViewModel(runtime);

        await vm.ToggleModeCommand.ExecuteAsync();

        Assert.True(vm.IsModeEnabled);
        Assert.True(runtime.ModeEnabled);
    }

    [Fact]
    public void Dispose_UnsubscribesFromRuntimeSnapshots()
    {
        var runtime = new FakeThermalRuntime();
        var vm = new MainViewModel(runtime);
        vm.Dispose();

        runtime.Publish(Snapshot(RuntimeStatus.Active, 68, new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero)));

        Assert.Empty(vm.History);
    }

    private static RuntimeSnapshot Snapshot(RuntimeStatus status, double? temperature, DateTimeOffset timestamp, RgbColor? color = null) => new(
        status,
        temperature is { } celsius ? new TemperatureReading(celsius, "NVML", "GPU 0", timestamp) : null,
        color ?? new RgbColor(0xFF, 0xC6, 0x4A),
        ThermalRange.Warm,
        new LightingDeviceInfo("lamp", "Desk lamp", 4, true),
        null,
        timestamp);

    private sealed class FakeThermalRuntime : IThermalRuntime
    {
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged;
        public RuntimeSnapshot CurrentSnapshot { get; private set; } = Snapshot(RuntimeStatus.Disabled, null, DateTimeOffset.MinValue);
        public bool ModeEnabled { get; private set; }

        public void Publish(RuntimeSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }

        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            ModeEnabled = enabled;
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
