using LumaTherm.App.Composition;
using LumaTherm.Core.Diagnostics;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Sensors;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;

namespace LumaTherm.App.Tests.Composition;

public sealed class AppHostTests
{
    [Fact]
    public async Task NonOwner_OnlySignalsAndRequestsExit()
    {
        var fixture = new HostFixture(owner: false);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);

        Assert.False(await host.StartAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.Equal(["logger.create", "instance.create", "instance.acquire", "instance.signal", "instance.dispose", "logger.dispose", "exit"], fixture.Events);
        Assert.Equal(0, fixture.SentinelBegins);
    }

    [Fact]
    public async Task Owner_StartsPrerequisitesBeforeRuntime_AndUsesNvmlThenAfterburnerProductionOrder()
    {
        var fixture = new HostFixture();
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);

        Assert.True(await host.StartAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.Equal(["NVML", "MSI Afterburner"], AppServices.ProductionTemperatureSourceNames());
        Assert.Equal(
            ["logger.create", "instance.create", "instance.acquire", "sentinel.create", "sentinel.begin", "settings.create", "settings.load", "runtime.create", "startup.create", "ui.create", "ui.show", "tray.create", "power.create", "discovery.create", "discovery.start", "runtime.start"],
            fixture.Events);
    }

    [Fact]
    public async Task Autostart_SuppressesWindowButKeepsTrayDiscoveryAndRuntime()
    {
        var fixture = new HostFixture();
        await using var host = new AppHost(fixture.Services, ["--autostart", "--ignored"], fixture.RequestExit);

        Assert.True(await host.StartAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain("ui.show", fixture.Events);
        Assert.Contains("tray.create", fixture.Events);
        Assert.True(fixture.Events.IndexOf("runtime.start") > fixture.Events.IndexOf("discovery.start"));
    }

    [Fact]
    public async Task PriorCrash_PersistsModeOffBeforeUiAndRuntimeStart_AndWarnsOnce()
    {
        var fixture = new HostFixture(priorCrash: true, settings: AppSettings.Default with { IsModeEnabled = true });
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);

        Assert.True(await host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal([false], fixture.RuntimeUpdates);
        Assert.Equal(false, fixture.SettingsSeenByUi?.IsModeEnabled);
        Assert.Equal(1, fixture.RecoveryWarnings);
        Assert.True(fixture.Events.IndexOf("runtime.update:false") < fixture.Events.IndexOf("ui.create"));
        Assert.True(fixture.Events.IndexOf("runtime.update:false") < fixture.Events.IndexOf("runtime.start"));
    }

    [Fact]
    public async Task CleanEnabledRestart_StartsWithPersistedEnabledMode()
    {
        var fixture = new HostFixture(settings: AppSettings.Default with { IsModeEnabled = true });
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);

        Assert.True(await host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Empty(fixture.RuntimeUpdates);
        Assert.True(fixture.SettingsSeenByUi!.IsModeEnabled);
        Assert.Equal(1, fixture.RuntimeStarts);
    }

    [Fact]
    public async Task Stop_AttemptsEveryCleanupInRequiredOrder_AndRetainsSentinelOnFailure()
    {
        var fixture = new HostFixture(failRuntimeStop: true, failTrayStop: true);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);
        await host.StartAsync(TestContext.Current.CancellationToken);

        var failure = await Assert.ThrowsAsync<AggregateException>(() => host.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Equal(
            ["runtime.stop", "discovery.dispose", "power.dispose", "tray.dispose", "ui.dispose", "instance.dispose", "logger.dispose"],
            fixture.Events.Where(IsCleanup).ToArray());
        Assert.Equal(0, fixture.SentinelCompletes);
    }

    [Fact]
    public async Task GracefulStop_RemovesSentinelOnce_AndStopIsIdempotent()
    {
        var fixture = new HostFixture();
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await host.StopAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.RuntimeStops);
        Assert.Equal(1, fixture.SentinelCompletes);
        Assert.True(fixture.Events.IndexOf("sentinel.complete") > fixture.Events.IndexOf("instance.dispose"));
        Assert.True(fixture.Events.IndexOf("sentinel.complete") > fixture.Events.IndexOf("logger.dispose"));
    }

