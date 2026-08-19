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
        Assert.Equal(ThermalRange.Warm, vm.CurrentRange);
        Assert.Single(vm.History);
    }

    [Fact]
    public void SnapshotUpdate_ProjectsSuppliedRangeWithoutReclassification()
    {
        var runtime = new FakeThermalRuntime();
        using var vm = new MainViewModel(runtime);

        runtime.Publish(Snapshot(RuntimeStatus.Active, 68, DateTimeOffset.UnixEpoch, range: ThermalRange.Cold));

        Assert.Equal(ThermalRange.Cold, vm.CurrentRange);
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
    public void SuspendedSnapshot_PreservesDisabledModeState()
    {
        var runtime = new FakeThermalRuntime();
        using var vm = new MainViewModel(runtime);

        runtime.Publish(Snapshot(RuntimeStatus.Disabled, null, DateTimeOffset.UnixEpoch, isModeEnabled: false));
        runtime.Publish(Snapshot(RuntimeStatus.Suspended, null, DateTimeOffset.UnixEpoch.AddSeconds(1), isModeEnabled: false));

        Assert.False(vm.IsModeEnabled);
    }

    [Fact]
    public void SuspendedSnapshot_PreservesEnabledModeState()
    {
        var runtime = new FakeThermalRuntime();
        using var vm = new MainViewModel(runtime);

        runtime.Publish(Snapshot(RuntimeStatus.Active, 68, DateTimeOffset.UnixEpoch));
        runtime.Publish(Snapshot(RuntimeStatus.Suspended, null, DateTimeOffset.UnixEpoch.AddSeconds(1)));

        Assert.True(vm.IsModeEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialSuspendedSnapshot_UsesAuthoritativeModeBit(bool enabled)
    {
        var runtime = new FakeThermalRuntime();
        runtime.Publish(Snapshot(RuntimeStatus.Suspended, null, DateTimeOffset.UnixEpoch, isModeEnabled: enabled));

        using var vm = new MainViewModel(runtime);

        Assert.Equal(enabled, vm.IsModeEnabled);
    }

    [Fact]
    public async Task ToggleMode_UsesDifferingRuntimeOutcomePublishedBeforeCompletion()
    {
        var runtime = new FakeThermalRuntime { ModeSetOutcome = RuntimeStatus.Disabled };
        using var vm = new MainViewModel(runtime);
        runtime.Publish(Snapshot(RuntimeStatus.Active, 68, DateTimeOffset.UnixEpoch));

        await vm.ToggleModeCommand.ExecuteAsync();

        Assert.False(vm.IsModeEnabled);
    }

    [Fact]
    public async Task ToggleMode_WhileSuspended_UsesEnabledBitPublishedByRuntime()
    {
        var runtime = new FakeThermalRuntime { ModeSetOutcome = RuntimeStatus.Suspended };
        using var vm = new MainViewModel(runtime);
        runtime.Publish(Snapshot(RuntimeStatus.Suspended, null, DateTimeOffset.UnixEpoch, isModeEnabled: false));

        await vm.ToggleModeCommand.ExecuteAsync();

        Assert.True(vm.IsModeEnabled);
    }

    [Fact]
    public void Dispose_IgnoresSnapshotAlreadyQueuedOnSynchronizationContext()
    {
        var runtime = new FakeThermalRuntime();
        var context = new QueuedSynchronizationContext();
        var vm = new MainViewModel(runtime, context);

        runtime.Publish(Snapshot(RuntimeStatus.Active, 68, DateTimeOffset.UnixEpoch));
        vm.Dispose();
        context.Drain();

        Assert.Empty(vm.History);
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

    private static RuntimeSnapshot Snapshot(RuntimeStatus status, double? temperature, DateTimeOffset timestamp, RgbColor? color = null, ThermalRange? range = ThermalRange.Warm, bool isModeEnabled = true) => new(
        status,
        temperature is { } celsius ? new TemperatureReading(celsius, "NVML", "GPU 0", timestamp) : null,
        color ?? new RgbColor(0xFF, 0xC6, 0x4A),
        range,
        new LightingDeviceInfo("lamp", "Desk lamp", 4, true),
        null,
        timestamp,
        isModeEnabled);

    private sealed class FakeThermalRuntime : IThermalRuntime
    {
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged;
        public RuntimeSnapshot CurrentSnapshot { get; private set; } = Snapshot(RuntimeStatus.Disabled, null, DateTimeOffset.MinValue, isModeEnabled: false);
        public bool ModeEnabled { get; private set; }
        public AppSettings CurrentSettings { get; private set; } = AppSettings.Default;
        public RuntimeStatus? ModeSetOutcome { get; init; }

        public void Publish(RuntimeSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }

        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            ModeEnabled = enabled;
            CurrentSettings = CurrentSettings with { IsModeEnabled = enabled };
            Publish(Snapshot(ModeSetOutcome ?? (enabled ? RuntimeStatus.Connecting : RuntimeStatus.Disabled), null, CurrentSnapshot.Timestamp.AddSeconds(1), isModeEnabled: ModeSetOutcome is not RuntimeStatus.Disabled && enabled));
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _work = [];

        public override void Post(SendOrPostCallback d, object? state) => _work.Enqueue((d, state));

        public void Drain()
        {
            while (_work.TryDequeue(out var item))
            {
                item.Callback(item.State);
            }
        }
    }
}
