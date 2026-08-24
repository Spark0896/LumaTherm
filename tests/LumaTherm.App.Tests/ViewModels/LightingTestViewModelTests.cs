#pragma warning disable xUnit1051 // Lifecycle tests intentionally exercise default and independently cancelled tokens.

using System.IO;
using LumaTherm.App.ViewModels;
using LumaTherm.Core.Colors;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;

namespace LumaTherm.App.Tests.ViewModels;

public sealed class LightingTestViewModelTests
{
    [Fact]
    public async Task OpenAsync_WhenCalledTwice_BeginsExactlyOneSession()
    {
        var runtime = new FakeRuntime();
        var vm = CreateViewModel(runtime);

        await vm.OpenAsync();
        await vm.OpenAsync();

        Assert.Equal(1, runtime.BeginCalls);
        await vm.CloseAsync();
    }

    [Fact]
    public async Task TestTemperature_SendsSelectedValueAndRecomputesPreviewFromDraft()
    {
        var runtime = new FakeRuntime();
        var vm = CreateViewModel(runtime);
        await vm.OpenAsync();
        vm.Editor.Points[1].Color = new RgbColor(12, 220, 80);

        vm.TestTemperature = vm.Editor.Points[1].Temperature;
        await vm.TemperatureUpdate;

        Assert.Equal(vm.Editor.Points[1].Temperature, runtime.Session.LastTemperature);
        Assert.Equal(new RgbColor(12, 220, 80), vm.PreviewColor);
        await vm.CloseAsync();
    }

    [Fact]
    public async Task TestTemperature_DoesNotPersistPreferences()
    {
        var runtime = new FakeRuntime();
        var saveCalls = 0;
        var vm = CreateViewModel(runtime, (_, _) => { saveCalls++; return Task.CompletedTask; });
        await vm.OpenAsync();

        vm.TestTemperature = 72;
        await vm.TemperatureUpdate;

        Assert.Equal(0, saveCalls);
        Assert.Equal(0, runtime.PreferenceUpdates);
        await vm.CloseAsync();
    }

    [Fact]
    public async Task CancelAsync_DiscardsDraftAndDisposesSession()
    {
        var original = ThermalProfile.Default;
        ThermalProfile? saved = null;
        var runtime = new FakeRuntime();
        var vm = CreateViewModel(runtime, (profile, _) => { saved = profile; return Task.CompletedTask; });
        await vm.OpenAsync();
        vm.Editor.Points[1].Color = new RgbColor(1, 2, 3);

        await vm.CancelAsync();

        Assert.True(runtime.Session.Disposed);
        Assert.Null(saved);
        Assert.Equal(original, runtime.CurrentSettings.Profile);
    }

    [Fact]
    public async Task ApplyAsync_PersistsBuiltDraftThenDisposesSession()
    {
        ThermalProfile? saved = null;
        var runtime = new FakeRuntime();
        var vm = CreateViewModel(runtime, (profile, _) => { saved = profile; runtime.Events.Add("save"); return Task.CompletedTask; });
        await vm.OpenAsync();
        vm.Editor.Points[1].Color = new RgbColor(4, 5, 6);

        await vm.ApplyAsync();

        Assert.NotNull(saved);
        Assert.Equal(new RgbColor(4, 5, 6), saved.Points[1].Color);
        Assert.True(runtime.Session.Disposed);
        Assert.Equal(["save", "dispose"], runtime.Events);
    }

    [Fact]
    public async Task CancelAsync_WhenModeWasOff_LeavesModeOffAfterSessionRestoration()
    {
        var runtime = new FakeRuntime(AppSettings.Default with { IsModeEnabled = false });
        var vm = CreateViewModel(runtime);
        await vm.OpenAsync();
        vm.TestTemperature = 85;
        await vm.TemperatureUpdate;

        await vm.CancelAsync();

        Assert.False(runtime.CurrentSettings.IsModeEnabled);
        Assert.Equal(0, runtime.ModeChanges);
        Assert.True(runtime.Session.Disposed);
    }

