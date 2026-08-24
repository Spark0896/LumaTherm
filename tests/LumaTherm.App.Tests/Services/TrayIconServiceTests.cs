using System.ComponentModel;
using System.Drawing;
using LumaTherm.App.Localization;
using LumaTherm.App.Tests.Ui;
using Forms = System.Windows.Forms;
using LumaTherm.App.Services;
using LumaTherm.Core.Lighting;
using LumaTherm.Core.Runtime;
using LumaTherm.Core.Sensors;
using LumaTherm.Core.Settings;

namespace LumaTherm.App.Tests.Services;

[Collection(ThermalCoreUiCollection.Name)]
public sealed class TrayIconServiceTests
{
    private readonly ThermalCoreStaFixture _sta;

    public TrayIconServiceTests(ThermalCoreStaFixture sta) => _sta = sta;

    [Fact]
    public void BuildMenuState_AllOptionalEntriesHidden_ContainsOnlyEnabledExit()
    {
        var state = TrayIconService.BuildMenuState(
            Snapshot(RuntimeStatus.Disabled, false, 68),
            Settings(new TrayMenuOptions(false, false, false)),
            new FakeLocalization(AppLanguage.English));

        Assert.Equal([new TrayMenuEntry(TrayCommandKind.Exit, "Exit", true)], state.Entries);
    }

    [Fact]
    public void BuildMenuState_AllOptionalEntriesVisible_UsesTypedStableOrderAndActualTemperature()
    {
        var state = TrayIconService.BuildMenuState(
            Snapshot(RuntimeStatus.Active, true, 68),
            Settings(TrayMenuOptions.Default),
            new FakeLocalization(AppLanguage.English));

        Assert.Equal(
            [
                new TrayMenuEntry(TrayCommandKind.Temperature, "68°C", false),
                new TrayMenuEntry(TrayCommandKind.Open, "Open LumaTherm", true),
                new TrayMenuEntry(TrayCommandKind.ToggleMode, "Disable mode", true),
                new TrayMenuEntry(TrayCommandKind.Exit, "Exit", true),
            ],
            state.Entries);
        Assert.Equal("LumaTherm · 68°C · Active", state.Tooltip);
        Assert.Equal("GIGABYTE Device · Available", state.DeviceStatus);
    }

    [Fact]
    public void BuildMenuState_MissingSensorUsesEmDashInsteadOfStaleTemperature()
    {
        var state = TrayIconService.BuildMenuState(
            Snapshot(RuntimeStatus.SensorUnavailable, true, temperature: null),
            Settings(TrayMenuOptions.Default),
            new FakeLocalization(AppLanguage.English));

        Assert.Equal(new TrayMenuEntry(TrayCommandKind.Temperature, "—", false), state.Entries[0]);
        Assert.Equal("LumaTherm · — · No sensor", state.Tooltip);
    }

    [Theory]
    [InlineData(AppLanguage.Russian, false, "Открыть LumaTherm", "Включить режим", "Выход")]
    [InlineData(AppLanguage.Russian, true, "Открыть LumaTherm", "Выключить режим", "Выход")]
    [InlineData(AppLanguage.English, false, "Open LumaTherm", "Enable mode", "Exit")]
    [InlineData(AppLanguage.English, true, "Open LumaTherm", "Disable mode", "Exit")]
    public void BuildMenuState_LocalizesCommandsAndReflectsLiveMode(
        AppLanguage language,
        bool modeEnabled,
        string open,
        string toggle,
        string exit)
    {
        var state = TrayIconService.BuildMenuState(
            Snapshot(modeEnabled ? RuntimeStatus.Active : RuntimeStatus.Disabled, modeEnabled, 68),
            Settings(TrayMenuOptions.Default),
            new FakeLocalization(language));

        Assert.Equal(open, state.Entries.Single(entry => entry.Kind == TrayCommandKind.Open).Label);
        Assert.Equal(toggle, state.Entries.Single(entry => entry.Kind == TrayCommandKind.ToggleMode).Label);
        Assert.Equal(exit, state.Entries.Single(entry => entry.Kind == TrayCommandKind.Exit).Label);
    }

    [Fact]
    public async Task Menu_RebuildsAfterPreferenceAndLanguageChanges()
    {
        var fixture = new TrayFixture();
        await using var service = fixture.CreateService();
        Assert.Equal(4, fixture.Platform.MenuState!.Entries.Count);

        fixture.Runtime.SetSettings(Settings(new TrayMenuOptions(false, false, false)) with { Language = AppLanguage.Russian });
        Assert.Equal([TrayCommandKind.Exit], fixture.Platform.MenuState!.Entries.Select(entry => entry.Kind));
        Assert.Equal("Выход", fixture.Platform.MenuState.Entries[0].Label);

        fixture.Localization.Apply(AppLanguage.English);

        Assert.Equal("Exit", fixture.Platform.MenuState.Entries[0].Label);
    }

