using LumaTherm.Core.Colors;

namespace LumaTherm.Core.Runtime;

public interface ILightingTestSession : IAsyncDisposable
{
    Task SetTemperatureAsync(double celsius, CancellationToken cancellationToken);
    Task SetProfileAsync(ThermalProfile profile, CancellationToken cancellationToken);
}
