using LumaTherm.App.Composition;
using LumaTherm.Core.Diagnostics;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Sensors;
using LumaTherm.Core.Settings;
using LumaTherm.Core.System;
using System.Windows.Threading;

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
        Assert.True(fixture.Events.IndexOf("runtime.mode:false") < fixture.Events.IndexOf("ui.create"));
        Assert.True(fixture.Events.IndexOf("runtime.mode:false") < fixture.Events.IndexOf("runtime.start"));
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
            ["runtime.stop", "discovery.dispose", "power.dispose", "tray.dispose", "ui.dispose", "logger.dispose", "instance.dispose"],
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
        Assert.True(fixture.Events.IndexOf("sentinel.complete") < fixture.Events.IndexOf("logger.dispose"));
        Assert.True(fixture.Events.IndexOf("logger.dispose") < fixture.Events.IndexOf("instance.dispose"));
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
        var dispatchesBeforeActivation = fixture.DispatchCalls;
        fixture.Instance.RaiseActivation();

        Assert.Equal(dispatchesBeforeActivation + 1, fixture.DispatchCalls);
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
    public async Task StopDuringStart_CancelsStartupAndCompletesWithoutWaitingForOwnership()
    {
        var fixture = new HostFixture(blockAcquire: true);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);

        var start = host.StartAsync(TestContext.Current.CancellationToken);
        var stop = host.StopAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await stop.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(0, fixture.RuntimeStops);
        Assert.Equal(0, fixture.SentinelCompletes);
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

        Assert.Equal(1, fixture.SentinelCompletes);
        Assert.Equal(2, fixture.SentinelBegins);
        Assert.True(fixture.SentinelMarkerPresent);
    }

    [Fact]
    public async Task SentinelFinalizationFailure_LogsIncompleteBeforeLoggerDisposeAndRetainsMarker()
    {
        var fixture = new HostFixture(failSentinelComplete: true);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StopAsync(TestContext.Current.CancellationToken));

        Assert.True(fixture.SentinelMarkerPresent);
        Assert.Equal(0, fixture.SentinelCompletes);
        Assert.True(fixture.StopTimeline.IndexOf("log:app.stop:LumaTherm stop was incomplete.") < fixture.StopTimeline.IndexOf("logger.dispose"));
    }

    [Fact]
    public async Task UiFactoriesAndShow_RunOnRealDispatcherAfterAsynchronousPrerequisites()
    {
        using var dispatcherHost = new StaDispatcherHost();
        var fixture = new HostFixture(
            priorCrash: true,
            dispatcher: new WpfAppDispatcher(dispatcherHost.Dispatcher),
            asynchronousPrerequisites: true,
            expectedUiThreadId: dispatcherHost.ThreadId);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);

        Assert.True(await host.StartAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        await host.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.All(fixture.UiThreadIds, id => Assert.Equal(dispatcherHost.ThreadId, id));
        Assert.Equal(8, fixture.UiThreadIds.Count);
    }

    [Fact]
    public async Task Stop_KeepsOwnershipThroughSentinelDeletionAndNeverDeletesTheNextOwnersMarker()
    {
        var fixture = new HostFixture(blockSentinelComplete: true, beginNewSentinelOnInstanceDispose: true);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);
        await host.StartAsync(TestContext.Current.CancellationToken);

        var stop = host.StopAsync(TestContext.Current.CancellationToken);
        await fixture.SentinelCompleteEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var ownershipWasRetained = !fixture.Instance.IsDisposed;
        fixture.ReleaseSentinelComplete();
        await stop.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.True(ownershipWasRetained);
        Assert.True(fixture.Instance.IsDisposed);
        Assert.Equal(2, fixture.SentinelBegins);
        Assert.True(fixture.SentinelMarkerPresent);
        Assert.True(fixture.Events.LastIndexOf("sentinel.begin") > fixture.Events.IndexOf("instance.dispose"));
    }

    [Fact]
    public async Task SuccessfulStop_LogsSuccessAfterSentinelFinalizationBeforeLoggerDispose()
    {
        var fixture = new HostFixture();
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);
        await host.StartAsync(TestContext.Current.CancellationToken);

        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(fixture.StopTimeline.IndexOf("sentinel.complete") < fixture.StopTimeline.IndexOf("log:app.stop:LumaTherm stopped."));
        Assert.True(fixture.StopTimeline.IndexOf("log:app.stop:LumaTherm stopped.") < fixture.StopTimeline.IndexOf("logger.dispose"));
    }

    [Fact]
    public async Task QueuedShowAfterStop_DoesNotTouchDisposedUi()
    {
        var dispatcher = new QueueOnceDispatcher();
        var fixture = new HostFixture(dispatcher: dispatcher);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);
        await host.StartAsync(TestContext.Current.CancellationToken);
        dispatcher.QueueNext = true;

        fixture.Instance.RaiseActivation();
        await host.StopAsync(TestContext.Current.CancellationToken);
        dispatcher.RunQueued();

        Assert.Equal(0, fixture.ActivationCalls);
    }

    [Fact]
    public async Task ActivationDispatchFailure_DoesNotEscapeCoordinatorCallback()
    {
        var dispatcher = new ThrowNextDispatcher();
        var fixture = new HostFixture(dispatcher: dispatcher);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);
        await host.StartAsync(TestContext.Current.CancellationToken);
        dispatcher.ThrowNext = true;

        var escaped = Record.Exception(fixture.Instance.RaiseActivation);

        Assert.Null(escaped);
        Assert.Equal(0, fixture.ActivationCalls);
    }

    [Fact]
    public async Task Stop_CancelsBlockedOwnershipAndCompletesSafetyCleanup()
    {
        var fixture = new HostFixture(blockAcquire: true);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);
        var start = host.StartAsync(TestContext.Current.CancellationToken);

        var stop = host.StopAsync(TestContext.Current.CancellationToken);
        var completed = await Task.WhenAny(stop, Task.Delay(250, TestContext.Current.CancellationToken));
        if (!ReferenceEquals(stop, completed)) fixture.Instance.ReleaseAcquire();

        Assert.Same(stop, completed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await stop;
    }

    [Fact]
    public async Task Stop_CancelsBlockedDiscoveryAndCompletesWithinDeadline()
    {
        var fixture = new HostFixture(blockDiscoveryStart: true);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);
        var start = host.StartAsync(TestContext.Current.CancellationToken);
        await fixture.DiscoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var stop = host.StopAsync(TestContext.Current.CancellationToken);

        await stop.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.Equal(1, fixture.RuntimeStops);
    }

    [Fact]
    public async Task Stop_WhenLightingTestUiCleanupIsBlocked_RetainsOwnershipUntilCleanupCompletes()
    {
        var fixture = new HostFixture(blockUiDispose: true);
        await using var host = new AppHost(fixture.Services, [], fixture.RequestExit);
        Assert.True(await host.StartAsync(TestContext.Current.CancellationToken));

        var stop = host.StopAsync(TestContext.Current.CancellationToken);
        await fixture.UiDisposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.False(stop.IsCompleted);
        Assert.Same(fixture.Ui, host.Ui);
        Assert.False(fixture.Ui.CleanupCompleted);
        fixture.ReleaseUiDispose();
        await stop.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Null(host.Ui);
        Assert.True(fixture.Ui.CleanupCompleted);
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
            bool failLoggerDispose = false,
            IAppDispatcher? dispatcher = null,
            bool asynchronousPrerequisites = false,
            int? expectedUiThreadId = null,
            bool blockSentinelComplete = false,
            bool blockDiscoveryStart = false,
            bool failSentinelComplete = false,
            bool beginNewSentinelOnInstanceDispose = false,
            bool blockUiDispose = false)
        {
            _logger = new FakeLogger(Events, LogEvents, StopTimeline, failLoggerDispose);
            var sentinelGate = blockSentinelComplete ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
            SentinelCompleteEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _sentinel = new FakeSentinel(Events, priorCrash, () => SentinelBegins++, () => { SentinelCompletes++; StopTimeline.Add("sentinel.complete"); }, SentinelCompleteEntered, sentinelGate, failSentinelComplete);
            Instance = new FakeInstance(
                Events,
                owner,
                blockAcquire,
                failAcquire,
                asynchronousPrerequisites,
                () => { if (beginNewSentinelOnInstanceDispose) _sentinel.BeginAsync(CancellationToken.None).GetAwaiter().GetResult(); });
            _sentinelGate = sentinelGate;
            _runtime = new FakeRuntime(Events, settings ?? AppSettings.Default, () => RuntimeStarts++, () => RuntimeStops++, RuntimeUpdates, failRuntimeStop);
            UiDisposeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _uiDisposeGate = blockUiDispose ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
            _ui = new FakeUi(Events, () => ActivationCalls++, RecordUiThread, RecordUiThread, UiDisposeStarted, _uiDisposeGate);
            _tray = new FakeTray(Events, () => { RecoveryWarnings++; RecordUiThread(); }, failTrayStop, RecordUiThread);
            _power = new FakeAsyncOwned(Events, "power.dispose");
            DiscoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _discovery = new FakeDiscovery(Events, failDiscoveryStart, blockDiscoveryStart, DiscoveryStarted);
            var store = new FakeStore(Events, settings ?? AppSettings.Default, asynchronousPrerequisites);
            Services = new AppServices(
                () => { Events.Add("logger.create"); return _logger; },
                _ => { Events.Add("instance.create"); return Instance; },
                () => { Events.Add("sentinel.create"); return _sentinel; },
                () => { Events.Add("settings.create"); return store; },
                (loaded, _) => { Events.Add("runtime.create"); _runtime.SetInitial(loaded); return _runtime; },
                _ => { Events.Add("startup.create"); return new FakeStartup(); },
                (_, _, effective) => { RecordUi("ui.create"); SettingsSeenByUi = effective; return _ui; },
                (_, _) => { RecordUi("tray.create"); return _tray; },
                _ => { RecordUi("power.create"); return _power; },
                (_, _) => { RecordUi("discovery.create"); return _discovery; },
                dispatcher ?? new InlineAppDispatcher(action => { DispatchCalls++; action(); }));

            void RecordUi(string name)
            {
                Events.Add(name);
                RecordUiThread();
            }

            void RecordUiThread()
            {
                if (expectedUiThreadId is not null) UiThreadIds.Add(Environment.CurrentManagedThreadId);
            }
        }

        public List<string> Events { get; } = [];
        public List<string> LogEvents { get; } = [];
        public List<string> StopTimeline { get; } = [];
        public AppServices Services { get; }
        public FakeInstance Instance { get; }
        public FakeRuntime Runtime => _runtime;
        public int SentinelBegins { get; private set; }
        public int SentinelCompletes { get; private set; }
        public FakeUi Ui => _ui;
        public bool SentinelMarkerPresent => _sentinel.MarkerPresent;
        public int RuntimeStarts { get; private set; }
        public int RuntimeStops { get; private set; }
        public List<bool> RuntimeUpdates { get; } = [];
        public AppSettings? SettingsSeenByUi { get; private set; }
        public int RecoveryWarnings { get; private set; }
        public int DispatchCalls { get; private set; }
        public int ActivationCalls { get; private set; }
        public List<int> UiThreadIds { get; } = [];
        private readonly TaskCompletionSource? _sentinelGate;
        public TaskCompletionSource SentinelCompleteEntered { get; }
        public void ReleaseSentinelComplete() => _sentinelGate?.TrySetResult();
        private readonly TaskCompletionSource? _uiDisposeGate;
        public TaskCompletionSource UiDisposeStarted { get; }
        public void ReleaseUiDispose() => _uiDisposeGate?.TrySetResult();
        public TaskCompletionSource DiscoveryStarted { get; }
        public void RequestExit() => Events.Add("exit");
    }

    private sealed class FakeLogger(List<string> events, List<string> logEvents, List<string> stopTimeline, bool failDispose) : IAppLogger, IDisposable
    {
        public void Write(AppLogLevel level, string eventName, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? data = null) { logEvents.Add(eventName); if (eventName == "app.stop") stopTimeline.Add($"log:{eventName}:{message}"); }
        public void Dispose() { events.Add("logger.dispose"); stopTimeline.Add("logger.dispose"); if (failDispose) throw new InvalidOperationException("logger"); }
    }

    public sealed class FakeInstance(List<string> events, bool owner, bool blockAcquire, bool failAcquire, bool asynchronous, Action disposed) : IAppInstanceCoordinator
    {
        private readonly TaskCompletionSource<bool>? _acquire = blockAcquire ? new(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        public int AcquireCalls { get; private set; }
        public bool IsDisposed { get; private set; }
        public event EventHandler? ActivationRequested;
        public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken) { AcquireCalls++; events.Add("instance.acquire"); if (failAcquire) throw new InvalidOperationException("acquire"); if (_acquire is not null) return await _acquire.Task.WaitAsync(cancellationToken); if (asynchronous) await Task.Delay(10, cancellationToken).ConfigureAwait(false); return owner; }
        public Task SignalActivationAsync(CancellationToken cancellationToken) { events.Add("instance.signal"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { events.Add("instance.dispose"); IsDisposed = true; disposed(); return ValueTask.CompletedTask; }
        public void RaiseActivation() => ActivationRequested?.Invoke(this, EventArgs.Empty);
        public void ReleaseAcquire() => _acquire?.TrySetResult(owner);
    }

    private sealed class FakeSentinel(List<string> events, bool priorCrash, Action begun, Action completed, TaskCompletionSource completeEntered, TaskCompletionSource? completeGate, bool failComplete) : ISessionSentinel
    {
        public bool MarkerPresent { get; private set; }
        public Task<bool> BeginAsync(CancellationToken cancellationToken) { events.Add("sentinel.begin"); MarkerPresent = true; begun(); return Task.FromResult(priorCrash); }
        public async Task CompleteAsync(CancellationToken cancellationToken) { events.Add("sentinel.complete"); completeEntered.TrySetResult(); if (completeGate is not null) await completeGate.Task.WaitAsync(cancellationToken); if (failComplete) throw new InvalidOperationException("sentinel"); MarkerPresent = false; completed(); }
    }

    private sealed class FakeStore(List<string> events, AppSettings settings, bool asynchronous) : ISettingsStore
    {
        public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) { events.Add("settings.load"); if (asynchronous) await Task.Delay(10, cancellationToken).ConfigureAwait(false); return new SettingsLoadResult(settings); }
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
        public Task UpdatePreferencesAsync(AppSettings settings, CancellationToken cancellationToken) { _settings = settings with { IsModeEnabled = _settings.IsModeEnabled }; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) { events.Add("runtime.stop"); stopped(); return failStop ? Task.FromException(new InvalidOperationException("runtime")) : Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken) { _settings = _settings with { IsModeEnabled = enabled }; updates.Add(enabled); events.Add($"runtime.mode:{enabled.ToString().ToLowerInvariant()}"); return Task.CompletedTask; }
        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Raise(RuntimeSnapshot snapshot) => SnapshotChanged?.Invoke(this, snapshot);
    }

    private sealed class FakeStartup : IStartupService
    {
        public Task<bool> GetEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class FakeUi(
        List<string> events,
        Action activated,
        Action shown,
        Action disposed,
        TaskCompletionSource disposeStarted,
        TaskCompletionSource? disposeGate) : IAppUiSession
    {
        public bool CleanupCompleted { get; private set; }
        public void Show() { events.Add("ui.show"); shown(); }
        public void ShowRestoreActivate() => activated();
        public void ShowForegroundError(string message) { }
        public async ValueTask DisposeAsync()
        {
            events.Add("ui.dispose"); disposed(); disposeStarted.TrySetResult();
            if (disposeGate is not null) await disposeGate.Task;
            CleanupCompleted = true;
        }
    }

    private sealed class FakeTray(List<string> events, Action warned, bool failDispose, Action disposed) : IAppTraySession
    {
        public void ShowRecoveryWarning(string message) => warned();
        public void ShowBackgroundError(string message) { }
        public ValueTask DisposeAsync() { events.Add("tray.dispose"); disposed(); return failDispose ? ValueTask.FromException(new InvalidOperationException("tray")) : ValueTask.CompletedTask; }
    }

    private sealed class FakeAsyncOwned(List<string> events, string name) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { events.Add(name); return ValueTask.CompletedTask; }
    }

    private sealed class FakeDiscovery(List<string> events, bool failStart, bool blockStart, TaskCompletionSource started) : IAppDiscoverySession
    {
        public async Task StartAsync(CancellationToken cancellationToken) { events.Add("discovery.start"); started.TrySetResult(); if (failStart) throw new InvalidOperationException("discovery"); if (blockStart) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        public ValueTask DisposeAsync() { events.Add("discovery.dispose"); return ValueTask.CompletedTask; }
    }

    private sealed class StaDispatcherHost : IDisposable
    {
        private readonly Thread _thread;
        public StaDispatcherHost()
        {
            var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread = new Thread(() =>
            {
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            Dispatcher = ready.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            ThreadId = _thread.ManagedThreadId;
        }
        public Dispatcher Dispatcher { get; }
        public int ThreadId { get; }
        public void Dispose()
        {
            Dispatcher.InvokeShutdown();
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private sealed class QueueOnceDispatcher : IAppDispatcher
    {
        private Action? _queued;
        public bool QueueNext { get; set; }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (QueueNext)
            {
                QueueNext = false;
                _queued = action;
                return Task.CompletedTask;
            }
            action();
            return Task.CompletedTask;
        }
        public void RunQueued() { var action = _queued; _queued = null; action?.Invoke(); }
    }

    private sealed class ThrowNextDispatcher : IAppDispatcher
    {
        public bool ThrowNext { get; set; }
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            if (ThrowNext)
            {
                ThrowNext = false;
                throw new InvalidOperationException("dispatch");
            }
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }
}