    [Fact]
    public async Task Menu_RebuildsAfterTemperatureAndModeSnapshotChanges()
    {
        var fixture = new TrayFixture();
        await using var service = fixture.CreateService();

        fixture.Runtime.Publish(Snapshot(RuntimeStatus.Active, true, 72));

        Assert.Equal(
            "72°C",
            fixture.Platform.MenuState!.Entries.Single(entry => entry.Kind == TrayCommandKind.Temperature).Label);
        Assert.Equal(
            "Disable mode",
            fixture.Platform.MenuState.Entries.Single(entry => entry.Kind == TrayCommandKind.ToggleMode).Label);
    }

    [Fact]
    public async Task TypedCommandRouting_InvokesOnlyMatchingActionsAndTemperatureIsNonActionable()
    {
        var fixture = new TrayFixture();
        await using var service = fixture.CreateService();

        fixture.Platform.RaiseCommand(TrayCommandKind.Open);
        fixture.Platform.RaiseCommand(TrayCommandKind.ToggleMode);
        await WaitUntilAsync(() => fixture.Window.BringToFrontCalls == 1 && fixture.ToggleCalls == 1);

        var showCalls = fixture.Window.BringToFrontCalls;
        var toggleCalls = fixture.ToggleCalls;
        fixture.Platform.RaiseCommand(TrayCommandKind.Temperature);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(showCalls, fixture.Window.BringToFrontCalls);
        Assert.Equal(toggleCalls, fixture.ToggleCalls);
        Assert.False(fixture.ClosePolicy.IsExplicitExitRequested);
        Assert.Equal(0, fixture.Runtime.StopCalls);
    }


