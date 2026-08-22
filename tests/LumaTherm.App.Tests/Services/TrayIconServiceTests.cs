using LumaTherm.App.Services;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Sensors;
using LumaTherm.Core.Settings;

namespace LumaTherm.App.Tests.Services;

public sealed class TrayIconServiceTests
{
    [Theory]
    [InlineData(RuntimeStatus.Disabled, false, "Включить режим")]
    [InlineData(RuntimeStatus.Active, true, "Выключить режим")]
    [InlineData(RuntimeStatus.SensorUnavailable, true, "Выключить режим")]
    [InlineData(RuntimeStatus.Faulted, true, "Выключить режим")]
    public void BuildMenuState_CreatesExactlyFourRussianEntries(RuntimeStatus status, bool enabled, string toggleLabel)
    {
        var state = TrayIconService.BuildMenuState(Snapshot(status, enabled, 68));

        Assert.Equal("LumaTherm · 68°C · " + (status == RuntimeStatus.Active ? "Активно" : TrayIconService.GetStatusLabel(status)), state.Tooltip);
        Assert.Equal(4, state.Entries.Count);
        Assert.Equal(new TrayMenuEntry("68°C", false), state.Entries[0]);
        Assert.Equal(new TrayMenuEntry("Открыть LumaTherm", true), state.Entries[1]);
        Assert.Equal(new TrayMenuEntry(toggleLabel, true), state.Entries[2]);
        Assert.Equal(new TrayMenuEntry("Выход", true), state.Entries[3]);
        Assert.Contains("GIGABYTE Device", state.DeviceStatus);
    }

    [Fact]
    public void Tooltip_IsTruncatedWithoutSplittingSurrogatePair()
    {
        var text = new string('A', 62) + "😀tail";

        var truncated = TrayIconService.TruncateTooltip(text, 63);

        Assert.Equal(62, truncated.Length);
        Assert.False(char.IsHighSurrogate(truncated[^1]));
    }

    [Theory]
    [InlineData(RuntimeStatus.SensorUnavailable)]
    [InlineData(RuntimeStatus.LightingUnavailable)]
    [InlineData(RuntimeStatus.Faulted)]
    public async Task Notification_EmitsOncePerTransitionAndActiveRearms(RuntimeStatus unavailable)
    {
        var fixture = new TrayFixture(notificationsEnabled: true);
        await using var service = fixture.CreateService();

        fixture.Runtime.Publish(Snapshot(RuntimeStatus.Active, true, 68));
        fixture.Runtime.Publish(Snapshot(unavailable, true, 68));
        fixture.Runtime.Publish(Snapshot(unavailable, true, 68));
        Assert.Single(fixture.Platform.Notifications);

        fixture.Runtime.Publish(Snapshot(RuntimeStatus.Active, true, 68));
        fixture.Runtime.Publish(Snapshot(unavailable, true, 68));

        Assert.Equal(2, fixture.Platform.Notifications.Count);
        Assert.All(fixture.Platform.Notifications, notification => Assert.Contains("LumaTherm", notification.Title));
    }

    [Fact]
    public async Task Notifications_AreSuppressedWhenDisabledOrModeOff_AndRecoveryIsOneShot()
    {
        var fixture = new TrayFixture(notificationsEnabled: false);
        await using var service = fixture.CreateService();

        fixture.Runtime.Publish(Snapshot(RuntimeStatus.Active, true, 68));
        fixture.Runtime.Publish(Snapshot(RuntimeStatus.SensorUnavailable, true, 68));
        service.ShowRecoveryWarning("Восстановлены настройки");
        service.ShowRecoveryWarning("Восстановлены настройки");
        Assert.Empty(fixture.Platform.Notifications);

        fixture.NotificationsEnabled = true;
        fixture.Runtime.Publish(Snapshot(RuntimeStatus.Active, false, 68));
        fixture.Runtime.Publish(Snapshot(RuntimeStatus.Faulted, false, 68));
        service.ShowRecoveryWarning("Восстановлены настройки");
        service.ShowRecoveryWarning("Повтор");

        Assert.Single(fixture.Platform.Notifications);
        Assert.Contains("Восстановлены", fixture.Platform.Notifications[0].Message);
    }

    [Fact]
    public async Task LeftAndDoubleClick_ShowRestoreActivateExistingWindowWithoutRecreatingIt()
    {
        var fixture = new TrayFixture();
        fixture.Window.IsVisible = false;
        fixture.Window.IsMinimized = true;
        await using var service = fixture.CreateService();

        fixture.Platform.RaiseLeftClick();
        fixture.Platform.RaiseDoubleClick();
        fixture.Platform.RaiseOpen();
        await WaitUntilAsync(() => fixture.Window.BringToFrontCalls == 3);

        Assert.Equal(1, fixture.Window.ShowCalls);
        Assert.Equal(1, fixture.Window.RestoreCalls);
        Assert.Equal(3, fixture.Window.ActivateCalls);
        Assert.Equal(3, fixture.Window.BringToFrontCalls);
    }

