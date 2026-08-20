using System.IO.Pipes;
using System.Text;

namespace LumaTherm.Infrastructure.System;

public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private const int MaximumMessageBytes = 16;
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);
    private readonly object _sync = new();
    private readonly string _mutexName;
    private readonly string _pipeName;
    private readonly Action<Exception>? _errorSink;
    private readonly CancellationTokenSource _serverCancellation = new();
    private readonly ManualResetEventSlim _releaseOwnership = new(false);
    private readonly TaskCompletionSource<bool> _ownership = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _ownershipThread;
    private Task? _serverTask;
    private bool _acquireStarted;
    private bool _disposed;

    public SingleInstanceCoordinator(string? instanceName = null, Action<Exception>? errorSink = null)
    {
        var prefix = string.IsNullOrWhiteSpace(instanceName) ? "LumaTherm" : instanceName;
        _mutexName = $@"Local\{prefix}.SingleInstance";
        _pipeName = $"{prefix}.Activation";
        _errorSink = errorSink;
    }

    public event EventHandler? ActivationRequested;

    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_acquireStarted)
            {
                _acquireStarted = true;
                _ownershipThread = new Thread(OwnershipThreadMain)
                {
                    IsBackground = true,
                    Name = "LumaTherm single-instance ownership",
                };
                _ownershipThread.Start();
            }
        }

        var acquired = await _ownership.Task.WaitAsync(cancellationToken);
        if (acquired)
        {
            lock (_sync)
            {
                if (!_disposed && _serverTask is null)
                {
                    _serverTask = Task.Run(() => ServeAsync(_serverCancellation.Token), CancellationToken.None);
                }
            }
        }

        return acquired;
    }

    public async Task SignalActivationAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.Out,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(cancellationToken);
        await pipe.WriteAsync("SHOW\n"u8.ToArray(), cancellationToken);
        await pipe.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Task? serverTask;
        Thread? ownershipThread;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ActivationRequested = null;
            _serverCancellation.Cancel();
            serverTask = _serverTask;
            ownershipThread = _ownershipThread;
        }

        if (serverTask is not null)
        {
            try
            {
                await serverTask.WaitAsync(ShutdownTimeout);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException exception)
            {
                _errorSink?.Invoke(exception);
            }
            catch (Exception exception)
            {
                _errorSink?.Invoke(exception);
            }
        }

        _releaseOwnership.Set();
        if (ownershipThread is not null && !ownershipThread.Join(ShutdownTimeout))
        {
            _errorSink?.Invoke(new TimeoutException("Single-instance ownership thread did not stop in time."));
        }

        _serverCancellation.Dispose();
        _releaseOwnership.Dispose();
    }

    private void OwnershipThreadMain()
    {
        try
        {
            using var mutex = new Mutex(initiallyOwned: false, _mutexName);
            var acquired = false;
            try
            {
                acquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            _ownership.TrySetResult(acquired);
            if (!acquired)
            {
                return;
            }

            _releaseOwnership.Wait();
            mutex.ReleaseMutex();
        }
        catch (Exception exception)
        {
            _ownership.TrySetException(exception);
            _errorSink?.Invoke(exception);
        }
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var server = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
                if (await ReadExactShowAsync(server, cancellationToken))
                {
                    RaiseActivationRequested();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _errorSink?.Invoke(exception);
            }
        }
    }

    private void RaiseActivationRequested()
    {
        EventHandler? handlers;
        lock (_sync)
        {
            handlers = _disposed ? null : ActivationRequested;
        }

        if (handlers is null)
        {
            return;
        }

        try
        {
            handlers(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _errorSink?.Invoke(exception);
        }
    }

    private static async Task<bool> ReadExactShowAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new byte[MaximumMessageBytes];
        var length = 0;
        var oneByte = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(oneByte, cancellationToken);
            if (read == 0)
            {
                return false;
            }

            if (oneByte[0] == (byte)'\n')
            {
                break;
            }

            if (length >= MaximumMessageBytes)
            {
                return false;
            }
            bytes[length++] = oneByte[0];
        }

        try
        {
            return string.Equals(
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes, 0, length),
                "SHOW",
                StringComparison.Ordinal);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
