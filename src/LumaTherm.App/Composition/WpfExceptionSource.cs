using System.Windows.Threading;

namespace LumaTherm.App.Composition;

internal sealed class WpfExceptionSource : IAppExceptionSource, IDisposable
{
    private readonly System.Windows.Application _application;
    private bool _disposed;

    public WpfExceptionSource(System.Windows.Application application)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _application.DispatcherUnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
        TaskScheduler.UnobservedTaskException += OnTaskUnhandled;
    }

    public event EventHandler<AppDispatcherUnhandledEventArgs>? DispatcherUnhandled;
    public event EventHandler<AppBackgroundUnhandledEventArgs>? DomainUnhandled;
    public event EventHandler<AppBackgroundUnhandledEventArgs>? TaskUnhandled;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _application.DispatcherUnhandledException -= OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandled;
        TaskScheduler.UnobservedTaskException -= OnTaskUnhandled;
        DispatcherUnhandled = null;
        DomainUnhandled = null;
        TaskUnhandled = null;
    }

    private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        var forwarded = new AppDispatcherUnhandledEventArgs(args.Exception);
        DispatcherUnhandled?.Invoke(this, forwarded);
        args.Handled = forwarded.Handled;
    }

    private void OnDomainUnhandled(object sender, UnhandledExceptionEventArgs args)
    {
        var exception = args.ExceptionObject as Exception ?? new InvalidOperationException("Unknown AppDomain failure.");
        DomainUnhandled?.Invoke(this, new AppBackgroundUnhandledEventArgs(exception));
    }

    private void OnTaskUnhandled(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        var forwarded = new AppBackgroundUnhandledEventArgs(args.Exception);
        TaskUnhandled?.Invoke(this, forwarded);
        if (forwarded.Observed) args.SetObserved();
    }
}
