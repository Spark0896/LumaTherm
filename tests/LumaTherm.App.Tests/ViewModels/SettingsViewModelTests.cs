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
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { ColdTemperature = 65 };

        await vm.SaveAsync();

        Assert.Equal("Температуры должны возрастать с шагом не менее 1 °C.", vm.ValidationMessage);
        Assert.Empty(recorder.Events);
        Assert.Equal(ThermalProfile.Default, vm.LiveSettings.Profile);
    }

    [Fact]
    public async Task Save_ValidProfile_PersistsExactlyOnceAndCommitsAfterRuntime()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default)
        {
            ColdTemperature = 40,
            WarmTemperature = 67,
            HotTemperature = 88,
        };
        vm.ProfileSaved += (_, _) => recorder.Record("vm.commit");

        await vm.SaveAsync();

        Assert.Equal(["runtime.persist", "vm.commit"], recorder.Events);
        Assert.Equal(1, runtime.PersistenceCount);
        Assert.Equal(40, vm.LiveSettings.Profile.ColdTemperature);
        Assert.Equal(67, vm.LiveSettings.Profile.WarmTemperature);
        Assert.Equal(88, vm.LiveSettings.Profile.HotTemperature);
        Assert.Null(vm.ValidationMessage);
    }

    [Fact]
    public async Task Save_ChangedAutostart_OrdersStartupRuntimeAndViewModelCommit()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { IsAutostartEnabled = true };
        vm.ProfileSaved += (_, _) => recorder.Record("vm.commit");

        await vm.SaveAsync();

        Assert.Equal(["startup:true", "runtime.persist", "vm.commit"], recorder.Events);
        Assert.True(vm.LiveSettings.IsAutostartEnabled);
    }

    [Fact]
    public async Task Save_RuntimeFailure_RollsAutostartBackAndKeepsLiveSettings()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { Failure = new InvalidOperationException("store unavailable") };
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { IsAutostartEnabled = true };

        await vm.SaveAsync();

        Assert.Equal(["startup:true", "runtime.fail", "startup:false"], recorder.Events);
        Assert.Equal(0, runtime.PersistenceCount);
        Assert.False(vm.LiveSettings.IsAutostartEnabled);
        Assert.Equal("Не удалось сохранить настройки.", vm.ValidationMessage);
    }

    [Fact]
    public async Task Save_RuntimeArgumentFailure_AlsoRollsAutostartBack()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { Failure = new ArgumentException("write rejected") };
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { IsAutostartEnabled = true };

        await vm.SaveAsync();

        Assert.Equal(["startup:true", "runtime.fail", "startup:false"], recorder.Events);
        Assert.False(vm.LiveSettings.IsAutostartEnabled);
        Assert.Equal("Не удалось сохранить настройки.", vm.ValidationMessage);
    }

    [Fact]
    public async Task Save_RuntimeAndRollbackFailure_ShowsSafeRollbackError()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { Failure = new InvalidOperationException("store unavailable") };
        var startup = new FakeStartupService(recorder) { FailWhenDisabled = true };
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { IsAutostartEnabled = true };

        await vm.SaveAsync();

        Assert.Equal(["startup:true", "runtime.fail", "startup:false"], recorder.Events);
        Assert.Equal("Не удалось сохранить настройки. Не удалось вернуть настройку автозапуска.", vm.ValidationMessage);
        Assert.False(vm.LiveSettings.IsAutostartEnabled);
    }

    [Fact]
    public async Task SaveCommand_PreventsReentryAndShowsRuntimeFailureAfterCompletion()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { GateUpdates = true };
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default);

        var first = vm.SaveCommand.ExecuteAsync();
        await runtime.UpdateEntered;
        var second = vm.SaveCommand.ExecuteAsync();

        Assert.True(vm.SaveCommand.IsExecuting);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.Equal(1, runtime.UpdateCalls);

        runtime.FailGate(new InvalidOperationException("store unavailable"));
        await Task.WhenAll(first, second);

        Assert.False(vm.SaveCommand.IsExecuting);
        Assert.True(vm.SaveCommand.CanExecute(null));
        Assert.Equal(1, runtime.UpdateCalls);
        Assert.Equal("Не удалось сохранить настройки.", vm.ValidationMessage);
    }

    [Fact]
    public async Task Save_ValidProfile_UpdatesBoundDashboardProfileOnlyAfterRuntimeSuccess()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var settings = new SettingsViewModel(runtime, startup, AppSettings.Default) { ColdTemperature = 40 };
        using var dashboard = new MainViewModel(runtime);
        dashboard.SynchronizeProfile(settings);

        await settings.SaveAsync();

        Assert.Equal(40, dashboard.Profile.ColdTemperature);
    }

    [Fact]
    public async Task SynchronizeProfile_AfterDispose_IsIgnored()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var settings = new SettingsViewModel(runtime, startup, AppSettings.Default) { ColdTemperature = 40 };
        var dashboard = new MainViewModel(runtime);
        dashboard.Dispose();

        dashboard.SynchronizeProfile(settings);
        await settings.SaveAsync();

        Assert.Equal(ThermalProfile.Default, dashboard.Profile);
    }

    [Fact]
    public void ResetDefaults_ReplacesEditableValuesWithoutPersisting()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { ColdTemperature = 40, IsAutostartEnabled = true };

        vm.ResetDefaultsCommand.Execute(null);

        Assert.Equal(35, vm.ColdTemperature);
        Assert.False(vm.IsAutostartEnabled);
        Assert.Empty(recorder.Events);
    }

    private sealed class OperationRecorder
    {
        public List<string> Events { get; } = [];
        public void Record(string value) => Events.Add(value);
    }

    private sealed class FakeThermalRuntime(OperationRecorder recorder) : IThermalRuntime
    {
        private readonly TaskCompletionSource _updateEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _updateGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged { add { } remove { } }
        public RuntimeSnapshot CurrentSnapshot { get; } = new(RuntimeStatus.Disabled, null, null, null, null, null, DateTimeOffset.MinValue);
        public Exception? Failure { get; init; }
        public bool GateUpdates { get; init; }
        public int UpdateCalls { get; private set; }
        public int PersistenceCount { get; private set; }
        public Task UpdateEntered => _updateEntered.Task;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
        public async Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            UpdateCalls++;
            if (GateUpdates)
            {
                _updateEntered.TrySetResult();
                await _updateGate.Task;
            }

            if (Failure is { } failure)
            {
                recorder.Record("runtime.fail");
                throw failure;
            }

            PersistenceCount++;
            recorder.Record("runtime.persist");
        }
        public void FailGate(Exception failure) => _updateGate.TrySetException(failure);
        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeStartupService(OperationRecorder recorder) : IStartupService
    {
        public bool FailWhenDisabled { get; init; }
        public Task<bool> GetEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            recorder.Record($"startup:{enabled.ToString().ToLowerInvariant()}");
            return !enabled && FailWhenDisabled
                ? Task.FromException(new InvalidOperationException("rollback denied"))
                : Task.CompletedTask;
        }
    }
}
