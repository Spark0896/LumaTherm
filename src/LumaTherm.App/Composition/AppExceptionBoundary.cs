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
    private readonly Action<Exception, bool, bool> _route;
    private readonly Action _requestShutdown;
    private int _attached;
    private int _foregroundNotified;
    private int _backgroundNotified;
    private int _shutdownRequested;

    public AppExceptionBoundary(
        IAppExceptionSource source,
        Action<Exception, bool, bool> route,
        Action requestShutdown)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
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
        _route(args.Exception, true, Interlocked.CompareExchange(ref _foregroundNotified, 1, 0) == 0);
        if (Interlocked.CompareExchange(ref _shutdownRequested, 1, 0) == 0) _requestShutdown();
    }

    private void OnDomainUnhandled(object? sender, AppBackgroundUnhandledEventArgs args) => RouteBackground(args);

    private void OnTaskUnhandled(object? sender, AppBackgroundUnhandledEventArgs args)
    {
        args.Observed = true;
        RouteBackground(args);
    }

    private void RouteBackground(AppBackgroundUnhandledEventArgs args) =>
        _route(args.Exception, false, Interlocked.CompareExchange(ref _backgroundNotified, 1, 0) == 0);
}
