using System.Threading.Channels;
using LumaTherm.Core.Runtime;

namespace LumaTherm.Infrastructure.System;

public sealed class PowerEventService : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly IPowerEventSource _source;
    private readonly IThermalRuntime _runtime;
    private readonly Action<Exception>? _errorSink;
    private readonly Channel<PowerEventKind> _events;
    private readonly CancellationTokenSource _operationCancellation = new();
    private readonly TimeSpan _shutdownTimeout;
    private readonly Task _consumer;
    private bool _disposed;

    public PowerEventService(
        IPowerEventSource source,
        IThermalRuntime runtime,
        Action<Exception>? errorSink = null,
        TimeSpan? shutdownTimeout = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _errorSink = errorSink;
        _shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(2);
        if (_shutdownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
        }
        _events = Channel.CreateUnbounded<PowerEventKind>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _source.PowerEvent += OnPowerEvent;
        _consumer = ConsumeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _source.PowerEvent -= OnPowerEvent;
            _disposed = true;
            _events.Writer.TryComplete();
        }

        try
        {
            await _consumer.WaitAsync(_shutdownTimeout);
        }
        catch (TimeoutException exception)
        {
            _errorSink?.Invoke(exception);
            _operationCancellation.Cancel();
            try
            {
                await _consumer.WaitAsync(_shutdownTimeout);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException cancellationTimeout)
            {
                _errorSink?.Invoke(cancellationTimeout);
            }
        }
        catch (Exception exception)
        {
            _errorSink?.Invoke(exception);
        }
        finally
        {
            _source.Dispose();
            _operationCancellation.Dispose();
        }
    }

    private void OnPowerEvent(object? sender, PowerEventArgs args)
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _events.Writer.TryWrite(args.Kind);
            }
        }
    }

    private async Task ConsumeAsync()
    {
        await foreach (var powerEvent in _events.Reader.ReadAllAsync())
        {
            try
            {
                if (powerEvent == PowerEventKind.Suspend)
                {
                    await _runtime.SuspendAsync(_operationCancellation.Token);
                }
                else
                {
                    await _runtime.ResumeAsync(_operationCancellation.Token);
                }
            }
            catch (OperationCanceledException) when (_operationCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _errorSink?.Invoke(exception);
            }
        }
    }
}
