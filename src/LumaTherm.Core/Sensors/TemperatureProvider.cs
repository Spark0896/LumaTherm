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
            var reading = await source.TryReadAsync(cancellationToken);
            if (reading is not null)
            {
                return reading;
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var source in _sources)
        {
            await source.DisposeAsync();
        }
    }
}
