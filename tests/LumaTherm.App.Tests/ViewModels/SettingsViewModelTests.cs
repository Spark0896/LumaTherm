using LumaTherm.App.ViewModels;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;

namespace LumaTherm.App.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task Save_InvalidCrossedTemperatures_ShowsValidationAndDoesNotChangeLiveSettings()
    {
        var runtime = new FakeThermalRuntime();
        var store = new FakeSettingsStore();
        var startup = new FakeStartupService();
        var vm = new SettingsViewModel(runtime, store, startup, AppSettings.Default);
        vm.ColdTemperature = 65;

        await vm.SaveAsync();

        Assert.Equal("Температуры должны возрастать с шагом не менее 1 °C.", vm.ValidationMessage);
        Assert.Empty(runtime.UpdatedSettings);
        Assert.Empty(store.SavedSettings);
        Assert.Equal(ThermalProfile.Default.ColdTemperature, vm.LiveSettings.Profile.ColdTemperature);
    }

    [Fact]
    public async Task Save_ValidProfile_UpdatesRuntimeAndPersistsSameSettings()
    {
        var runtime = new FakeThermalRuntime();
        var store = new FakeSettingsStore();
        var startup = new FakeStartupService();
        var vm = new SettingsViewModel(runtime, store, startup, AppSettings.Default)
        {
            ColdTemperature = 40,
            WarmTemperature = 67,
            HotTemperature = 88,
        };

        await vm.SaveAsync();

        var saved = Assert.Single(store.SavedSettings);
        Assert.Equal(40, saved.Profile.ColdTemperature);
        Assert.Equal(67, saved.Profile.WarmTemperature);
        Assert.Equal(88, saved.Profile.HotTemperature);
        Assert.Equal(saved, Assert.Single(runtime.UpdatedSettings));
        Assert.Null(vm.ValidationMessage);
        Assert.Equal(saved, vm.LiveSettings);
    }

    [Fact]
    public async Task Save_ChangedAutostart_ChangesStartupBeforePersistingSettings()
    {
        var runtime = new FakeThermalRuntime();
        var store = new FakeSettingsStore();
        var startup = new FakeStartupService();
        var vm = new SettingsViewModel(runtime, store, startup, AppSettings.Default)
        {
            IsAutostartEnabled = true,
        };

        await vm.SaveAsync();

        Assert.Equal(["startup:true", "runtime", "store"], startup.Sequence.Concat(runtime.Sequence).Concat(store.Sequence));
        Assert.True(vm.LiveSettings.IsAutostartEnabled);
    }

    [Fact]
    public async Task Save_ValidProfile_UpdatesBoundDashboardProfileOnlyAfterSuccess()
    {
        var runtime = new FakeThermalRuntime();
        var store = new FakeSettingsStore();
        var startup = new FakeStartupService();
        var settings = new SettingsViewModel(runtime, store, startup, AppSettings.Default)
        {
            ColdTemperature = 40,
        };
        using var dashboard = new MainViewModel(runtime);
        dashboard.SynchronizeProfile(settings);

        await settings.SaveAsync();

        Assert.Equal(40, dashboard.Profile.ColdTemperature);
    }

    [Fact]
    public async Task Dispose_IgnoresQueuedProfileUpdates()
    {
        var runtime = new FakeThermalRuntime();
        var store = new FakeSettingsStore();
        var startup = new FakeStartupService();
        var settings = new SettingsViewModel(runtime, store, startup, AppSettings.Default)
        {
            ColdTemperature = 40,
        };
        var context = new QueuedSynchronizationContext();
        var dashboard = new MainViewModel(runtime, context);
        dashboard.SynchronizeProfile(settings);

        await settings.SaveAsync();
        dashboard.Dispose();
        context.Drain();

        Assert.Equal(ThermalProfile.Default.ColdTemperature, dashboard.Profile.ColdTemperature);
    }

    [Fact]
    public async Task Save_WhenStartupFails_LeavesRuntimeAndLiveSettingsUnchanged()
    {
        var runtime = new FakeThermalRuntime();
        var store = new FakeSettingsStore();
        var startup = new FakeStartupService { Failure = new InvalidOperationException("denied") };
        var vm = new SettingsViewModel(runtime, store, startup, AppSettings.Default)
        {
            IsAutostartEnabled = true,
        };

        await vm.SaveAsync();

        Assert.Empty(runtime.UpdatedSettings);
        Assert.Empty(store.SavedSettings);
        Assert.False(vm.LiveSettings.IsAutostartEnabled);
        Assert.Equal("Не удалось изменить автозапуск.", vm.ValidationMessage);
    }

    [Fact]
    public void ResetDefaults_ReplacesEditableValuesWithoutSaving()
    {
        var runtime = new FakeThermalRuntime();
        var store = new FakeSettingsStore();
        var startup = new FakeStartupService();
        var vm = new SettingsViewModel(runtime, store, startup, AppSettings.Default)
        {
            ColdTemperature = 40,
            IsAutostartEnabled = true,
        };

        vm.ResetDefaultsCommand.Execute(null);

        Assert.Equal(35, vm.ColdTemperature);
        Assert.False(vm.IsAutostartEnabled);
        Assert.Empty(store.SavedSettings);
    }

    private sealed class FakeThermalRuntime : IThermalRuntime
    {
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged { add { } remove { } }
        public RuntimeSnapshot CurrentSnapshot { get; } = new(
            RuntimeStatus.Connecting,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.MinValue);
        public List<AppSettings> UpdatedSettings { get; } = [];
        public List<string> Sequence { get; } = [];
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            UpdatedSettings.Add(settings);
            Sequence.Add("runtime");
            return Task.CompletedTask;
        }
        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public List<AppSettings> SavedSettings { get; } = [];
        public List<string> Sequence { get; } = [];
        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new SettingsLoadResult(AppSettings.Default));
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            SavedSettings.Add(settings);
            Sequence.Add("store");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStartupService : IStartupService
    {
        public Exception? Failure { get; init; }
        public List<string> Sequence { get; } = [];
        public Task<bool> GetEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            Sequence.Add($"startup:{enabled.ToString().ToLowerInvariant()}");
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
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
