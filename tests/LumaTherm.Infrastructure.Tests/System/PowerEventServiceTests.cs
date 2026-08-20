using LumaTherm.Core.Runtime;
using LumaTherm.Core.Settings;
using LumaTherm.Infrastructure.System;

namespace LumaTherm.Infrastructure.Tests.System;

public sealed class PowerEventServiceTests
{
    [Fact]
    public async Task SuspendResume_AreAwaitedInOrderWithoutOverlap()
    {
        var source = new FakePowerEventSource();
        var runtime = new GatedRuntime();
        await using var service = new PowerEventService(source, runtime);

        source.Raise(PowerEventKind.Suspend);
        await runtime.SuspendEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        source.Raise(PowerEventKind.Resume);

        Assert.False(runtime.ResumeEntered.Task.IsCompleted);
        runtime.ReleaseSuspend.TrySetResult();
        await runtime.ResumeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal([PowerEventKind.Suspend, PowerEventKind.Resume], runtime.Events);
        Assert.Equal(0, runtime.OverlapDetected);
    }

    [Fact]
    public async Task Failure_IsObservedAndDoesNotStopLaterEvents()
    {
        var source = new FakePowerEventSource();
        var runtime = new GatedRuntime { SuspendFailure = new InvalidOperationException("sleep failed") };
        runtime.ReleaseSuspend.TrySetResult();
        var failures = new List<Exception>();
        await using var service = new PowerEventService(source, runtime, failures.Add);

        source.Raise(PowerEventKind.Suspend);
        source.Raise(PowerEventKind.Resume);

        await runtime.ResumeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Single(failures);
        Assert.Equal("sleep failed", failures[0].Message);
    }

    [Fact]
    public async Task Dispose_UnsubscribesFirstDrainsQueuedWorkAndIsIdempotent()
    {
        var source = new FakePowerEventSource();
        var runtime = new GatedRuntime();
        var service = new PowerEventService(source, runtime);
        source.Raise(PowerEventKind.Suspend);
        await runtime.SuspendEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var disposal = service.DisposeAsync().AsTask();
        Assert.Equal(0, source.SubscriberCount);
        Assert.False(disposal.IsCompleted);
        source.Raise(PowerEventKind.Resume);
        runtime.ReleaseSuspend.TrySetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await service.DisposeAsync();

        Assert.False(runtime.ResumeEntered.Task.IsCompleted);
        Assert.Equal(1, source.DisposeCalls);
    }

    [Fact]
    public async Task Dispose_CancelsAStuckRuntimeAfterFiniteDeadline()
    {
        var source = new FakePowerEventSource();
        var runtime = new GatedRuntime();
        var failures = new List<Exception>();
        var service = new PowerEventService(source, runtime, failures.Add, TimeSpan.FromMilliseconds(50));
        source.Raise(PowerEventKind.Suspend);
        await runtime.SuspendEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(1, source.DisposeCalls);
        Assert.Contains(failures, exception => exception is TimeoutException);
    }

    private sealed class FakePowerEventSource : IPowerEventSource
    {
        private EventHandler<PowerEventArgs>? _powerEvent;
        public int SubscriberCount { get; private set; }
        public int DisposeCalls { get; private set; }
        public event EventHandler<PowerEventArgs>? PowerEvent
        {
            add { _powerEvent += value; SubscriberCount++; }
            remove { _powerEvent -= value; SubscriberCount--; }
        }
        public void Raise(PowerEventKind kind) => _powerEvent?.Invoke(this, new PowerEventArgs(kind));
        public void Dispose() => DisposeCalls++;
    }

    private sealed class GatedRuntime : IThermalRuntime
    {
        private int _active;
        public TaskCompletionSource SuspendEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResumeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSuspend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<PowerEventKind> Events { get; } = [];
        public int OverlapDetected { get; private set; }
        public Exception? SuspendFailure { get; init; }
        public event EventHandler<RuntimeSnapshot>? SnapshotChanged { add { } remove { } }
        public RuntimeSnapshot CurrentSnapshot { get; } = new(RuntimeStatus.Disabled, null, null, null, null, null, DateTimeOffset.UtcNow);
        public AppSettings CurrentSettings => AppSettings.Default;

        public async Task SuspendAsync(CancellationToken cancellationToken)
        {
            Enter(PowerEventKind.Suspend);
            SuspendEntered.TrySetResult();
            try
            {
                await ReleaseSuspend.Task.WaitAsync(cancellationToken);
                if (SuspendFailure is not null) throw SuspendFailure;
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public Task ResumeAsync(CancellationToken cancellationToken)
        {
            Enter(PowerEventKind.Resume);
            ResumeEntered.TrySetResult();
            Interlocked.Decrement(ref _active);
            return Task.CompletedTask;
        }

        private void Enter(PowerEventKind kind)
        {
            if (Interlocked.Increment(ref _active) != 1) OverlapDetected = 1;
            lock (Events) Events.Add(kind);
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetModeEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
