using LumaTherm.Core.Lighting;

namespace LumaTherm.App.Services;

public interface ILightingDeviceDiscovery
{
    Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken);
}

public sealed class LightingDeviceDiscoveryService(ILightingController lightingController) : ILightingDeviceDiscovery
{
    private readonly ILightingController _lightingController = lightingController ?? throw new ArgumentNullException(nameof(lightingController));

    public Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken) =>
        _lightingController.DiscoverAsync(cancellationToken);
}

internal sealed class EmptyLightingDeviceDiscovery : ILightingDeviceDiscovery
{
    public static EmptyLightingDeviceDiscovery Instance { get; } = new();

    private EmptyLightingDeviceDiscovery()
    {
    }

    public Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LightingDeviceInfo>>([]);
}
