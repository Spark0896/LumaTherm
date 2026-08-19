using LumaTherm.Core.Colors;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Sensors;
using LumaTherm.Core.Settings;

namespace LumaTherm.Core.Tests.Runtime;

public sealed class ThermalRuntimeTests
{
    [Fact]
    public async Task DisabledMode_DoesNotReadOrWrite()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: false, temperatures: [68]);

        var snapshot = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(RuntimeStatus.Disabled, snapshot.Status);
        Assert.Equal(0, fixture.Temperatures.ReadCalls);
        Assert.Empty(fixture.Lighting.Colors);
    }

    [Fact]
    public async Task ValidReading_WhenEnabled_WritesCalculatedColorAndRange()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);

        var snapshot = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(RuntimeStatus.Active, snapshot.Status);
        Assert.Single(fixture.Lighting.Colors);
        Assert.Equal(fixture.Lighting.Colors[0], snapshot.Color);
        Assert.Equal(68, snapshot.Temperature!.Celsius);
        Assert.Equal(ThermalRange.Warm, snapshot.Range);
        Assert.True(snapshot.IsModeEnabled);
    }

    [Fact]
    public async Task SuspendedSnapshots_ExposeAuthoritativeModeBit()
    {
        await using var disabled = RuntimeFixture.Create(modeEnabled: false, temperatures: []);
        await disabled.Runtime.SuspendAsync(CancellationToken.None);
        Assert.False(disabled.Runtime.CurrentSnapshot.IsModeEnabled);

        await using var enabled = RuntimeFixture.Create(modeEnabled: true, temperatures: []);
        await enabled.Runtime.SuspendAsync(CancellationToken.None);
        Assert.True(enabled.Runtime.CurrentSnapshot.IsModeEnabled);

        await enabled.Runtime.UpdateSettingsAsync(enabled.SettingsStore.Initial with { NotificationsEnabled = false }, CancellationToken.None);
        Assert.True(enabled.Runtime.CurrentSnapshot.IsModeEnabled);

        await enabled.Runtime.UpdateSettingsAsync(enabled.SettingsStore.Initial with { IsModeEnabled = false }, CancellationToken.None);
        Assert.False(enabled.Runtime.CurrentSnapshot.IsModeEnabled);
    }

    [Fact]
    public async Task UpdateSettings_WhenSnapshotSubscriberThrows_KeepsCommittedSettingsAndDoesNotThrow()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: false, temperatures: []);
        fixture.Runtime.SnapshotChanged += (_, _) => throw new InvalidOperationException("subscriber failed");
        var candidate = fixture.SettingsStore.Initial with { NotificationsEnabled = false };

        await fixture.Runtime.UpdateSettingsAsync(candidate, CancellationToken.None);

        Assert.Equal(candidate, fixture.Runtime.CurrentSettings);
        Assert.Equal(candidate, fixture.SettingsStore.Saved);
    }

    [Fact]
    public async Task OneSecondRamp_ProducesTenGradualWritesBetweenSamples()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [35, 85, 85]);
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        for (var tick = 0; tick < 4; tick++)
        {
            fixture.Advance(TimeSpan.FromMilliseconds(100));
            await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        }

        for (var tick = 0; tick < 10; tick++)
        {
            fixture.Advance(TimeSpan.FromMilliseconds(100));
            await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        }

        var ramp = fixture.Lighting.Colors.Skip(1).ToArray();
        Assert.InRange(ramp.Length, 5, 10);
        Assert.True(ramp.Distinct().Count() >= 5);
        Assert.DoesNotContain(new RgbColor(0xFF, 0x56, 0x5D), ramp);
        Assert.Equal(3, fixture.Temperatures.ReadCalls);
    }

    [Fact]
    public async Task DuplicateColors_AreSuppressed()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [35, 35, 35]);

        for (var tick = 0; tick < 11; tick++)
        {
            await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
            fixture.Advance(TimeSpan.FromMilliseconds(100));
        }

        Assert.Single(fixture.Lighting.Colors);
        Assert.Equal(3, fixture.Temperatures.ReadCalls);
    }

    [Fact]
    public async Task MissingReading_UsesExactBackoffAndReleasesOnceAtFiveSeconds()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68, null, null, null, null]);
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromMilliseconds(500));
        var holding = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);

        fixture.Advance(TimeSpan.FromMilliseconds(999));
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromMilliseconds(1));
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromSeconds(2));
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromSeconds(2));
        var released = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromSeconds(3));
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromSeconds(10));
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromSeconds(15));
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(RuntimeStatus.HoldingLastColor, holding.Status);
        Assert.Equal(RuntimeStatus.SensorUnavailable, released.Status);
        Assert.Equal(1, fixture.Lighting.ReleaseCalls);
        Assert.Single(fixture.Lighting.Colors);
        Assert.Equal(
            [0, 500, 1_500, 3_500, 8_500, 18_500, 33_500],
            fixture.Temperatures.ReadTimes.Select(time => (int)(time - RuntimeFixture.Start).TotalMilliseconds));
    }

    [Fact]
    public async Task MissingReading_WhenSafetyReleaseFails_DoesNotCrashOrRepeatRelease()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68, null]);
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromMilliseconds(500));
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Lighting.ReleaseFailuresRemaining = 1;
        fixture.Advance(TimeSpan.FromSeconds(5));

        var unavailable = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromMilliseconds(100));
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(RuntimeStatus.SensorUnavailable, unavailable.Status);
        Assert.Equal(1, fixture.Lighting.ReleaseCalls);
        Assert.Contains("release failed", unavailable.Message);
    }

    [Fact]
    public async Task Recovery_ResetsBackoffAndContinuesSmoothingFromHeldColor()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [35, null, 85, null, 35]);
        var cold = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromMilliseconds(500));
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromSeconds(1));
        var recovered = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromMilliseconds(500));
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromSeconds(1));
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(RuntimeStatus.Active, recovered.Status);
        Assert.NotEqual(cold.Color, recovered.Color);
        Assert.NotEqual(new RgbColor(0xFF, 0x56, 0x5D), recovered.Color);
        Assert.Equal([0, 500, 1_500, 2_000, 3_000], fixture.Temperatures.ReadTimes.Select(time => (int)(time - RuntimeFixture.Start).TotalMilliseconds));
    }

    [Fact]
    public async Task LightingConnectionFailure_DoesNotDelaySensorPollingAndRetriesConnection()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68, 69]);
        fixture.Lighting.ConnectFailuresRemaining = 1;

        var unavailable = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromMilliseconds(100));
        var active = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromMilliseconds(400));
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(RuntimeStatus.LightingUnavailable, unavailable.Status);
        Assert.Equal(RuntimeStatus.Active, active.Status);
        Assert.Equal(2, fixture.Lighting.ConnectCalls);
        Assert.Equal(2, fixture.Temperatures.ReadCalls);
    }

    [Fact]
    public async Task LightingWriteFailure_RetriesWithoutChangingSensorSchedule()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68, 69]);
        fixture.Lighting.SetColorFailuresRemaining = 1;

        var unavailable = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromMilliseconds(100));
        var active = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromMilliseconds(400));
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(RuntimeStatus.LightingUnavailable, unavailable.Status);
        Assert.Equal(RuntimeStatus.Active, active.Status);
        Assert.Equal(3, fixture.Lighting.SetColorCalls);
        Assert.Equal(2, fixture.Temperatures.ReadCalls);
    }

    [Fact]
    public async Task DisableMode_PersistsBeforeReleaseAndSnapshot()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.SettingsStore.OnSave = settings =>
        {
            Assert.False(settings.IsModeEnabled);
            Assert.Equal(RuntimeStatus.Active, fixture.Runtime.CurrentSnapshot.Status);
            Assert.Equal(0, fixture.Lighting.ReleaseCalls);
        };

        await fixture.Runtime.SetModeEnabledAsync(false, CancellationToken.None);

        Assert.Equal(1, fixture.Lighting.ReleaseCalls);
        Assert.False(fixture.SettingsStore.Saved!.IsModeEnabled);
        Assert.Equal(RuntimeStatus.Disabled, fixture.Runtime.CurrentSnapshot.Status);
    }

    [Fact]
    public async Task UpdateSettings_WhenItDisablesMode_PersistsBeforeRelease()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.SettingsStore.OnSave = settings =>
        {
            Assert.False(settings.IsModeEnabled);
            Assert.Equal(0, fixture.Lighting.ReleaseCalls);
            Assert.Equal(RuntimeStatus.Active, fixture.Runtime.CurrentSnapshot.Status);
        };

        await fixture.Runtime.UpdateSettingsAsync(
            fixture.SettingsStore.Initial with { IsModeEnabled = false, NotificationsEnabled = false },
            CancellationToken.None);

        Assert.Equal(1, fixture.Lighting.ReleaseCalls);
        Assert.Equal(RuntimeStatus.Disabled, fixture.Runtime.CurrentSnapshot.Status);
        Assert.False(fixture.SettingsStore.Saved!.NotificationsEnabled);
    }

    [Fact]
    public async Task DisableMode_WhenReleaseFails_PublishesDisabledAndRetriesOnRepeat()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Lighting.ReleaseFailuresRemaining = 1;

        await fixture.Runtime.SetModeEnabledAsync(false, CancellationToken.None);

        Assert.Equal(RuntimeStatus.Disabled, fixture.Runtime.CurrentSnapshot.Status);
        Assert.Contains("release failed", fixture.Runtime.CurrentSnapshot.Message);
        Assert.Equal(1, fixture.SettingsStore.SaveCalls);
        Assert.Equal(1, fixture.Lighting.ReleaseCalls);

        await fixture.Runtime.SetModeEnabledAsync(false, CancellationToken.None);

        Assert.Equal(RuntimeStatus.Disabled, fixture.Runtime.CurrentSnapshot.Status);
        Assert.Null(fixture.Runtime.CurrentSnapshot.Message);
        Assert.Equal(1, fixture.SettingsStore.SaveCalls);
        Assert.Equal(2, fixture.Lighting.ReleaseCalls);
    }

    [Fact]
    public async Task UpdateSettings_WhenDisableReleaseFails_PublishesDisabledAndRetriesOnRepeat()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Lighting.ReleaseFailuresRemaining = 1;
        var disabled = fixture.SettingsStore.Initial with { IsModeEnabled = false, NotificationsEnabled = false };

        await fixture.Runtime.UpdateSettingsAsync(disabled, CancellationToken.None);

        Assert.Equal(RuntimeStatus.Disabled, fixture.Runtime.CurrentSnapshot.Status);
        Assert.Contains("release failed", fixture.Runtime.CurrentSnapshot.Message);
        Assert.Equal(1, fixture.Lighting.ReleaseCalls);

        await fixture.Runtime.UpdateSettingsAsync(disabled, CancellationToken.None);

        Assert.Equal(RuntimeStatus.Disabled, fixture.Runtime.CurrentSnapshot.Status);
        Assert.Null(fixture.Runtime.CurrentSnapshot.Message);
        Assert.Equal(2, fixture.SettingsStore.SaveCalls);
        Assert.Equal(2, fixture.Lighting.ReleaseCalls);
    }

    [Fact]
    public async Task Suspend_WhenReleaseFails_PublishesSuspendedAndRetriesOnRepeat()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Lighting.ReleaseFailuresRemaining = 1;

        await fixture.Runtime.SuspendAsync(CancellationToken.None);

        Assert.Equal(RuntimeStatus.Suspended, fixture.Runtime.CurrentSnapshot.Status);
        Assert.Contains("release failed", fixture.Runtime.CurrentSnapshot.Message);
        Assert.Equal(1, fixture.Lighting.ReleaseCalls);

        await fixture.Runtime.SuspendAsync(CancellationToken.None);

        Assert.Equal(RuntimeStatus.Suspended, fixture.Runtime.CurrentSnapshot.Status);
        Assert.Null(fixture.Runtime.CurrentSnapshot.Message);
        Assert.Equal(2, fixture.Lighting.ReleaseCalls);
    }

    [Fact]
    public async Task DisabledResume_PreservesPendingReleaseForAnIdenticalDisableRetry()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Lighting.ReleaseFailuresRemaining = 2;

        await fixture.Runtime.SuspendAsync(CancellationToken.None);
        await fixture.Runtime.SetModeEnabledAsync(false, CancellationToken.None);
        await fixture.Runtime.ResumeAsync(CancellationToken.None);
        await fixture.Runtime.SetModeEnabledAsync(false, CancellationToken.None);

        Assert.Equal(RuntimeStatus.Disabled, fixture.Runtime.CurrentSnapshot.Status);
        Assert.Null(fixture.Runtime.CurrentSnapshot.Message);
        Assert.Equal(1, fixture.SettingsStore.SaveCalls);
        Assert.Equal(3, fixture.Lighting.ReleaseCalls);
    }

    [Fact]
    public async Task UpdateSettings_DuringRamp_PreservesSmoothedTemperature()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [35, 85]);
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromMilliseconds(500));
        var ramping = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        var slowerProfile = fixture.SettingsStore.Initial.Profile with { SmoothingSeconds = 2 };
        await fixture.Runtime.UpdateSettingsAsync(
            fixture.SettingsStore.Initial with { Profile = slowerProfile },
            CancellationToken.None);
        fixture.Advance(TimeSpan.FromMilliseconds(100));

        var continued = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);

        Assert.NotEqual(new RgbColor(0xFF, 0x56, 0x5D), ramping.Color);
        Assert.NotEqual(new RgbColor(0xFF, 0x56, 0x5D), continued.Color);
        Assert.NotEqual(ramping.Color, continued.Color);
    }

    [Fact]
    public async Task SuspendResumeAndStop_AreIdempotentAndPreserveMode()
    {
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68, 69]);
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);

        await fixture.Runtime.SuspendAsync(CancellationToken.None);
        await fixture.Runtime.SuspendAsync(CancellationToken.None);
        await fixture.Runtime.ResumeAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromMilliseconds(500));
        var resumed = await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        await fixture.Runtime.StopAsync(CancellationToken.None);
        await fixture.Runtime.StopAsync(CancellationToken.None);

        Assert.Equal(RuntimeStatus.Active, resumed.Status);
        Assert.True(fixture.SettingsStore.Initial.IsModeEnabled);
        Assert.Equal(2, fixture.Lighting.ReleaseCalls);
        Assert.Equal(1, fixture.Temperatures.DisposeCalls);
        Assert.Equal(1, fixture.Lighting.DisposeCalls);
    }

    [Fact]
    public async Task BackgroundLoop_StartSuspendResumeAndStop_DoNotLeakTimersOrReads()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68, 69]);
        await fixture.Runtime.StartAsync(CancellationToken.None);
        await fixture.Runtime.StartAsync(CancellationToken.None);
        var firstRead = fixture.Temperatures.WaitForReadCountAsync(1);

        fixture.Advance(TimeSpan.FromMilliseconds(100));
        await firstRead.WaitAsync(timeout.Token);
        Assert.Equal(1, fixture.Clock.ActiveTimerCount);

        await fixture.Runtime.SuspendAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, fixture.Temperatures.ReadCalls);
        Assert.Equal(0, fixture.Clock.ActiveTimerCount);

        await fixture.Runtime.ResumeAsync(CancellationToken.None);
        var secondRead = fixture.Temperatures.WaitForReadCountAsync(2);
        fixture.Advance(TimeSpan.FromMilliseconds(100));
        await secondRead.WaitAsync(timeout.Token);
        Assert.Equal(1, fixture.Clock.ActiveTimerCount);

        await fixture.Runtime.StopAsync(CancellationToken.None);
        fixture.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(2, fixture.Temperatures.ReadCalls);
        Assert.Equal(0, fixture.Clock.ActiveTimerCount);
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledTwiceWithoutRepeatingCleanup()
    {
        var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);
        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None);

        await fixture.Runtime.DisposeAsync();
        await fixture.Runtime.DisposeAsync();

        Assert.Equal(1, fixture.Temperatures.DisposeCalls);
        Assert.Equal(1, fixture.Lighting.DisposeCalls);
    }

    [Fact]
    public async Task Stop_CancellationAfterShutdownBegins_DoesNotAbandonCleanup()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);
        fixture.Temperatures.BlockReads = true;
        var processing = fixture.Runtime.ProcessOnceAsync(CancellationToken.None);
        await fixture.Temperatures.ReadEntered.WaitAsync(timeout.Token);
        using var stopCancellation = new CancellationTokenSource();

        var stopping = fixture.Runtime.StopAsync(stopCancellation.Token);
        stopCancellation.Cancel();
        fixture.Temperatures.ContinueRead();
        await processing.WaitAsync(timeout.Token);
        await stopping.WaitAsync(timeout.Token);

        Assert.Equal(1, fixture.Temperatures.DisposeCalls);
        Assert.Equal(1, fixture.Lighting.DisposeCalls);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task SnapshotChanged_CanReenterRuntimeBecauseItRunsOutsideRuntimeLocks()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await using var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);
        EventHandler<RuntimeSnapshot>? handler = null;
        handler = (_, snapshot) =>
        {
            if (snapshot.Status != RuntimeStatus.Active)
            {
                return;
            }

            fixture.Runtime.SnapshotChanged -= handler;
            fixture.Runtime.SetModeEnabledAsync(false, CancellationToken.None).GetAwaiter().GetResult();
        };
        fixture.Runtime.SnapshotChanged += handler;

        await fixture.Runtime.ProcessOnceAsync(CancellationToken.None).WaitAsync(timeout.Token);

        Assert.Equal(RuntimeStatus.Disabled, fixture.Runtime.CurrentSnapshot.Status);
        Assert.Equal(1, fixture.Lighting.ReleaseCalls);
    }

    [Fact]
    public async Task SnapshotChanged_FromBackgroundLoop_CanStopThatLoopWithoutSelfDeadlock()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runtime.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.Status != RuntimeStatus.Active)
            {
                return;
            }

            fixture.Runtime.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            stopped.TrySetResult();
        };
        await fixture.Runtime.StartAsync(CancellationToken.None);

        var advancingClock = Task.Run(
            () => fixture.Advance(TimeSpan.FromMilliseconds(100)),
            TestContext.Current.CancellationToken);
        await Task.WhenAll(stopped.Task, advancingClock).WaitAsync(timeout.Token);

        Assert.Equal(0, fixture.Clock.ActiveTimerCount);
        Assert.Equal(1, fixture.Temperatures.DisposeCalls);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task ThrowingActiveSubscriber_DoesNotFaultOrStopBackgroundLoop()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var fixture = RuntimeFixture.Create(modeEnabled: true, temperatures: [68]);
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runtime.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.Status == RuntimeStatus.Active)
            {
                active.TrySetResult();
                throw new InvalidOperationException("subscriber failed");
            }
        };
        await fixture.Runtime.StartAsync(CancellationToken.None);

        var advancingClock = Task.Run(
            () => fixture.Advance(TimeSpan.FromMilliseconds(100)),
            TestContext.Current.CancellationToken);
        await Task.WhenAll(active.Task, advancingClock).WaitAsync(timeout.Token);

        Assert.Equal(RuntimeStatus.Active, fixture.Runtime.CurrentSnapshot.Status);
        Assert.Equal(1, fixture.Clock.ActiveTimerCount);
        await fixture.Runtime.StopAsync(CancellationToken.None);
        Assert.Equal(0, fixture.Clock.ActiveTimerCount);
        await fixture.DisposeAsync();
    }

    private sealed class RuntimeFixture : IAsyncDisposable
    {
        public static readonly DateTimeOffset Start = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        private RuntimeFixture(AppSettings settings, IEnumerable<double?> temperatures)
        {
            Clock = new FakeTimeProvider(Start);
            Temperatures = new FakeTemperatureProvider(Clock, temperatures);
            Lighting = new FakeLightingController();
            SettingsStore = new FakeSettingsStore(settings);
            Runtime = new ThermalRuntime(
                Temperatures,
                new ColorEngine(settings.Profile, settings.Profile.ColdTemperature),
                Lighting,
                settings,
                SettingsStore,
                Clock);
        }

        public FakeTimeProvider Clock { get; }
        public FakeTemperatureProvider Temperatures { get; }
        public FakeLightingController Lighting { get; }
        public FakeSettingsStore SettingsStore { get; }
        public ThermalRuntime Runtime { get; }

        public static RuntimeFixture Create(bool modeEnabled, IEnumerable<double?> temperatures)
        {
            return new RuntimeFixture(AppSettings.Default with { IsModeEnabled = modeEnabled }, temperatures);
        }

        public void Advance(TimeSpan amount) => Clock.Advance(amount);

        public ValueTask DisposeAsync() => Runtime.DisposeAsync();
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<FakeTimer> _timers = [];
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public int ActiveTimerCount
        {
            get
            {
                lock (_sync)
                {
                    return _timers.Count;
                }
            }
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _utcNow;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new FakeTimer(this, callback, state, dueTime, period);
            lock (_sync)
            {
                _timers.Add(timer);
            }

            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            List<(TimerCallback Callback, object? State)> callbacks = [];
            lock (_sync)
            {
                _utcNow += amount;
                foreach (var timer in _timers.ToArray())
                {
                    timer.CollectDue(_utcNow, callbacks);
                }
            }

            foreach (var (callback, state) in callbacks)
            {
                callback(state);
            }
        }

        private void Remove(FakeTimer timer)
        {
            lock (_sync)
            {
                _timers.Remove(timer);
            }
        }

        private sealed class FakeTimer : ITimer
        {
            private readonly FakeTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private DateTimeOffset? _nextDue;
            private TimeSpan _period;
            private bool _disposed;

            public FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                Change(dueTime, period);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period;
                _nextDue = dueTime == Timeout.InfiniteTimeSpan ? null : _owner.GetUtcNow() + dueTime;
                return true;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _owner.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void CollectDue(DateTimeOffset now, List<(TimerCallback Callback, object? State)> callbacks)
            {
                if (_disposed || _nextDue is not { } next || next > now)
                {
                    return;
                }

                callbacks.Add((_callback, _state));
                _nextDue = _period == Timeout.InfiniteTimeSpan ? null : now + _period;
            }
        }
    }

    private sealed class FakeTemperatureProvider : ITemperatureProvider
    {
        private readonly object _sync = new();
        private readonly FakeTimeProvider _clock;
        private readonly Queue<double?> _temperatures;
        private readonly Dictionary<int, TaskCompletionSource> _readWaiters = [];
        private readonly TaskCompletionSource _readEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _continueRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeTemperatureProvider(FakeTimeProvider clock, IEnumerable<double?> temperatures)
        {
            _clock = clock;
            _temperatures = new Queue<double?>(temperatures);
        }

        public int ReadCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public List<DateTimeOffset> ReadTimes { get; } = [];
        public bool BlockReads { get; set; }
        public Task ReadEntered => _readEntered.Task;

        public async ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double? value;
            lock (_sync)
            {
                ReadCalls++;
                ReadTimes.Add(_clock.GetUtcNow());
                value = _temperatures.Count == 0 ? null : _temperatures.Dequeue();
                if (_readWaiters.Remove(ReadCalls, out var waiter))
                {
                    waiter.TrySetResult();
                }
            }

            if (BlockReads)
            {
                _readEntered.TrySetResult();
                await _continueRead.Task.WaitAsync(cancellationToken);
            }

            return value is { } celsius
                ? new TemperatureReading(celsius, "fake", "GPU 0", _clock.GetUtcNow())
                : null;
        }

        public void ContinueRead() => _continueRead.TrySetResult();

        public Task WaitForReadCountAsync(int count)
        {
            lock (_sync)
            {
                if (ReadCalls >= count)
                {
                    return Task.CompletedTask;
                }

                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _readWaiters.Add(count, waiter);
                return waiter.Task;
            }
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeLightingController : ILightingController
    {
        public bool IsConnected { get; private set; }
        public LightingDeviceInfo? ConnectedDevice => IsConnected ? new LightingDeviceInfo("lamp", "Lamp", 4, true) : null;
        public event EventHandler? DevicesChanged
        {
            add { }
            remove { }
        }
        public int ConnectCalls { get; private set; }
        public int ConnectFailuresRemaining { get; set; }
        public int SetColorFailuresRemaining { get; set; }
        public int SetColorCalls { get; private set; }
        public int ReleaseFailuresRemaining { get; set; }
        public int ReleaseCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public List<RgbColor> Colors { get; } = [];

        public Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LightingDeviceInfo>>([new LightingDeviceInfo("lamp", "Lamp", 4, true)]);

        public Task<bool> ConnectAsync(string? preferredDeviceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCalls++;
            if (ConnectFailuresRemaining > 0)
            {
                ConnectFailuresRemaining--;
                return Task.FromResult(false);
            }

            IsConnected = true;
            return Task.FromResult(true);
        }

        public Task SetColorAsync(RgbColor color, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetColorCalls++;
            if (SetColorFailuresRemaining > 0)
            {
                SetColorFailuresRemaining--;
                throw new InvalidOperationException("write failed");
            }

            Colors.Add(color);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCalls++;
            if (ReleaseFailuresRemaining > 0)
            {
                ReleaseFailuresRemaining--;
                throw new InvalidOperationException("release failed");
            }

            IsConnected = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSettingsStore(AppSettings initial) : ISettingsStore
    {
        public AppSettings Initial { get; } = initial;
        public AppSettings? Saved { get; private set; }
        public int SaveCalls { get; private set; }
        public Action<AppSettings>? OnSave { get; set; }

        public Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SettingsLoadResult(Initial));

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCalls++;
            OnSave?.Invoke(settings);
            Saved = settings;
            return Task.CompletedTask;
        }
    }
}
