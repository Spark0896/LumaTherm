using System.Threading.Channels;
using LumaTherm.Core.Runtime;

namespace LumaTherm.App.Services;

public sealed class TrayIconService : IAsyncDisposable
{
    public const int TooltipLimit = 63;
    private readonly object _sync = new();
    private readonly ITrayIconPlatform _platform;
    private readonly ITrayWindow _window;
    private readonly ITrayApplication _application;
    private readonly IThermalRuntime _runtime;
    private readonly Func<Task> _toggleModeAsync;
    private readonly WindowClosePolicy _closePolicy;
    private readonly Func<bool> _notificationsEnabled;
    private readonly Action<Exception>? _errorSink;
    private readonly Channel<Func<Task>> _operations;
    private readonly Task _operationConsumer;
    private RuntimeStatus _previousStatus;
    private int _exitRequested;
    private bool _recoveryWarningShown;
    private bool _platformDisposed;
    private bool _disposed;

    public TrayIconService(
        ITrayIconPlatform platform,
        ITrayWindow window,
        ITrayApplication application,
        IThermalRuntime runtime,
        Func<Task> toggleModeAsync,
        WindowClosePolicy closePolicy,
        Func<bool> notificationsEnabled,
        Action<Exception>? errorSink = null)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _toggleModeAsync = toggleModeAsync ?? throw new ArgumentNullException(nameof(toggleModeAsync));
        _closePolicy = closePolicy ?? throw new ArgumentNullException(nameof(closePolicy));
        _notificationsEnabled = notificationsEnabled ?? throw new ArgumentNullException(nameof(notificationsEnabled));
        _errorSink = errorSink;
        _operations = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        _platform.LeftClick += OnShowRequested;
        _platform.DoubleClick += OnShowRequested;
        _platform.OpenRequested += OnShowRequested;
        _platform.ToggleRequested += OnToggleRequested;
        _platform.ExitRequested += OnExitRequested;
        _runtime.SnapshotChanged += OnSnapshotChanged;
        _previousStatus = _runtime.CurrentSnapshot.Status;
        ApplySnapshot(_runtime.CurrentSnapshot, notify: false);
        _platform.Visible = true;
        _operationConsumer = ConsumeOperationsAsync();
    }

    public static TrayMenuState BuildMenuState(RuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var temperature = snapshot.Temperature is null ? "—°C" : $"{snapshot.Temperature.Celsius:0}°C";
        var tooltip = TruncateTooltip($"LumaTherm · {temperature} · {GetStatusLabel(snapshot.Status)}", TooltipLimit);
        var toggle = snapshot.IsModeEnabled ? "Выключить режим" : "Включить режим";
        var device = snapshot.LightingDevice is null
            ? "Подсветка не обнаружена"
            : $"{snapshot.LightingDevice.Name} · {(snapshot.LightingDevice.IsAvailable ? "Доступна" : "Недоступна")}";
        return new TrayMenuState(
            tooltip,
            device,
            [
                new TrayMenuEntry(temperature, false),
                new TrayMenuEntry("Открыть LumaTherm", true),
                new TrayMenuEntry(toggle, true),
                new TrayMenuEntry("Выход", true),
            ]);
    }

    public static string GetStatusLabel(RuntimeStatus status) => status switch
    {
        RuntimeStatus.Disabled => "Выключено",
        RuntimeStatus.Connecting => "Подключение",
        RuntimeStatus.Active => "Активно",
        RuntimeStatus.HoldingLastColor => "Удержание цвета",
        RuntimeStatus.SensorUnavailable => "Нет датчика",
        RuntimeStatus.LightingUnavailable => "Нет подсветки",
        RuntimeStatus.Suspended => "Приостановлено",
        RuntimeStatus.Faulted => "Ошибка",
        _ => "Неизвестно",
    };

    public static string TruncateTooltip(string value, int maximumLength = TooltipLimit)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);
        if (value.Length <= maximumLength)
        {
            return value;
        }

        var length = maximumLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }

    public void ShowWindow()
    {
        lock (_sync)
        {
            if (_disposed || Volatile.Read(ref _exitRequested) != 0)
            {
                return;
            }
        }

        if (!_window.IsVisible)
        {
            _window.Show();
        }
        if (_window.IsMinimized)
        {
            _window.Restore();
        }
        _window.Activate();
        _window.BringToFront();
    }

    public void ShowNotification(string title, string message)
    {
        lock (_sync)
        {
            if (_disposed || _platformDisposed || !_notificationsEnabled())
            {
                return;
            }

            _platform.ShowNotification(title, message);
        }
    }

    public void ShowRecoveryWarning(string message)
    {
        lock (_sync)
        {
            if (_recoveryWarningShown || _disposed || _platformDisposed || !_notificationsEnabled())
            {
                return;
            }

            _recoveryWarningShown = true;
            _platform.ShowNotification("LumaTherm · Настройки восстановлены", message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _platform.LeftClick -= OnShowRequested;
            _platform.DoubleClick -= OnShowRequested;
            _platform.OpenRequested -= OnShowRequested;
            _platform.ToggleRequested -= OnToggleRequested;
            _platform.ExitRequested -= OnExitRequested;
            _runtime.SnapshotChanged -= OnSnapshotChanged;
            _operations.Writer.TryComplete();
        }

        await _operationConsumer;
        DisposePlatform();
    }

    private void OnShowRequested(object? sender, EventArgs args) => Enqueue(() =>
    {
        ShowWindow();
        return Task.CompletedTask;
    });

    private void OnToggleRequested(object? sender, EventArgs args)
    {
        if (Volatile.Read(ref _exitRequested) == 0)
        {
            Enqueue(_toggleModeAsync);
        }
    }

    private void OnExitRequested(object? sender, EventArgs args)
    {
        if (Interlocked.CompareExchange(ref _exitRequested, 1, 0) == 0)
        {
            Enqueue(ExitAsync);
        }
    }

    private void OnSnapshotChanged(object? sender, RuntimeSnapshot snapshot) => ApplySnapshot(snapshot, notify: true);

    private void ApplySnapshot(RuntimeSnapshot snapshot, bool notify)
    {
        lock (_sync)
        {
            if (_disposed || _platformDisposed || Volatile.Read(ref _exitRequested) != 0)
            {
                return;
            }

            _platform.MenuState = BuildMenuState(snapshot);
            if (notify && ShouldNotify(snapshot))
            {
                _platform.ShowNotification("LumaTherm · Внимание", NotificationMessage(snapshot.Status));
            }
            _previousStatus = snapshot.Status;
        }
    }

    private bool ShouldNotify(RuntimeSnapshot snapshot)
    {
        if (!_notificationsEnabled() || !snapshot.IsModeEnabled || snapshot.Status == _previousStatus)
        {
            return false;
        }

        return snapshot.Status is RuntimeStatus.SensorUnavailable or RuntimeStatus.LightingUnavailable or RuntimeStatus.Faulted;
    }

    private static string NotificationMessage(RuntimeStatus status) => status switch
    {
        RuntimeStatus.SensorUnavailable => "Датчик температуры недоступен.",
        RuntimeStatus.LightingUnavailable => "Подсветка недоступна.",
        _ => "LumaTherm столкнулся с ошибкой.",
    };

    private void Enqueue(Func<Task> operation)
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _operations.Writer.TryWrite(operation);
            }
        }
    }

    private async Task ConsumeOperationsAsync()
    {
        await foreach (var operation in _operations.Reader.ReadAllAsync())
        {
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }
    }

    private async Task ExitAsync()
    {
        _closePolicy.RequestExplicitExit();
        try
        {
            await _runtime.StopAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
        finally
        {
            try
            {
                DisposePlatform();
            }
            finally
            {
                try
                {
                    _application.RequestShutdown();
                }
                catch (Exception exception)
                {
                    ReportFailure(exception);
                }
            }
        }
    }

    private void DisposePlatform()
    {
        lock (_sync)
        {
            if (_platformDisposed)
            {
                return;
            }

            _platformDisposed = true;
        }

        try
        {
            _platform.Visible = false;
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }

        try
        {
            _platform.Dispose();
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private void ReportFailure(Exception exception)
    {
        try
        {
            _errorSink?.Invoke(exception);
        }
        catch
        {
        }
    }
}
