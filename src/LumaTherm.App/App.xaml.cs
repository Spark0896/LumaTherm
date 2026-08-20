using System.Windows;
using System.Windows.Threading;
using LumaTherm.App.Composition;

namespace LumaTherm.App;

public partial class App : System.Windows.Application
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(8);
    private readonly CancellationTokenSource _lifetime = new();
    private AppHost? _host;
    private WpfExceptionSource? _exceptionSource;
    private AppExceptionBoundary? _exceptionBoundary;
    private Task? _startupObserver;

    public App()
        : this(startHost: true)
    {
    }

    internal App(bool startHost)
    {
        StartHost = startHost;
    }

    private bool StartHost { get; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (!StartHost) return;
        _host = new AppHost(ProductionAppServices.Create(this), e.Args, RequestShutdown);
        _exceptionSource = new WpfExceptionSource(this);
        _exceptionBoundary = new AppExceptionBoundary(
            _exceptionSource,
            new WpfAppDispatcher(Dispatcher),
            (exception, foreground, notify) => _host.LogUnhandled(exception, foreground, notify),
            RequestShutdown);
        _exceptionBoundary.Attach();
        _startupObserver = ObserveStartupAsync(_host.StartAsync(_lifetime.Token));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifetime.Cancel();
        _exceptionBoundary?.Detach();
        _exceptionSource?.Dispose();
        if (_host is not null)
        {
            WaitWithDispatcherPump(_host.StopAsync(CancellationToken.None), ShutdownTimeout);
        }
        _lifetime.Dispose();
        base.OnExit(e);
    }

    private async Task ObserveStartupAsync(Task<bool> startup)
    {
        try
        {
            if (!await startup) RequestShutdown();
        }
        catch (Exception exception)
        {
            _host?.LogUnhandled(exception, foreground: true);
            RequestShutdown();
        }
    }

    private void RequestShutdown()
    {
        if (Dispatcher.CheckAccess()) Shutdown();
        else Dispatcher.BeginInvoke((Action)Shutdown);
    }

    internal static bool WaitWithDispatcherPump(Task task, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(task);
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = timeout,
        };
        EventHandler tick = (_, _) => frame.Continue = false;
        timer.Tick += tick;
        task.ContinueWith(
            _ => frame.Continue = false,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.FromCurrentSynchronizationContext());
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        timer.Tick -= tick;
        if (!task.IsCompleted) return false;
        task.GetAwaiter().GetResult();
        return true;
    }
}
