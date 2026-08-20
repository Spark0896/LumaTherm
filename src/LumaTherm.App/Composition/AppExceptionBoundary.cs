namespace LumaTherm.App.Composition;

public sealed class AppDispatcherUnhandledEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception ?? throw new ArgumentNullException(nameof(exception));
    public bool Handled { get; set; }
}

public sealed class AppBackgroundUnhandledEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception ?? throw new ArgumentNullException(nameof(exception));
    public bool Observed { get; set; }
}

public interface IAppExceptionSource
{
    event EventHandler<AppDispatcherUnhandledEventArgs>? DispatcherUnhandled;
    event EventHandler<AppBackgroundUnhandledEventArgs>? DomainUnhandled;
    event EventHandler<AppBackgroundUnhandledEventArgs>? TaskUnhandled;
}

public sealed class AppExceptionBoundary : IDisposable
{
    private readonly IAppExceptionSource _source;
    private readonly IAppDispatcher _dispatcher;
    private readonly Action<Exception, bool, bool> _route;
    private readonly Action _requestShutdown;
    private int _attached;
    private int _foregroundNotified;
    private int _backgroundNotified;
    private int _shutdownRequested;
    private int _foregroundRouting;
    private int _backgroundRouting;

    public AppExceptionBoundary(
        IAppExceptionSource source,
        IAppDispatcher dispatcher,
        Action<Exception, bool, bool> route,
        Action requestShutdown)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        _requestShutdown = requestShutdown ?? throw new ArgumentNullException(nameof(requestShutdown));
    }

    public void Attach()
    {
        if (Interlocked.CompareExchange(ref _attached, 1, 0) != 0) return;
        _source.DispatcherUnhandled += OnDispatcherUnhandled;
        _source.DomainUnhandled += OnDomainUnhandled;
        _source.TaskUnhandled += OnTaskUnhandled;
    }

    public void Detach()
    {
        if (Interlocked.CompareExchange(ref _attached, 0, 1) != 1) return;
        _source.DispatcherUnhandled -= OnDispatcherUnhandled;
        _source.DomainUnhandled -= OnDomainUnhandled;
        _source.TaskUnhandled -= OnTaskUnhandled;
    }

    public void Dispose() => Detach();

    private void OnDispatcherUnhandled(object? sender, AppDispatcherUnhandledEventArgs args)
    {
        args.Handled = true;
        if (Interlocked.CompareExchange(ref _foregroundRouting, 1, 0) != 0) return;
        DispatchContained(
            () =>
            {
                try { _route(args.Exception, true, Interlocked.CompareExchange(ref _foregroundNotified, 1, 0) == 0); }
                catch { }
                finally { RequestShutdownContained(); }
            },
            () => Volatile.Write(ref _foregroundRouting, 0),
            requestShutdownOnDispatchFailure: true);
    }

    private void OnDomainUnhandled(object? sender, AppBackgroundUnhandledEventArgs args) => RouteBackground(args);

    private void OnTaskUnhandled(object? sender, AppBackgroundUnhandledEventArgs args)
    {
        args.Observed = true;
        RouteBackground(args);
    }

    private void RouteBackground(AppBackgroundUnhandledEventArgs args)
    {
        if (Interlocked.CompareExchange(ref _backgroundRouting, 1, 0) != 0) return;
        DispatchContained(
            () =>
            {
                try { _route(args.Exception, false, Interlocked.CompareExchange(ref _backgroundNotified, 1, 0) == 0); }
                catch { }
            },
            () => Volatile.Write(ref _backgroundRouting, 0),
            requestShutdownOnDispatchFailure: false);
    }

    private void DispatchContained(Action action, Action completed, bool requestShutdownOnDispatchFailure)
    {
        Task dispatch;
        try
        {
            dispatch = _dispatcher.InvokeAsync(() =>
            {
                try { action(); }
                finally { completed(); }
            });
        }
        catch
        {
            completed();
            if (requestShutdownOnDispatchFailure) RequestShutdownContained();
            return;
        }

        dispatch.ContinueWith(
            failedOrCanceled =>
            {
                if (failedOrCanceled.IsFaulted) _ = failedOrCanceled.Exception;
                completed();
                if (requestShutdownOnDispatchFailure) RequestShutdownContained();
            },
            CancellationToken.None,
            TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RequestShutdownContained()
    {
        if (Interlocked.CompareExchange(ref _shutdownRequested, 1, 0) != 0) return;
        try { _requestShutdown(); }
        catch { }
    }
}
