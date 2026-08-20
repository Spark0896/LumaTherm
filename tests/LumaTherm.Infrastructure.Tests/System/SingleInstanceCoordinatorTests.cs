using System.IO.Pipes;
using System.Text;
using LumaTherm.Infrastructure.System;

namespace LumaTherm.Infrastructure.Tests.System;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public async Task SecondInstance_SignalsFirstInstanceWithinBoundedTime()
    {
        var name = $"LumaTherm-Test-{Guid.NewGuid():N}";
        using var timeout = CreateTimeout();
        await using var first = new SingleInstanceCoordinator(name);
        await using var second = new SingleInstanceCoordinator(name);
        var signaled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ActivationRequested += (_, _) => signaled.TrySetResult();

        Assert.True(await first.TryAcquireAsync(timeout.Token));
        Assert.False(await second.TryAcquireAsync(timeout.Token));
        await second.SignalActivationAsync(timeout.Token);

        await signaled.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Server_IgnoresMalformedAndOversizedMessages()
    {
        var name = $"LumaTherm-Test-{Guid.NewGuid():N}";
        using var timeout = CreateTimeout();
        await using var owner = new SingleInstanceCoordinator(name);
        var count = 0;
        owner.ActivationRequested += (_, _) => Interlocked.Increment(ref count);
        Assert.True(await owner.TryAcquireAsync(timeout.Token));

        await SendRawAsync(name, "show\n");
        try
        {
            await SendRawAsync(name, new string('X', 100) + "\n");
        }
        catch (IOException)
        {
            // Immediate rejection may close the pipe while the client is still writing.
        }
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(0, Volatile.Read(ref count));
    }

    [Fact]
    public async Task HeldOpenOversizedClient_IsRejectedSoLaterShowCanConnect()
    {
        var name = $"LumaTherm-Test-{Guid.NewGuid():N}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await using var owner = new SingleInstanceCoordinator(name);
        await using var sender = new SingleInstanceCoordinator(name);
        var signaled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.ActivationRequested += (_, _) => signaled.TrySetResult();
        Assert.True(await owner.TryAcquireAsync(timeout.Token));
        Assert.False(await sender.TryAcquireAsync(timeout.Token));

        var heldOpen = new NamedPipeClientStream(".", $"{name}.Activation", PipeDirection.Out, PipeOptions.Asynchronous);
        try
        {
            await heldOpen.ConnectAsync(timeout.Token);
            await heldOpen.WriteAsync(Encoding.UTF8.GetBytes(new string('X', 17)), timeout.Token);
            await heldOpen.FlushAsync(timeout.Token);

            await sender.SignalActivationAsync(timeout.Token);
            await signaled.Task.WaitAsync(TimeSpan.FromSeconds(1), timeout.Token);
        }
        finally
        {
            await heldOpen.DisposeAsync();
        }
    }

    [Fact]
    public async Task RepeatedSignals_AreSerialized_AndHandlerFailuresAreObserved()
    {
        var name = $"LumaTherm-Test-{Guid.NewGuid():N}";
        using var timeout = CreateTimeout();
        var failures = new List<Exception>();
        await using var owner = new SingleInstanceCoordinator(name, failures.Add);
        await using var sender = new SingleInstanceCoordinator(name);
        var inHandler = 0;
        var overlap = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        owner.ActivationRequested += (_, _) =>
        {
            if (Interlocked.Increment(ref inHandler) != 1) Interlocked.Exchange(ref overlap, 1);
            var current = Interlocked.Increment(ref count);
            Thread.Sleep(30);
            Interlocked.Decrement(ref inHandler);
            if (current == 1) throw new InvalidOperationException("activation failed");
            if (current == 3) completed.TrySetResult();
        };
        Assert.True(await owner.TryAcquireAsync(timeout.Token));
        Assert.False(await sender.TryAcquireAsync(timeout.Token));

        await sender.SignalActivationAsync(timeout.Token);
        await sender.SignalActivationAsync(timeout.Token);
        await sender.SignalActivationAsync(timeout.Token);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.Equal(0, Volatile.Read(ref overlap));
        Assert.Single(failures);
    }

    [Fact]
    public async Task CrossThreadDispose_ReleasesMutexForLaterCoordinator_AndIsIdempotent()
    {
        var name = $"LumaTherm-Test-{Guid.NewGuid():N}";
        using var timeout = CreateTimeout();
        await using var first = new SingleInstanceCoordinator(name);
        Assert.True(await first.TryAcquireAsync(timeout.Token));

        await Task.Run(async () => await first.DisposeAsync(), TestContext.Current.CancellationToken);
        await first.DisposeAsync();

        await using var later = new SingleInstanceCoordinator(name);
        Assert.True(await later.TryAcquireAsync(timeout.Token));
    }

    [Fact]
    public async Task Dispose_CancelsServerAndPreventsFurtherCallbacks()
    {
        var name = $"LumaTherm-Test-{Guid.NewGuid():N}";
        using var timeout = CreateTimeout();
        await using var owner = new SingleInstanceCoordinator(name);
        var callbacks = 0;
        owner.ActivationRequested += (_, _) => Interlocked.Increment(ref callbacks);
        Assert.True(await owner.TryAcquireAsync(timeout.Token));

        await owner.DisposeAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => SendRawAsync(name, "SHOW\n", TimeSpan.FromMilliseconds(200)));
        Assert.Equal(0, Volatile.Read(ref callbacks));
    }

    private static async Task SendRawAsync(string name, string text, TimeSpan? timeout = null)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(timeout ?? TimeSpan.FromSeconds(2));
        await using var pipe = new NamedPipeClientStream(".", $"{name}.Activation", PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellation.Token);
        var bytes = Encoding.UTF8.GetBytes(text);
        await pipe.WriteAsync(bytes, cancellation.Token);
        await pipe.FlushAsync(cancellation.Token);
    }

    private static CancellationTokenSource CreateTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        return timeout;
    }
}
