namespace LumaTherm.Core.Runtime;

public interface ILightingTestSession : IAsyncDisposable
{
    Task SetTemperatureAsync(double celsius, CancellationToken cancellationToken);
}