    [Fact]
    public async Task StartupFailure_RetainsSentinelAndCleansConstructedServices()
    {
        var fixture = new HostFixture(failDiscoveryStart: true);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, fixture.SentinelCompletes);
        Assert.Contains("runtime.stop", fixture.Events);
        Assert.Contains("instance.dispose", fixture.Events);
    }

    [Fact]
    public async Task Activation_IsDispatchedAndUsesIdempotentShowRestoreActivatePath()
    {
        var fixture = new HostFixture();
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);
        await host.StartAsync(TestContext.Current.CancellationToken);
        fixture.Instance.RaiseActivation();

        Assert.Equal(1, fixture.DispatchCalls);
        Assert.Equal(1, fixture.ActivationCalls);
    }

    [Fact]
    public async Task ConcurrentStart_CallersShareOneOwnershipAndStartupOperation()
    {
        var fixture = new HostFixture(blockAcquire: true);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);

        var first = host.StartAsync(TestContext.Current.CancellationToken);
        var second = host.StartAsync(TestContext.Current.CancellationToken);
        fixture.Instance.ReleaseAcquire();

        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.Instance.AcquireCalls);
        Assert.Equal(1, fixture.RuntimeStarts);
    }

    [Fact]
    public async Task StopDuringStart_WaitsForStartupThenPerformsOneCompleteCleanup()
    {
        var fixture = new HostFixture(blockAcquire: true);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);

        var start = host.StartAsync(TestContext.Current.CancellationToken);
        var stop = host.StopAsync(TestContext.Current.CancellationToken);
        Assert.False(stop.IsCompleted);
        fixture.Instance.ReleaseAcquire();

        Assert.True(await start.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        await stop.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(1, fixture.RuntimeStops);
        Assert.Equal(1, fixture.SentinelCompletes);
    }

    [Fact]
    public async Task UnknownArguments_AreIgnoredAndLoggedWithoutAffectingStartup()
    {
        var fixture = new HostFixture();
        await using var host = new AppHost(fixture.Services, ["--mystery", "value"], fixture.RequestExit);

        Assert.True(await host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(2, fixture.LogEvents.Count(value => value == "app.argument_ignored"));
        Assert.Contains("ui.show", fixture.Events);
    }

    [Fact]
    public async Task RuntimeSnapshots_LogStableSensorLampAndModeTransitions()
    {
        var fixture = new HostFixture();
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);
        await host.StartAsync(TestContext.Current.CancellationToken);

        fixture.Runtime.Raise(new RuntimeSnapshot(
            RuntimeStatus.Active,
            new TemperatureReading(61, "NVML", "RTX", DateTimeOffset.UtcNow),
            null,
            null,
            new LightingDeviceInfo("lamp", "LampArray", 4, true),
            null,
            DateTimeOffset.UtcNow,
            true));
        fixture.Runtime.Raise(new RuntimeSnapshot(RuntimeStatus.SensorUnavailable, null, null, null, null, "missing", DateTimeOffset.UtcNow, true));
        fixture.Runtime.Raise(new RuntimeSnapshot(RuntimeStatus.Disabled, null, null, null, null, null, DateTimeOffset.UtcNow, false));

        Assert.Contains("sensor.selected", fixture.LogEvents);
        Assert.Contains("sensor.failed", fixture.LogEvents);
        Assert.Contains("lamp.connected", fixture.LogEvents);
        Assert.Contains("lamp.disconnected", fixture.LogEvents);
        Assert.Contains("runtime.enabled", fixture.LogEvents);
        Assert.Contains("runtime.disabled", fixture.LogEvents);
    }

    [Fact]
    public async Task OwnershipFailure_CleansCoordinatorAndLoggerWithoutTouchingSentinel()
    {
        var fixture = new HostFixture(failAcquire: true);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("instance.dispose", fixture.Events);
        Assert.Contains("logger.dispose", fixture.Events);
        Assert.Equal(0, fixture.SentinelBegins);
    }

    [Fact]
    public async Task LoggerDisposalFailure_RetainsSentinelAndIsReported()
    {
        var fixture = new HostFixture(failLoggerDispose: true);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StopAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, fixture.SentinelCompletes);
    }

    private static bool IsCleanup(string value) => value.EndsWith(".dispose", StringComparison.Ordinal) || value == "runtime.stop";

    private sealed class HostFixture
    {
        private readonly FakeLogger _logger;
        private readonly FakeSentinel _sentinel;
        private readonly FakeRuntime _runtime;
        private readonly FakeUi _ui;
        private readonly FakeTray _tray;
        private readonly FakeAsyncOwned _power;
        private readonly FakeDiscovery _discovery;

        public HostFixture(
            bool owner = true,
            bool priorCrash = false,
            AppSettings? settings = null,
            bool failRuntimeStop = false,
            bool failTrayStop = false,
            bool failDiscoveryStart = false,
            bool blockAcquire = false,
            bool failAcquire = false,
            bool failLoggerDispose = false)
        {
            _logger = new FakeLogger(Events, LogEvents, failLoggerDispose);
            Instance = new FakeInstance(Events, owner, blockAcquire, failAcquire);
            _sentinel = new FakeSentinel(Events, priorCrash, () => SentinelBegins++, () => SentinelCompletes++);
            _runtime = new FakeRuntime(Events, settings ?? AppSettings.Default, () => RuntimeStarts++, () => RuntimeStops++, RuntimeUpdates, failRuntimeStop);
            _ui = new FakeUi(Events, () => ActivationCalls++);
            _tray = new FakeTray(Events, () => RecoveryWarnings++, failTrayStop);
            _power = new FakeAsyncOwned(Events, "power.dispose");
            _discovery = new FakeDiscovery(Events, failDiscoveryStart);
            var store = new FakeStore(Events, settings ?? AppSettings.Default);
            Services = new AppServices(
                () => { Events.Add("logger.create"); return _logger; },
                _ => { Events.Add("instance.create"); return Instance; },
                () => { Events.Add("sentinel.create"); return _sentinel; },
                () => { Events.Add("settings.create"); return store; },
                (loaded, _) => { Events.Add("runtime.create"); _runtime.SetInitial(loaded); return _runtime; },
                _ => { Events.Add("startup.create"); return new FakeStartup(); },
                (_, _, effective) => { Events.Add("ui.create"); SettingsSeenByUi = effective; return _ui; },
                (_, _) => { Events.Add("tray.create"); return _tray; },
                _ => { Events.Add("power.create"); return _power; },
                (_, _) => { Events.Add("discovery.create"); return _discovery; },
                action => { DispatchCalls++; action(); });
        }

        public List<string> Events { get; } = [];
        public List<string> LogEvents { get; } = [];
        public AppServices Services { get; }
        public FakeInstance Instance { get; }
        public FakeRuntime Runtime => _runtime;
        public int SentinelBegins { get; private set; }
        public int SentinelCompletes { get; private set; }
        public int RuntimeStarts { get; private set; }
        public int RuntimeStops { get; private set; }
        public List<bool> RuntimeUpdates { get; } = [];
        public AppSettings? SettingsSeenByUi { get; private set; }
        public int RecoveryWarnings { get; private set; }
        public int DispatchCalls { get; private set; }
        public int ActivationCalls { get; private set; }
        public void RequestExit() => Events.Add("exit");
    }

    private sealed class FakeLogger(List<string> events, List<string> logEvents, bool failDispose) : IAppLogger, IDisposable
    {
        public void Write(AppLogLevel level, string eventName, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null) => logEvents.Add(eventName);
        public void Dispose() { events.Add("logger.dispose"); if (failDispose) throw new InvalidOperationException("logger"); }
    }

    public sealed class FakeInstance(List<string> events, bool owner, bool blockAcquire, bool failAcquire) : IAppInstanceCoordinator
    {
        private readonly TaskCompletionSource<bool>? _acquire = blockAcquire ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        public int AcquireCalls { get; private set; }
        public event EventHandler? ActivationRequested;
        public Task<bool> TryAcquireAsync(CancellationToken cancellationToken) { AcquireCalls++; events.Add("instance.acquire"); if (failAcquire) return Task.FromException<bool>(new InvalidOperationException("acquire")); return _acquire?.Task.WaitAsync(cancellationToken) ?? Task.FromResult(owner); }
        public Task SignalActivationAsync(CancellationToken cancellationToken) { events.Add("instance.signal"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { events.Add("instance.dispose"); return ValueTask.CompletedTask; }
        public void RaiseActivation() => ActivationRequested?.Invoke(this, EventArgs.Empty);
        public void ReleaseAcquire() => _acquire?.TrySetResult(owner);
    }

    private sealed class FakeSentinel(List<string> events, bool priorCrash, Action begun, Action completed) : ISessionSentinel
    {
        public Task<bool> BeginAsync(CancellationToken cancellationToken) { events.Add("sentinel.begin"); begun(); return Task.FromResult(priorCrash); }
        public Task CompleteAsync(CancellationToken cancellationToken) { events.Add("sentinel.complete"); completed(); return Task.CompletedTask; }
    }

    private sealed class FakeStore(List<string> events, AppSettings settings) : ISettingsStore
    {
        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) { events.Add("settings.load"); return Task.FromResult(new SettingsLoadResult(settings)); }
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeRuntime(List<string> events, AppSettings initial, Action started, Action stopped, List<bool> updates, bool failStop) : IThermalRuntime
    {
        private AppSettings _settings = initial;
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged;
        public RuntimeSnapshot CurrentSnapshot => new(RuntimeStatus.Disabled, null, null, null, null, null, DateTimeOffset.UtcNow, _settings.IsModeEnabled);
        public AppSettings CurrentSettings => _settings;
        public void SetInitial(AppSettings value) => _settings = value;
        public Task StartAsync(CancellationToken cancellationToken) { events.Add("runtime.start"); started(); return Task.CompletedTask; }
        public Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken) { _settings = settings; updates.Add(settings.IsModeEnabled); events.Add($"runtime.update:{settings.IsModeEnabled.ToString().ToLowerInvariant()}"); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { events.Add("runtime.stop"); stopped(); return failStop ? Task.FromException(new InvalidOperationException("runtime")) : Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Raise(RuntimeSnapshot snapshot) => SnapshotChanged?.Invoke(this, snapshot);
    }

    private sealed class FakeStartup : IStartupService
    {
        public Task<bool> GetEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeUi(List<string> events, Action activated) : IAppUiSession
    {
        public void Show() => events.Add("ui.show");
        public void ShowRestoreActivate() => activated();
        public void ShowForegroundError(string message) { }
        public void Dispose() => events.Add("ui.dispose");
    }

    private sealed class FakeTray(List<string> events, Action warned, bool failDispose) : IAppTraySession
    {
        public void ShowRecoveryWarning(string message) => warned();
        public void ShowBackgroundError(string message) { }
        public ValueTask DisposeAsync() { events.Add("tray.dispose"); return failDispose ? ValueTask.FromException(new InvalidOperationException("tray")) : ValueTask.CompletedTask; }
    }

    private sealed class FakeAsyncOwned(List<string> events, string name) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { events.Add(name); return ValueTask.CompletedTask; }
    }

    private sealed class FakeDiscovery(List<string> events, bool failStart) : IAppDiscoverySession
    {
        public Task StartAsync(CancellationToken cancellationToken) { events.Add("discovery.start"); return failStart ? Task.FromException(new InvalidOperationException("discovery")) : Task.CompletedTask; }
        public ValueTask DisposeAsync() { events.Add("discovery.dispose"); return ValueTask.CompletedTask; }
    }
}
