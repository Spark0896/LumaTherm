using LumaTherm.App.ViewModels;
using LumaTherm.App.Services;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;

namespace LumaTherm.App.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task PickColdColor_SelectedColorChangesOnlyColdStopAndUsesRuntimeSaveOnce()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var picker = new FakeColorPicker(new RgbColor(1, 2, 3));
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default, picker);

        await vm.PickColdColorCommand.ExecuteAsync();

        Assert.Equal(new RgbColor(1, 2, 3), vm.ColdColor);
        Assert.Equal("#010203", vm.ColdColorHex);
        Assert.Equal(ThermalProfile.Default.WarmColor, vm.WarmColor);
        Assert.Equal(ThermalProfile.Default.HotColor, vm.HotColor);
        Assert.Equal(ThermalProfile.Default.ColdColor, picker.CurrentColor);
        Assert.Equal(1, runtime.UpdateCalls);
        Assert.Equal(vm.LiveSettings, runtime.CurrentSettings);
        Assert.Equal(new RgbColor(1, 2, 3), runtime.CurrentSettings.Profile.ColdColor);
    }

    [Fact]
    public async Task PickWarmColor_SelectedColorChangesOnlyWarmStopAndUsesRuntimeSaveOnce()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var picker = new FakeColorPicker(new RgbColor(4, 5, 6));
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default, picker);

        await vm.PickWarmColorCommand.ExecuteAsync();

        Assert.Equal(ThermalProfile.Default.ColdColor, vm.ColdColor);
        Assert.Equal(new RgbColor(4, 5, 6), vm.WarmColor);
        Assert.Equal("#040506", vm.WarmColorHex);
        Assert.Equal(ThermalProfile.Default.HotColor, vm.HotColor);
        Assert.Equal(ThermalProfile.Default.WarmColor, picker.CurrentColor);
        Assert.Equal(1, runtime.UpdateCalls);
    }

    [Fact]
    public async Task PickHotColor_SelectedColorChangesOnlyHotStopAndUsesRuntimeSaveOnce()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var picker = new FakeColorPicker(new RgbColor(7, 8, 9));
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default, picker);

        await vm.PickHotColorCommand.ExecuteAsync();

        Assert.Equal(ThermalProfile.Default.ColdColor, vm.ColdColor);
        Assert.Equal(ThermalProfile.Default.WarmColor, vm.WarmColor);
        Assert.Equal(new RgbColor(7, 8, 9), vm.HotColor);
        Assert.Equal("#070809", vm.HotColorHex);
        Assert.Equal(ThermalProfile.Default.HotColor, picker.CurrentColor);
        Assert.Equal(1, runtime.UpdateCalls);
    }

    [Fact]
    public async Task PickColdColor_CancelChangesNothingAndDoesNotSave()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var picker = new FakeColorPicker(null);
        var vm = new SettingsViewModel(runtime, new FakeStartupService(recorder), AppSettings.Default, picker);

        await vm.PickColdColorCommand.ExecuteAsync();

        Assert.Equal(ThermalProfile.Default.ColdColor, vm.ColdColor);
        Assert.Equal(ThermalProfile.Default.ColdColor, picker.CurrentColor);
        Assert.Equal(0, runtime.UpdateCalls);
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public async Task SaveAndPicker_ShareOneSerializedMutationPipelineWithoutOverlappingPersistence()
    {
        var recorder = new OperationRecorder();
        var runtime = new BlockingThermalRuntime();
        var picker = new FakeColorPicker(new RgbColor(1, 2, 3));
        var vm = new SettingsViewModel(runtime, new FakeStartupService(recorder), AppSettings.Default, picker)
        {
            ColdTemperature = 40,
        };

        var save = vm.SaveCommand.ExecuteAsync();
        await runtime.FirstUpdateEntered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var pick = vm.PickColdColorCommand.ExecuteAsync();

        Assert.Equal(0, picker.PickCalls);
        Assert.Equal(1, runtime.UpdateCalls);
        Assert.Equal(1, runtime.MaxConcurrentUpdates);

        runtime.ReleaseFirstUpdate();
        await Task.WhenAll(save, pick).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(1, picker.PickCalls);
        Assert.Equal(2, runtime.UpdateCalls);
        Assert.Equal(1, runtime.MaxConcurrentUpdates);
        Assert.Equal(2, runtime.PersistedCandidates.Count);
        Assert.Equal(40, runtime.PersistedCandidates[0].Profile.ColdTemperature);
        Assert.Equal(ThermalProfile.Default.ColdColor, runtime.PersistedCandidates[0].Profile.ColdColor);
        Assert.Equal(new RgbColor(1, 2, 3), runtime.PersistedCandidates[1].Profile.ColdColor);
        Assert.Equal(runtime.PersistedCandidates[1], vm.LiveSettings);
    }

    [Fact]
    public async Task DiscoverLighting_MultipleDevicesShowsSelectorAndSavesSelectedStableId()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var discovery = new FakeLightingDeviceDiscovery(
            new LightingDeviceInfo("lamp-a", "GIGABYTE Device", 4, true),
            new LightingDeviceInfo("lamp-b", "Desk Lamp", 8, true));
        var saved = AppSettings.Default with { PreferredLightingDeviceId = "lamp-b" };
        var vm = new SettingsViewModel(runtime, startup, saved, new FakeColorPicker(null), discovery);

        await vm.DiscoverLightingDevicesCommand.ExecuteAsync();

        Assert.True(vm.IsLightingDeviceSelectorVisible);
        Assert.Equal(["lamp-a", "lamp-b"], vm.LightingDevices.Select(device => device.Id));
        Assert.Equal("lamp-b", vm.SelectedLightingDeviceId);

        vm.SelectedLightingDeviceId = "lamp-a";
        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal("lamp-a", runtime.CurrentSettings.PreferredLightingDeviceId);
        Assert.Equal(1, runtime.UpdateCalls);
    }

    [Fact]
    public async Task DiscoverLighting_NoDevicesShowsNonFatalDirectLampArrayStatus()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(
            runtime,
            startup,
            AppSettings.Default,
            new FakeColorPicker(null),
            new FakeLightingDeviceDiscovery());

        await vm.DiscoverLightingDevicesCommand.ExecuteAsync();

        Assert.Empty(vm.LightingDevices);
        Assert.False(vm.IsLightingDeviceSelectorVisible);
        Assert.Null(vm.SelectedLightingDeviceId);
        Assert.Equal("Устройства Windows LampArray не найдены.", vm.LightingHardwareStatus);
        Assert.Null(vm.ValidationMessage);
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public async Task DiscoverLighting_OneAvailableDeviceKeepsCompactDirectLampArrayPresentation()
    {
        var recorder = new OperationRecorder();
        var vm = new SettingsViewModel(
            new FakeThermalRuntime(recorder),
            new FakeStartupService(recorder),
            AppSettings.Default,
            new FakeColorPicker(null),
            new FakeLightingDeviceDiscovery(new LightingDeviceInfo("lamp-a", "GIGABYTE Device", 4, true)));

        await vm.DiscoverLightingDevicesCommand.ExecuteAsync();

        Assert.False(vm.IsLightingDeviceSelectorVisible);
        Assert.Equal("lamp-a", vm.SelectedLightingDeviceId);
        Assert.Equal("GIGABYTE Device", vm.LightingDeviceName);
        Assert.Equal("Подключено напрямую через Windows LampArray.", vm.LightingHardwareStatus);
    }

    [Fact]
    public async Task DiscoverLighting_UnavailableSavedIdRemainsSelectableUntilUserChoosesAnotherDevice()
    {
        var recorder = new OperationRecorder();
        var saved = AppSettings.Default with { PreferredLightingDeviceId = "lamp-missing" };
        var vm = new SettingsViewModel(
            new FakeThermalRuntime(recorder),
            new FakeStartupService(recorder),
            saved,
            new FakeColorPicker(null),
            new FakeLightingDeviceDiscovery(new LightingDeviceInfo("lamp-a", "Desk Lamp", 8, true)));

        await vm.DiscoverLightingDevicesCommand.ExecuteAsync();

        Assert.True(vm.IsLightingDeviceSelectorVisible);
        var unavailable = Assert.Single(vm.LightingDevices, device => device.Id == "lamp-missing");
        Assert.False(unavailable.IsAvailable);
        Assert.Contains("недоступно", unavailable.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("lamp-missing", vm.SelectedLightingDeviceId);
        Assert.Equal("Сохранённое устройство недоступно или занято другим контроллером.", vm.LightingHardwareStatus);
    }

    [Fact]
    public async Task SelectLighting_AvailableDeviceRemovesStaleUnavailableChoiceAndUpdatesDirectStatus()
    {
        var recorder = new OperationRecorder();
        var saved = AppSettings.Default with { PreferredLightingDeviceId = "lamp-missing" };
        var vm = new SettingsViewModel(
            new FakeThermalRuntime(recorder),
            new FakeStartupService(recorder),
            saved,
            new FakeColorPicker(null),
            new FakeLightingDeviceDiscovery(new LightingDeviceInfo("lamp-a", "Desk Lamp", 8, true)));
        await vm.DiscoverLightingDevicesCommand.ExecuteAsync();

        vm.SelectedLightingDeviceId = "lamp-a";

        Assert.DoesNotContain(vm.LightingDevices, device => device.Id == "lamp-missing");
        Assert.False(vm.IsLightingDeviceSelectorVisible);
        Assert.Equal("Desk Lamp", vm.LightingDeviceName);
        Assert.Equal("Подключено напрямую через Windows LampArray.", vm.LightingHardwareStatus);
    }

    [Fact]
    public async Task DiscoverLighting_UnavailableSavedIdStaysVisibleWhenNoDevicesAreDiscovered()
    {
        var recorder = new OperationRecorder();
        var saved = AppSettings.Default with { PreferredLightingDeviceId = "lamp-missing" };
        var vm = new SettingsViewModel(
            new FakeThermalRuntime(recorder),
            new FakeStartupService(recorder),
            saved,
            new FakeColorPicker(null),
            new FakeLightingDeviceDiscovery());

        await vm.DiscoverLightingDevicesCommand.ExecuteAsync();

        var unavailable = Assert.Single(vm.LightingDevices);
        Assert.Equal("lamp-missing", unavailable.Id);
        Assert.False(unavailable.IsAvailable);
        Assert.True(vm.IsLightingDeviceSelectorVisible);
        Assert.Equal("lamp-missing", vm.SelectedLightingDeviceId);
        Assert.Equal("Сохранённое устройство недоступно или занято другим контроллером.", vm.LightingHardwareStatus);
    }

    [Fact]
    public async Task DiscoverLighting_FailureIsNonFatalAndKeepsEditableSettings()
    {
        var recorder = new OperationRecorder();
        var vm = new SettingsViewModel(
            new FakeThermalRuntime(recorder),
            new FakeStartupService(recorder),
            AppSettings.Default,
            new FakeColorPicker(null),
            new ThrowingLightingDeviceDiscovery());
        vm.ColdTemperature = 40;

        await vm.DiscoverLightingDevicesCommand.ExecuteAsync();

        Assert.Equal(40, vm.ColdTemperature);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
        Assert.Equal("Не удалось получить устройства Windows LampArray. Подсветка может быть занята другим контроллером.", vm.LightingHardwareStatus);
        Assert.Null(vm.ValidationMessage);
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public async Task Save_InvalidCrossedTemperatures_ShowsValidationAndDoesNotChangeLiveSettings()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { ColdTemperature = 65 };
        using var dashboard = new MainViewModel(runtime);
        dashboard.SynchronizeProfile(vm);

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal("Температуры должны возрастать с шагом не менее 1 °C.", vm.ValidationMessage);
        Assert.Empty(recorder.Events);
        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
        Assert.Equal(ThermalProfile.Default, dashboard.Profile);
    }

    [Fact]
    public async Task Save_InvalidCandidate_IsRejectedByRuntimeBeforeAnyStartupSideEffect()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default)
        {
            ColdTemperature = 65,
            IsAutostartEnabled = true,
        };
        using var dashboard = new MainViewModel(runtime);
        dashboard.SynchronizeProfile(vm);

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(1, runtime.UpdateCalls);
        Assert.Empty(recorder.Events);
        Assert.Equal("Температуры должны возрастать с шагом не менее 1 °C.", vm.ValidationMessage);
        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
        Assert.Equal(ThermalProfile.Default, dashboard.Profile);
        Assert.False(startup.IsEnabled);
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

        Assert.Equal(["runtime.persist", "startup:true", "vm.commit"], recorder.Events);
        Assert.True(startup.IsEnabled);
        Assert.True(vm.LiveSettings.IsAutostartEnabled);
    }

    [Fact]
    public async Task Save_RuntimeFailure_DoesNotTouchStartupAndKeepsLiveSettings()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { Failure = new InvalidOperationException("store unavailable") };
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { IsAutostartEnabled = true };

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(["runtime.fail"], recorder.Events);
        Assert.Equal(0, runtime.PersistenceCount);
        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
        Assert.False(startup.IsEnabled);
        Assert.Equal("Не удалось сохранить настройки.", vm.ValidationMessage);
    }

    [Fact]
    public async Task Save_RuntimeArgumentFailure_IsTranslatedWithoutTouchingStartup()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { Failure = new ArgumentException("write rejected") };
        var startup = new FakeStartupService(recorder);
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { IsAutostartEnabled = true };

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(["runtime.fail"], recorder.Events);
        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
        Assert.False(startup.IsEnabled);
        Assert.Equal("Проверьте параметры температурного профиля.", vm.ValidationMessage);
    }

    [Fact]
    public async Task Save_StartupAndRuntimeCompensationFailure_ShowsSafeRollbackError()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { Failure = new InvalidOperationException("rollback unavailable"), FailOnUpdateCall = 2 };
        var startup = new FakeStartupService(recorder) { FailWhenEnabled = true };
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { IsAutostartEnabled = true };

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(["runtime.persist", "startup:true", "runtime.fail"], recorder.Events);
        Assert.Equal("Не удалось изменить автозапуск. Не удалось вернуть настройки.", vm.ValidationMessage);
        Assert.True(runtime.CurrentSettings.IsAutostartEnabled);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
        Assert.False(startup.IsEnabled);
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
        Assert.Equal(["runtime.fail"], recorder.Events);
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

        Assert.Equal(["runtime.persist", "startup:true", "runtime.persist"], recorder.Events);
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
    public async Task ResetDefaults_AppliesOnlyAfterExplicitSave()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder);
        var startup = new FakeStartupService(recorder);
        var saved = AppSettings.Default with
        {
            Profile = ThermalProfile.Default with { ColdTemperature = 40 },
            NotificationsEnabled = false,
        };
        var vm = new SettingsViewModel(runtime, startup, saved);

        vm.ResetDefaultsCommand.Execute(null);

        Assert.Equal(ThermalProfile.Default, new ThermalProfile(vm.ColdTemperature, vm.ColdColor, vm.WarmTemperature, vm.WarmColor, vm.HotTemperature, vm.HotColor, vm.SmoothingSeconds));
        Assert.True(vm.NotificationsEnabled);
        Assert.Equal(0, runtime.UpdateCalls);

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(1, runtime.UpdateCalls);
        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
    }

    [Fact]
    public async Task SaveCommand_UnexpectedPreCommitFailure_DoesNotTouchStartupAndUsesCommandExceptionHandler()
    {
        var recorder = new OperationRecorder();
        var runtime = new FakeThermalRuntime(recorder) { Failure = new UnexpectedRuntimeException() };
        var startup = new FakeStartupService(recorder) { FailWhenDisabled = true };
        var vm = new SettingsViewModel(runtime, startup, AppSettings.Default) { IsAutostartEnabled = true };

        await vm.SaveCommand.ExecuteAsync();

        Assert.Equal(["runtime.fail"], recorder.Events);
        Assert.Equal(AppSettings.Default, runtime.CurrentSettings);
        Assert.Equal(AppSettings.Default, vm.LiveSettings);
        Assert.False(startup.IsEnabled);
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
        public int? FailOnUpdateCall { get; init; }
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
            settings.Validate();
            if (GateUpdates)
            {
                _updateEntered.TrySetResult();
                await _updateGate.Task;
            }

            if (Failure is { } failure && (FailOnUpdateCall is null || FailOnUpdateCall == UpdateCalls))
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

    private sealed class BlockingThermalRuntime : IThermalRuntime
    {
        private readonly TaskCompletionSource _firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeUpdates;
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged { add { } remove { } }
        public RuntimeSnapshot CurrentSnapshot { get; } = new(RuntimeStatus.Disabled, null, null, null, null, null, DateTimeOffset.MinValue);
        public AppSettings CurrentSettings { get; private set; } = AppSettings.Default;
        public List<AppSettings> PersistedCandidates { get; } = [];
        public int UpdateCalls { get; private set; }
        public int MaxConcurrentUpdates { get; private set; }
        public Task FirstUpdateEntered => _firstEntered.Task;
        public void ReleaseFirstUpdate() => _firstGate.TrySetResult();
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
        public async Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            var call = ++UpdateCalls;
            var active = Interlocked.Increment(ref _activeUpdates);
            MaxConcurrentUpdates = Math.Max(MaxConcurrentUpdates, active);
            try
            {
                settings.Validate();
                if (call == 1)
                {
                    _firstEntered.TrySetResult();
                    await _firstGate.Task;
                }

                PersistedCandidates.Add(settings);
                CurrentSettings = settings;
            }
            finally
            {
                Interlocked.Decrement(ref _activeUpdates);
            }
        }
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

    private sealed class FakeColorPicker(RgbColor? result) : IColorPickerService
    {
        public RgbColor CurrentColor { get; private set; }
        public int PickCalls { get; private set; }

        public RgbColor? Pick(RgbColor current)
        {
            PickCalls++;
            CurrentColor = current;
            return result;
        }
    }

    private sealed class FakeLightingDeviceDiscovery(params LightingDeviceInfo[] devices) : ILightingDeviceDiscovery
    {
        public Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LightingDeviceInfo>>(devices);
    }

    private sealed class ThrowingLightingDeviceDiscovery : ILightingDeviceDiscovery
    {
        public Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<LightingDeviceInfo>>(new InvalidOperationException("device occupied"));
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