    [Fact]
    public void NotifyPlatform_RebuildsFreshTypedMenuAndDisposesReplacedAndCurrentItems()
    {
        _sta.Run(() =>
        {
            var notifyIcon = new Forms.NotifyIcon();
            var ownedIcon = (Icon)SystemIcons.Application.Clone();
            var platform = new NotifyIconTrayPlatform(ownedIcon, notifyIcon);
            var routed = new List<TrayCommandKind>();
            platform.CommandRequested += (_, kind) => routed.Add(kind);
            platform.MenuState = new TrayMenuState(
                "LumaTherm",
                "device",
                [
                    new TrayMenuEntry(TrayCommandKind.Exit, "Exit", true),
                    new TrayMenuEntry(TrayCommandKind.Temperature, "68°C", true),
                    new TrayMenuEntry(TrayCommandKind.ToggleMode, "Disable mode", true),
                    new TrayMenuEntry(TrayCommandKind.Open, "Open LumaTherm", true),
                ]);
            var firstMenu = Assert.IsType<Forms.ContextMenuStrip>(notifyIcon.ContextMenuStrip);
            var firstItems = firstMenu.Items.Cast<Forms.ToolStripMenuItem>().ToArray();

            foreach (var item in firstItems)
            {
                item.PerformClick();
            }

            Assert.Equal(
                [TrayCommandKind.Exit, TrayCommandKind.ToggleMode, TrayCommandKind.Open],
                routed);

            platform.MenuState = new TrayMenuState(
                "LumaTherm",
                "device",
                [new TrayMenuEntry(TrayCommandKind.Exit, "Exit", true)]);
            var currentMenu = Assert.IsType<Forms.ContextMenuStrip>(notifyIcon.ContextMenuStrip);
            var currentItems = currentMenu.Items.Cast<Forms.ToolStripMenuItem>().ToArray();

            Assert.NotSame(firstMenu, currentMenu);
            Assert.True(firstMenu.IsDisposed);
            Assert.All(firstItems, item => Assert.True(item.IsDisposed));
            Assert.Single(currentItems);

            platform.Dispose();

            Assert.True(currentMenu.IsDisposed);
            Assert.All(currentItems, item => Assert.True(item.IsDisposed));
        });
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
        var menuAssignments = fixture.Platform.MenuStateAssignments;

        await service.DisposeAsync();
        await service.DisposeAsync();
        fixture.Platform.RaiseToggle();
        fixture.Runtime.Publish(Snapshot(RuntimeStatus.Faulted, true, 68));
        fixture.Runtime.SetSettings(Settings(new TrayMenuOptions(false, false, false)) with { Language = AppLanguage.Russian });
        fixture.Localization.Apply(AppLanguage.Russian);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.Platform.DisposeCalls);
        Assert.Equal(0, fixture.ToggleCalls);
        Assert.Empty(fixture.Platform.Notifications);
        Assert.Equal(menuAssignments, fixture.Platform.MenuStateAssignments);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
        while (!condition()) await Task.Delay(10, linked.Token);
    }

    private static RuntimeSnapshot Snapshot(RuntimeStatus status, bool modeEnabled, double? temperature) => new(
        status,
        temperature is null
            ? null
            : new TemperatureReading(temperature.Value, "NVML", "RTX 5070", DateTimeOffset.UtcNow),
        null,
        null,
        new LightingDeviceInfo("lamp", "GIGABYTE Device", 12, true),
        null,
        DateTimeOffset.UtcNow,
        modeEnabled);

    private static AppSettings Settings(TrayMenuOptions options) =>
        AppSettings.Default with { TrayMenu = options };

    private sealed class FakeLocalization(AppLanguage language) : ILocalizationService
    {
        public AppLanguage CurrentLanguage { get; private set; } = language;
        public event EventHandler? LanguageChanged;

        public string Get(string key)
        {
            var english = CurrentLanguage == AppLanguage.English;
            return key switch
            {
                "App.Name" => "LumaTherm",
                "Tray.Open" => english ? "Open LumaTherm" : "Открыть LumaTherm",
                "Tray.EnableMode" => english ? "Enable mode" : "Включить режим",
                "Tray.DisableMode" => english ? "Disable mode" : "Выключить режим",
                "Tray.Exit" => english ? "Exit" : "Выход",
                "Tray.Available" => english ? "Available" : "Доступна",
                "Tray.Unavailable" => english ? "Unavailable" : "Недоступна",
                "Tray.StatusDisabled" => english ? "Disabled" : "Выключено",
                "Tray.StatusConnecting" => english ? "Connecting" : "Подключение",
                "Tray.StatusActive" => english ? "Active" : "Активно",
                "Tray.StatusHolding" => english ? "Holding color" : "Удержание цвета",
                "Tray.StatusSensorUnavailable" => english ? "No sensor" : "Нет датчика",
                "Tray.StatusLightingUnavailable" => english ? "No lighting" : "Нет подсветки",
                "Tray.StatusSuspended" => english ? "Suspended" : "Приостановлено",
                "Tray.StatusFaulted" => english ? "Error" : "Ошибка",
                "Tray.StatusUnknown" => english ? "Unknown" : "Неизвестно",
                "Runtime.LightingNotFound" => english ? "Lighting not found" : "Подсветка не обнаружена",
                _ => throw new KeyNotFoundException(key),
            };
        }

        public void Apply(AppLanguage value)
        {
            if (CurrentLanguage == value) return;
            CurrentLanguage = value;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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
        public FakeLocalization Localization { get; } = new(AppLanguage.English);
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
                Localization,
                () => Runtime.CurrentSettings,
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
        private TrayMenuState? _menuState;
        public event EventHandler? DoubleClick;
        public List<(string Title, string Message)> Notifications { get; } = [];
        public List<string> Order { get; set; } = [];
        public int DisposeCalls { get; private set; }
        public event EventHandler<TrayCommandKind>? CommandRequested;
        public int HideCalls { get; private set; }
        public Exception? HideFailure { get; set; }
        public int MenuStateAssignments { get; private set; }
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
        public TrayMenuState? MenuState
        {
            get => _menuState;
            set
            {
                _menuState = value;
                MenuStateAssignments++;
            }
        }
        public void ShowNotification(string title, string message) => Notifications.Add((title, message));
        public void Dispose()
        {
            DisposeCalls++;
            Order.Add("icon.dispose");
            if (DisposeFailure is not null) throw DisposeFailure;
        }
        public void RaiseLeftClick() => LeftClick?.Invoke(this, EventArgs.Empty);
        public void RaiseDoubleClick() => DoubleClick?.Invoke(this, EventArgs.Empty);
        public void RaiseOpen() => RaiseCommand(TrayCommandKind.Open);
        public void RaiseToggle() => RaiseCommand(TrayCommandKind.ToggleMode);
        public void RaiseExit() => RaiseCommand(TrayCommandKind.Exit);
        public void RaiseCommand(TrayCommandKind kind) => CommandRequested?.Invoke(this, kind);
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

    private sealed class FakeRuntime(List<string> order) : IThermalRuntime, INotifyPropertyChanged
    {
        private RuntimeSnapshot _snapshot = Snapshot(RuntimeStatus.Disabled, false, 68);
        public Exception? StopFailure { get; set; }
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged;
        public RuntimeSnapshot CurrentSnapshot => _snapshot;
        public event PropertyChangedEventHandler? PropertyChanged;
        private AppSettings _settings = AppSettings.Default;
        public int StopCalls { get; private set; }
        public AppSettings CurrentSettings => _settings;
        public void Publish(RuntimeSnapshot snapshot) { _snapshot = snapshot; SnapshotChanged?.Invoke(this, snapshot); }
        public void SetSettings(AppSettings settings)
        {
            _settings = settings;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentSettings)));
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
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
