using LumaTherm.App.Composition;

namespace LumaTherm.App.Tests.Composition;

public sealed class AppExceptionBoundaryTests
{
    [Fact]
    public void AttachDetach_AreExactlyOnce()
    {
        var source = new FakeExceptionSource();
        using var boundary = new AppExceptionBoundary(source, new InlineAppDispatcher(), (_, _, _) => { }, () => { });

        boundary.Attach();
        boundary.Attach();
        Assert.Equal((1, 1, 1), (source.DispatcherSubscribers, source.DomainSubscribers, source.TaskSubscribers));

        boundary.Detach();
        boundary.Detach();
        Assert.Equal((0, 0, 0), (source.DispatcherSubscribers, source.DomainSubscribers, source.TaskSubscribers));
    }

    [Fact]
    public void DispatcherFailure_IsHandledWarnedOnceAndRequestsShutdown()
    {
        var source = new FakeExceptionSource();
        var routed = new List<(bool Foreground, bool Notify)>();
        var shutdowns = 0;
        using var boundary = new AppExceptionBoundary(source, new InlineAppDispatcher(), (_, foreground, notify) => routed.Add((foreground, notify)), () => shutdowns++);
        boundary.Attach();

        var first = source.RaiseDispatcher(new InvalidOperationException("one"));
        var second = source.RaiseDispatcher(new InvalidOperationException("two"));

        Assert.True(first.Handled);
        Assert.True(second.Handled);
        Assert.Equal([(true, true), (true, false)], routed);
        Assert.Equal(1, shutdowns);
    }

    [Fact]
    public void BackgroundFailures_AreObservedAndNotificationIsSuppressedAfterFirst()
    {
        var source = new FakeExceptionSource();
        var routed = new List<(bool Foreground, bool Notify)>();
        using var boundary = new AppExceptionBoundary(source, new InlineAppDispatcher(), (_, foreground, notify) => routed.Add((foreground, notify)), () => { });
        boundary.Attach();

        source.RaiseDomain(new InvalidOperationException("domain"));
        var task = source.RaiseTask(new InvalidOperationException("task"));

        Assert.True(task.Observed);
        Assert.Equal([(false, true), (false, false)], routed);
    }

    [Fact]
    public void ThrowingForegroundRoute_CannotEscapeOrSkipShutdown()
    {
        var source = new FakeExceptionSource();
        var shutdowns = 0;
        using var boundary = new AppExceptionBoundary(source, new InlineAppDispatcher(), (_, _, _) => throw new InvalidOperationException("route"), () => shutdowns++);
        boundary.Attach();

        var escaped = Record.Exception(() => source.RaiseDispatcher(new InvalidOperationException("failure")));

        Assert.Null(escaped);
        Assert.Equal(1, shutdowns);
    }

    [Fact]
    public void ThrowingShutdown_CannotEscapeDispatcherBoundary()
    {
        var source = new FakeExceptionSource();
        using var boundary = new AppExceptionBoundary(source, new InlineAppDispatcher(), (_, _, _) => { }, () => throw new InvalidOperationException("shutdown"));
        boundary.Attach();

        Assert.Null(Record.Exception(() => source.RaiseDispatcher(new InvalidOperationException("failure"))));
    }

    [Fact]
    public async Task BackgroundFailureRaisedOffDispatcher_IsMarshalledWithoutDeadlock()
    {
        using var dispatcher = new DispatcherThread();
        var source = new FakeExceptionSource();
        var routed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var boundary = new AppExceptionBoundary(
            source,
            new WpfAppDispatcher(dispatcher.Dispatcher),
            (_, _, _) => routed.TrySetResult(Environment.CurrentManagedThreadId),
            () => { });
        boundary.Attach();

        await Task.Run(() => source.RaiseDomain(new InvalidOperationException("background")), TestContext.Current.CancellationToken);

        Assert.Equal(dispatcher.ThreadId, await routed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CanceledDispatch_ReleasesReentrancyGateAndForegroundStillRequestsShutdown()
    {
        var source = new FakeExceptionSource();
        var dispatcher = new CancelOnceDispatcher();
        var routed = 0;
        var shutdowns = 0;
        using var boundary = new AppExceptionBoundary(source, dispatcher, (_, _, _) => routed++, () => shutdowns++);
        boundary.Attach();

        source.RaiseDispatcher(new InvalidOperationException("canceled"));
        source.RaiseDispatcher(new InvalidOperationException("next"));

        Assert.Equal(1, routed);
        Assert.Equal(1, shutdowns);
    }

    private sealed class FakeExceptionSource : IAppExceptionSource
    {
        private EventHandler<AppDispatcherUnhandledEventArgs>? _dispatcher;
        private EventHandler<AppBackgroundUnhandledEventArgs>? _domain;
        private EventHandler<AppBackgroundUnhandledEventArgs>? _task;
        public int DispatcherSubscribers { get; private set; }
        public int DomainSubscribers { get; private set; }
        public int TaskSubscribers { get; private set; }
        public event EventHandler<AppDispatcherUnhandledEventArgs>? DispatcherUnhandled { add { _dispatcher += value; DispatcherSubscribers++; } remove { _dispatcher -= value; DispatcherSubscribers--; } }
        public event EventHandler<AppBackgroundUnhandledEventArgs>? DomainUnhandled { add { _domain += value; DomainSubscribers++; } remove { _domain -= value; DomainSubscribers--; } }
        public event EventHandler<AppBackgroundUnhandledEventArgs>? TaskUnhandled { add { _task += value; TaskSubscribers++; } remove { _task -= value; TaskSubscribers--; } }
        public AppDispatcherUnhandledEventArgs RaiseDispatcher(Exception exception) { var args = new AppDispatcherUnhandledEventArgs(exception); _dispatcher?.Invoke(this, args); return args; }
        public void RaiseDomain(Exception exception) => _domain?.Invoke(this, new AppBackgroundUnhandledEventArgs(exception));
        public AppBackgroundUnhandledEventArgs RaiseTask(Exception exception) { var args = new AppBackgroundUnhandledEventArgs(exception); _task?.Invoke(this, args); return args; }
    }

    private sealed class DispatcherThread : IDisposable
    {
        private readonly Thread _thread;
        public DispatcherThread()
        {
            var ready = new TaskCompletionSource<System.Windows.Threading.Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread = new Thread(() => { ready.SetResult(System.Windows.Threading.Dispatcher.CurrentDispatcher); System.Windows.Threading.Dispatcher.Run(); }) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            Dispatcher = ready.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            ThreadId = _thread.ManagedThreadId;
        }
        public System.Windows.Threading.Dispatcher Dispatcher { get; }
        public int ThreadId { get; }
        public void Dispose() { Dispatcher.InvokeShutdown(); _thread.Join(TimeSpan.FromSeconds(2)); }
    }

    private sealed class CancelOnceDispatcher : IAppDispatcher
    {
        private int _calls;
        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1) return Task.FromCanceled(new CancellationToken(canceled: true));
            action();
            return Task.CompletedTask;
        }
    }
}
