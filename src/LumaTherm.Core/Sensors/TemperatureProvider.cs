using System.Runtime.ExceptionServices;

namespace LumaTherm.Core.Sensors;

public sealed class TemperatureProvider : ITemperatureProvider
{
    private readonly IReadOnlyList<ITemperatureSource> _sources;

    public TemperatureProvider(IReadOnlyList<ITemperatureSource> sources) =>
        _sources = sources.Count > 0 ? sources : throw new ArgumentException("At least one source is required.", nameof(sources));

    public async ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken)
    {
        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reading = await source.TryReadAsync(cancellationToken).ConfigureAwait(false);
                if (reading is not null)
                {
                    return reading;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // A failed hardware source must not prevent reading the next one.
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception>? failures = null;
        foreach (var source in _sources)
        {
            try
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(failures);
        }
    }
}
