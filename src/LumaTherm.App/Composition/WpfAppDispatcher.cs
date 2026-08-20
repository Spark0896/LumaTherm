using System.Windows.Threading;

namespace LumaTherm.App.Composition;

public sealed class WpfAppDispatcher(Dispatcher dispatcher) : IAppDispatcher
{
    private readonly Dispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_dispatcher.CheckAccess())
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
        return _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task;
    }
}