    [Fact]
    public async Task SetTemperatureFailure_IsReportedAndDoesNotPreventCleanup()
    {
        var runtime = new FakeRuntime();
        var vm = CreateViewModel(runtime);
        await vm.OpenAsync();
        runtime.Session.SetFailure = new InvalidOperationException("lamp unavailable");

        vm.TestTemperature = 91;
        await vm.TemperatureUpdate;
        await vm.CloseAsync();

        Assert.Contains("lamp unavailable", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.True(runtime.Session.Disposed);
    }

    [Fact]
    public async Task CloseAsync_WhenCalledTwice_DisposesSessionOnce()
    {
        var runtime = new FakeRuntime();
        var vm = CreateViewModel(runtime);
        await vm.OpenAsync();

        await Task.WhenAll(vm.CloseAsync(), vm.CloseAsync());

        Assert.Equal(1, runtime.Session.DisposeCalls);
    }

    [Fact]
    public async Task OpenAsync_WhenBeginFails_PropagatesAndLeavesNothingToDispose()
    {
        var runtime = new FakeRuntime { BeginFailure = new InvalidOperationException("busy") };
        var vm = CreateViewModel(runtime);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.OpenAsync());
        await vm.CloseAsync();

        Assert.Equal("busy", failure.Message);
        Assert.Equal(1, runtime.BeginCalls);
        Assert.Equal(0, runtime.Session.DisposeCalls);
    }

    [Fact]
    public async Task ApplyAsync_WhenSaveFails_PropagatesAfterDisposingSession()
    {
        var runtime = new FakeRuntime();
        var vm = CreateViewModel(runtime, (_, _) => Task.FromException(new IOException("disk full")));
        await vm.OpenAsync();

        var failure = await Assert.ThrowsAsync<IOException>(() => vm.ApplyAsync());

        Assert.Equal("disk full", failure.Message);
        Assert.True(runtime.Session.Disposed);
    }

    [Fact]
    public async Task OpenAsync_WhenCancelled_DoesNotOwnSession()
    {
        var runtime = new FakeRuntime();
        var vm = CreateViewModel(runtime);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.OpenAsync(cancellation.Token));
        await vm.CloseAsync();

