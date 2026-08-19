using LumaTherm.Core.Colors;

namespace LumaTherm.Core.Lighting;

public interface ILightingController : IAsyncDisposable
{
    bool IsConnected { get; }
    LightingDeviceInfo? ConnectedDevice { get; }
    event EventHandler? DevicesChanged;
    Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken);
    Task<bool> ConnectAsync(string? preferredDeviceId, CancellationToken cancellationToken);
    Task SetColorAsync(RgbColor color, CancellationToken cancellationToken);
    Task ReleaseAsync(CancellationToken cancellationToken);
}
