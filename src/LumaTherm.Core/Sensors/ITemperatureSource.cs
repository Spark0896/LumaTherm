namespace LumaTherm.Core.Sensors;

public interface ITemperatureSource : IAsyncDisposable
{
    string Name { get; }

    ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken);
}
