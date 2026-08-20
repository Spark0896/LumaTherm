using LumaTherm.App.Composition;

namespace LumaTherm.App.Tests.Composition;

public sealed class AppExceptionBoundaryTests
{
    [Fact]
    public void AttachDetach_AreExactlyOnce()
    {
        var source = new FakeExceptionSource();
        using var boundary = new AppExceptionBoundary(source, (_, _, _) => { }, () => { });

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
        using var boundary = new AppExceptionBoundary(source, (_, foreground, notify) => routed.Add((foreground, notify)), () => shutdowns++);
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
        using var boundary = new AppExceptionBoundary(source, (_, foreground, notify) => routed.Add((foreground, notify)), () => { });
        boundary.Attach();

        source.RaiseDomain(new InvalidOperationException("domain"));
        var task = source.RaiseTask(new InvalidOperationException("task"));

        Assert.True(task.Observed);
        Assert.Equal([(false, true), (false, false)], routed);
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
}