    [Fact]
    public async Task ShowCallback_ObservesWindowFailureWithoutEscapingTrayEvent()
    {
        var fixture = new TrayFixture();
        fixture.Window.IsVisible = false;
        fixture.Window.ShowFailure = new InvalidOperationException("window failed");
        await using var service = fixture.CreateService();

        fixture.Platform.RaiseLeftClick();
        await WaitUntilAsync(() => fixture.Errors.Count == 1);

        Assert.Equal("window failed", fixture.Errors[0].Message);
    }

    [Fact]
    public async Task Exit_StopsRuntimeBeforeIconDisposeBeforeShutdown_EvenWhenStopFails()
    {
        var order = new List<string>();
        var fixture = new TrayFixture(order: order) { RuntimeStopFailure = new InvalidOperationException("stop failed") };
        await using var service = fixture.CreateService();

        fixture.Platform.RaiseExit();
        await fixture.Application.ShutdownRequested.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(["runtime.stop", "icon.hide", "icon.dispose", "app.shutdown"], order);
        Assert.True(fixture.ClosePolicy.IsExplicitExitRequested);
        Assert.Single(fixture.Errors);

        fixture.Platform.RaiseToggle();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(0, fixture.ToggleCalls);
    }

    [Fact]
    public async Task Exit_AttemptsHideDisposeAndShutdownWhenEveryCleanupStepFails()
    {
        var order = new List<string>();
        var fixture = new TrayFixture(order: order) { RuntimeStopFailure = new InvalidOperationException("stop failed") };
        fixture.Platform.HideFailure = new InvalidOperationException("hide failed");
        fixture.Platform.DisposeFailure = new InvalidOperationException("dispose failed");
        fixture.ErrorHandler = exception =>
        {
            fixture.Errors.Add(exception);
            order.Add($"error:{exception.Message}");
        };
        await using var service = fixture.CreateService();

        fixture.Platform.RaiseExit();
        await fixture.Application.ShutdownRequested.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["runtime.stop", "error:stop failed", "icon.hide", "error:hide failed", "icon.dispose", "error:dispose failed", "app.shutdown"],
            order);
        Assert.Equal(1, fixture.Platform.HideCalls);
        Assert.Equal(1, fixture.Platform.DisposeCalls);
        Assert.Equal(3, fixture.Errors.Count);
    }

    [Fact]
    public async Task Exit_StillShutsDownWhenErrorSinkThrows()
    {
        var order = new List<string>();
        var fixture = new TrayFixture(order: order) { RuntimeStopFailure = new InvalidOperationException("stop failed") };
        var reports = 0;
        fixture.ErrorHandler = _ =>
        {
            Interlocked.Increment(ref reports);
            throw new InvalidOperationException("logger failed");
        };
        await using var service = fixture.CreateService();

        fixture.Platform.RaiseExit();
        await fixture.Application.ShutdownRequested.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(["runtime.stop", "icon.hide", "icon.dispose", "app.shutdown"], order);
        Assert.Equal(1, reports);
    }

    [Fact]
    public async Task ToggleOperations_AreSerializedAndErrorsObservedWithoutEscapingCallback()
    {
        var fixture = new TrayFixture();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var gateTimeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        gateTimeout.CancelAfter(TimeSpan.FromSeconds(2));
        var calls = 0;
        fixture.Toggle = async () =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task.WaitAsync(gateTimeout.Token);
                throw new InvalidOperationException("toggle failed");
            }
        };
        var service = fixture.CreateService();

        try
        {
            fixture.Platform.RaiseToggle();
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), gateTimeout.Token);
            fixture.Platform.RaiseToggle();
            Assert.Equal(1, calls);
            releaseFirst.TrySetResult();

            await WaitUntilAsync(() => calls == 2 && fixture.Errors.Count == 1);
            Assert.Equal(2, calls);
            Assert.Equal("toggle failed", fixture.Errors[0].Message);
        }
        finally
        {
            releaseFirst.TrySetResult();
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task Dispose_UnsubscribesAndDisposesExactlyOnceWithNoLaterCallbacks()
    {
        var fixture = new TrayFixture();
        var service = fixture.CreateService();

        await service.DisposeAsync();
        await service.DisposeAsync();
        fixture.Platform.RaiseToggle();
        fixture.Runtime.Publish(Snapshot(RuntimeStatus.Faulted, true, 68));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.Platform.DisposeCalls);
        Assert.Equal(0, fixture.ToggleCalls);
        Assert.Empty(fixture.Platform.Notifications);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
        while (!condition()) await Task.Delay(10, linked.Token);
    }

    private static RuntimeSnapshot Snapshot(RuntimeStatus status, bool modeEnabled, double temperature) => new(
        status,
        new TemperatureReading(temperature, "NVML", "RTX 5070", DateTimeOffset.UtcNow),
        null,
        null,
        new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true),
        null,
        DateTimeOffset.UtcNow,
        modeEnabled);

    private sealed class TrayFixture
    {
        private readonly List<string> _order;
        private readonly FakeRuntime _runtime;
        public TrayFixture(bool notificationsEnabled = true, List<string>? order = null)
        {
            NotificationsEnabled = notificationsEnabled;
            _order = order ?? [];
            _runtime = new FakeRuntime(_order);
            ClosePolicy = new WindowClosePolicy(() => true);
        }
        public FakeTrayPlatform Platform { get; } = new();
        public FakeWindow Window { get; } = new();
        public FakeApplication Application { get; } = new();
        public FakeRuntime Runtime => _runtime;
        public WindowClosePolicy ClosePolicy { get; }
        public bool NotificationsEnabled { get; set; }
        public Exception? RuntimeStopFailure { set => _runtime.StopFailure = value; }
        public List<Exception> Errors { get; } = [];
        public Action<Exception>? ErrorHandler { get; set; }
        public int ToggleCalls { get; private set; }
        public Func<Task> Toggle { get; set; } = () => Task.CompletedTask;

        public TrayIconService CreateService()
        {
            Platform.Order = _order;
            Application.Order = _order;
            return new TrayIconService(
                Platform,
                Window,
                Application,
                Runtime,
                async () => { ToggleCalls++; await Toggle(); },
                ClosePolicy,
                () => NotificationsEnabled,
                ErrorHandler ?? Errors.Add);
        }
    }

    private sealed class FakeTrayPlatform : ITrayIconPlatform
    {
        private bool _visible;
        public event EventHandler? LeftClick;
        public event EventHandler? DoubleClick;
        public event EventHandler? OpenRequested;
        public event EventHandler? ToggleRequested;
        public event EventHandler? ExitRequested;
        public List<(string Title, string Message)> Notifications { get; } = [];
        public List<string> Order { get; set; } = [];
        public int DisposeCalls { get; private set; }
        public int HideCalls { get; private set; }
        public Exception? HideFailure { get; set; }
        public Exception? DisposeFailure { get; set; }
        public bool Visible
        {
            get => _visible;
            set
            {
                if (!value)
                {
                    HideCalls++;
                    Order.Add("icon.hide");
                    if (HideFailure is not null) throw HideFailure;
                }
                _visible = value;
            }
        }
        public TrayMenuState? MenuState { get; set; }
        public void ShowNotification(string title, string message) => Notifications.Add((title, message));
        public void Dispose()
        {
            DisposeCalls++;
            Order.Add("icon.dispose");
            if (DisposeFailure is not null) throw DisposeFailure;
        }
        public void RaiseLeftClick() => LeftClick?.Invoke(this, EventArgs.Empty);
        public void RaiseDoubleClick() => DoubleClick?.Invoke(this, EventArgs.Empty);
        public void RaiseOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);
        public void RaiseToggle() => ToggleRequested?.Invoke(this, EventArgs.Empty);
        public void RaiseExit() => ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeWindow : ITrayWindow
    {
        public bool IsVisible { get; set; } = true;
        public bool IsMinimized { get; set; }
        public int ShowCalls { get; private set; }
        public int RestoreCalls { get; private set; }
        public int ActivateCalls { get; private set; }
        public int BringToFrontCalls { get; private set; }
        public Exception? ShowFailure { get; set; }
        public void Show()
        {
            ShowCalls++;
            if (ShowFailure is not null) throw ShowFailure;
            IsVisible = true;
        }
        public void Restore() { RestoreCalls++; IsMinimized = false; }
        public void Activate() => ActivateCalls++;
        public void BringToFront() => BringToFrontCalls++;
    }

    private sealed class FakeApplication : ITrayApplication
    {
        public TaskCompletionSource ShutdownRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Order { get; set; } = [];
        public void RequestShutdown() { Order.Add("app.shutdown"); ShutdownRequested.TrySetResult(); }
    }

    private sealed class FakeRuntime(List<string> order) : IThermalRuntime
    {
        private RuntimeSnapshot _snapshot = Snapshot(RuntimeStatus.Disabled, false, 68);
        public Exception? StopFailure { get; set; }
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged;
        public RuntimeSnapshot CurrentSnapshot => _snapshot;
        public AppSettings CurrentSettings => AppSettings.Default;
        public void Publish(RuntimeSnapshot snapshot) { _snapshot = snapshot; SnapshotChanged?.Invoke(this, snapshot); }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            order.Add("runtime.stop");
            return StopFailure is null ? Task.CompletedTask : Task.FromException(StopFailure);
        }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdatePreferencesAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SuspendAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
