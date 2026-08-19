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

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal("Температуры должны возрастать с шагом не менее 1 °C.", vm.ValidationMessage);
        Assert.Empty(recorder.Events);
        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
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

        await vm.SaveCommand.ExecuteAsync();

        var expected = AppSettings.Default with
        {
            Profile = new ThermalProfile(
                40,
                ThermalProfile.Default.ColdColor,
                67,
                ThermalProfile.Default.WarmColor,
                88,
                ThermalProfile.Default.HotColor,
                ThermalProfile.Default.SmoothingSeconds),
        };
        Assert.Equal(["runtime.persist", "vm.commit"], recorder.Events);
        Assert.Equal(1, runtime.PersistenceCount);
        Assert.Equal(expected, runtime.CurrentSettings);
        Assert.Equal(expected, vm.LiveSettings);
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

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(["startup:true", "runtime.persist", "vm.commit"], recorder.Events);
        Assert.True(startup.IsEnabled);
        Assert.True(vm.LiveSettings.IsAutostartEnabled);
    }

    [Fact]
    public async Task Save_RuntimeFailure_RollsAutostartBackAndKeepsLiveSettings()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { Failure = new InvalidOperationException("store unavailable") };
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { IsAutostartEnabled = true };

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(["startup:true", "runtime.fail", "startup:false"], recorder.Events);
        Assert.Equal(0, runtime.PersistenceCount);
        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
        Assert.False(startup.IsEnabled);
        Assert.Equal("Не удалось сохранить настройки.", vm.ValidationMessage);
    }

    [Fact]
    public async Task Save_RuntimeArgumentFailure_AlsoRollsAutostartBack()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { Failure = new ArgumentException("write rejected") };
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { IsAutostartEnabled = true };

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(["startup:true", "runtime.fail", "startup:false"], recorder.Events);
        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
        Assert.False(startup.IsEnabled);
        Assert.Equal("Не удалось сохранить настройки.", vm.ValidationMessage);
    }

    [Fact]
    public async Task Save_RuntimeAndRollbackFailure_ShowsSafeRollbackError()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { Failure = new InvalidOperationException("store unavailable") };
        var startup = new FakeStartupService(recorder) { FailWhenDisabled = true };
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { IsAutostartEnabled = true };

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(["startup:true", "runtime.fail", "startup:false"], recorder.Events);
        Assert.Equal("Не удалось сохранить настройки. Не удалось вернуть настройку автозапуска.", vm.ValidationMessage);
        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
        Assert.True(startup.IsEnabled);
    }

    [Fact]
    public async Task Save_RuntimeFailure_RollsBackAndKeepsLastGoodSettings()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { Failure = new InvalidOperationException("write failed") };
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { ColdTemperature = 40, IsAutostartEnabled = true };
        vm.ProfileSaved += (_, _) => recorder.Record("vm.commit");

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
        Assert.False(startup.IsEnabled);
        Assert.Equal(["startup:true", "runtime.fail", "startup:false"], recorder.Events);
        Assert.Equal("Не удалось сохранить настройки.", vm.ValidationMessage);
    }

    [Fact]
    public async Task Save_StartupFailure_LeavesRuntimeAndLiveSettingsUnchanged()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder) { FailWhenEnabled = true };
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { IsAutostartEnabled = true };

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(["startup:true"], recorder.Events);
        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
        Assert.False(startup.IsEnabled);
        Assert.Equal("Не удалось изменить автозапуск.", vm.ValidationMessage);
    }

    [Fact]
    public async Task Save_WhenProfileObserverThrows_KeepsCommittedSettingsAndShowsPostSaveWarning()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { ColdTemperature = 40 };
        vm.ProfileSaved += (_, _) => throw new InvalidOperationException("view failed");

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(runtime.CurrentSettings, vm.LiveSettings);
        Assert.Equal(40, vm.LiveSettings.Profile.ColdTemperature);
        Assert.Equal("Настройки сохранены, но обновление интерфейса выполнено не полностью.", vm.ValidationMessage);
    }

    [Fact]
    public async Task Dispose_IgnoresQueuedProfileUpdates()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var settings = new SettingsViewModel(runtime, startup, AppSettings.Default) { ColdTemperature = 40 };
        var context = new QueuedSynchronizationContext();
        var dashboard = new MainViewModel(runtime, context);
        dashboard.SynchronizeProfile(settings);

        await settings.SaveCommand.ExecuteAsync();
        dashboard.Dispose();
        context.Drain();

        Assert.Equal(ThermalProfile.Default, dashboard.Profile);
    }

    [Fact]
    public async Task SaveCommand_PreventsReentryAndShowsRuntimeFailureAfterCompletion()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { GateUpdates = true };
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { ColdTemperature = 40 };

        var first = vm.SaveCommand.ExecuteAsync();
        await runtime.UpdateEntered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var second = vm.SaveCommand.ExecuteAsync();

        Assert.True(vm.SaveCommand.IsExecuting);
        Assert.False(vm.SaveCommand.CanExecute(null));
        Assert.Equal(1, runtime.UpdateCalls);

        runtime.FailGate(new InvalidOperationException("store unavailable"));
        await Task.WhenAll(first, second)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(vm.SaveCommand.IsExecuting);
        Assert.True(vm.SaveCommand.CanExecute(null));
        Assert.Equal(1, runtime.UpdateCalls);
        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
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

        await settings.SaveCommand.ExecuteAsync();

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
        await settings.SaveCommand.ExecuteAsync();

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

    [Fact]
    public async Task SaveCommand_UnexpectedPreCommitFailure_CompensatesStartupAndUsesCommandExceptionHandler()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { Failure = new UnexpectedRuntimeException() };
        var startup = new FakeStartupService(recorder) { FailWhenDisabled = true };
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { IsAutostartEnabled = true };

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(["startup:true", "runtime.fail", "startup:false"], recorder.Events);
        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
        Assert.True(startup.IsEnabled);
        Assert.Equal("Не удалось сохранить настройки.", vm.ValidationMessage);
        Assert.False(vm.SaveCommand.IsExecuting);
        Assert.True(vm.SaveCommand.CanExecute(null));
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
        public bool ThrowAfterCommit { get; init; }
        public AppSettings CurrentSettings { get; private set; } = AppSettings.Default;
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
            CurrentSettings = settings;
            if (ThrowAfterCommit)
            {
                throw new InvalidOperationException("subscriber failed");
            }
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
        public bool FailWhenEnabled { get; init; }
        public bool IsEnabled { get; private set; }
        public Task<bool> GetEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(IsEnabled);
        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            recorder.Record($"startup:{enabled.ToString().ToLowerInvariant()}");
            if ((!enabled && FailWhenDisabled) || (enabled && FailWhenEnabled))
            {
                return Task.FromException(new InvalidOperationException("rollback denied"));
            }

            IsEnabled = enabled;
            return Task.CompletedTask;
        }
    }

    private sealed class UnexpectedRuntimeException : Exception;

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