        Assert.Equal(0, runtime.Session.DisposeCalls);
    }

    [Fact]
    public async Task CloseAsync_WaitsForInFlightTemperatureUpdateBeforeDisposal()
    {
        var runtime = new FakeRuntime();
        var vm = CreateViewModel(runtime);
        await vm.OpenAsync();
        runtime.Session.BlockTemperatureUpdates = true;
        vm.TestTemperature = 77;

        var close = vm.CloseAsync();
        Assert.False(close.IsCompleted);
        Assert.False(runtime.Session.Disposed);

        runtime.Session.CompleteTemperatureUpdate();
        await close;
        Assert.True(runtime.Session.Disposed);
    }

    [Fact]
    public async Task CloseAsync_WhileBeginIsBlocked_WaitsForLateSessionDisposal()
    {
        var runtime = new FakeRuntime(blockBegin: true);
        var vm = CreateViewModel(runtime);
        var open = vm.OpenAsync();
        await runtime.BeginEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var close = vm.CloseAsync();

        Assert.False(close.IsCompleted);
        Assert.False(runtime.Session.Disposed);
        runtime.CompleteBegin();
        await Task.WhenAll(open, close).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.True(runtime.Session.Disposed);
        Assert.Equal(1, runtime.Session.DisposeCalls);
    }

    [Fact]
    public async Task ConcurrentDoubleOpenAndClose_SharesBeginAndLateDisposalBarrier()
    {
        var runtime = new FakeRuntime(blockBegin: true);
        var vm = CreateViewModel(runtime);
        var firstOpen = vm.OpenAsync();
        await runtime.BeginEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var secondOpen = vm.OpenAsync();

        var firstClose = vm.CloseAsync();
        var secondClose = vm.CloseAsync();

        Assert.False(firstClose.IsCompleted);
        Assert.Same(firstClose, secondClose);
        Assert.Equal(1, runtime.BeginCalls);
        runtime.CompleteBegin();
        await Task.WhenAll(firstOpen, firstClose, secondClose)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => secondOpen)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(1, runtime.BeginCalls);
        Assert.Equal(1, runtime.Session.DisposeCalls);
    }

    [Fact]
    public async Task LightingTestApply_CommitsOneAuthoritativeProfileAcrossSettingsRuntimeAndDashboard()
    {
        var runtime = new FakeRuntime();
        using var settings = new SettingsViewModel(runtime, new FakeStartupService(), AppSettings.Default);
        using var dashboard = new MainViewModel(runtime);
        dashboard.SynchronizeProfile(settings);
        var applied = ThermalProfile.Create(
        [
            new(10, new RgbColor(1, 2, 3)),
            new(72, new RgbColor(4, 5, 6)),
            new(110, new RgbColor(7, 8, 9)),
        ], 1.4);

        await settings.ApplyLightingTestProfileAsync(applied, TestContext.Current.CancellationToken);

        Assert.Equal(applied, runtime.CurrentSettings.Profile);
        Assert.Equal(applied, settings.LiveSettings.Profile);
        Assert.Equal(applied, settings.ProfileEditor.BuildProfile(settings.SmoothingSeconds));
        Assert.Equal(applied, dashboard.Profile);
        var reopened = new LightingTestViewModel(
            runtime,
            settings.ProfileEditor.BuildProfile(settings.SmoothingSeconds),
            (_, _) => Task.CompletedTask);
        Assert.Equal(applied, reopened.Editor.BuildProfile(reopened.SmoothingSeconds));
        await reopened.CloseAsync();
    }

    [Fact]
    public async Task SettingsSave_AfterLightingTestApply_CannotOverwriteAppliedProfile()
    {
        var runtime = new FakeRuntime();
        using var settings = new SettingsViewModel(runtime, new FakeStartupService(), AppSettings.Default);
        var applied = ThermalProfile.Create(
        [
            new(5, new RgbColor(10, 20, 30)),
            new(75, new RgbColor(40, 50, 60)),
            new(115, new RgbColor(70, 80, 90)),
        ], 1.2);
        await settings.ApplyLightingTestProfileAsync(applied, TestContext.Current.CancellationToken);
        settings.NotificationsEnabled = false;

        await settings.SaveCommand.ExecuteAsync();

        Assert.Equal(applied, runtime.CurrentSettings.Profile);
        Assert.Equal(applied, settings.LiveSettings.Profile);
        Assert.False(runtime.CurrentSettings.NotificationsEnabled);
    }

    private static LightingTestViewModel CreateViewModel(
        FakeRuntime runtime,
        Func<ThermalProfile, CancellationToken, Task>? save = null) =>
        new(runtime, ThermalProfile.Default, save ?? ((_, _) => Task.CompletedTask));

    private sealed class FakeRuntime : IThermalRuntime
    {
        private readonly TaskCompletionSource<ILightingTestSession>? _beginGate;

        public FakeRuntime(AppSettings? settings = null, bool blockBegin = false)
        {
            CurrentSettings = settings ?? AppSettings.Default;
            Session = new FakeSession(Events);
            _beginGate = blockBegin ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        }

        public event EventHandler<RuntimeSnapshot>? SnapshotChanged { add { } remove { } }
        public RuntimeSnapshot CurrentSnapshot { get; } = new(RuntimeStatus.Disabled, null, null, null, null, null, DateTimeOffset.MinValue, false);
        public TaskCompletionSource BeginEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AppSettings CurrentSettings { get; private set; }
        public FakeSession Session { get; }
        public List<string> Events { get; } = [];
        public Exception? BeginFailure { get; init; }
        public int BeginCalls { get; private set; }
        public int PreferenceUpdates { get; private set; }
        public int ModeChanges { get; private set; }

        public async Task<ILightingTestSession> BeginLightingTestAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeginCalls++;
            BeginEntered.TrySetResult();
            if (BeginFailure is not null)
            {
                throw BeginFailure;
            }

            return _beginGate is null
                ? Session
                : await _beginGate.Task.WaitAsync(cancellationToken);
        }

        public void CompleteBegin() => _beginGate?.TrySetResult(Session);

        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            ModeChanges++;
            CurrentSettings = CurrentSettings with { IsModeEnabled = enabled };
            return Task.CompletedTask;
        }

        public Task UpdatePreferencesAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            PreferenceUpdates++;
            CurrentSettings = settings with { IsModeEnabled = CurrentSettings.IsModeEnabled };
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSession(List<string> events) : ILightingTestSession
    {
        private TaskCompletionSource? _temperatureUpdate;
        public double? LastTemperature { get; private set; }
        public Exception? SetFailure { get; set; }
        public bool BlockTemperatureUpdates { get; set; }
        public int DisposeCalls { get; private set; }
        public bool Disposed => DisposeCalls > 0;

        public Task SetTemperatureAsync(double celsius, CancellationToken cancellationToken)
        {
            LastTemperature = celsius;
            if (SetFailure is not null) return Task.FromException(SetFailure);
            if (!BlockTemperatureUpdates) return Task.CompletedTask;
            _temperatureUpdate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _temperatureUpdate.Task;
        }

        public void CompleteTemperatureUpdate() => _temperatureUpdate?.TrySetResult();

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            events.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeStartupService : IStartupService
    {
        public Task<bool> GetEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

#pragma warning restore xUnit1051
