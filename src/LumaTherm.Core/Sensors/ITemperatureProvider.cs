namespace LumaTherm.Core.Sensors;

public interface ITemperatureProvider : IAsyncDisposable
{
    ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken);
}
