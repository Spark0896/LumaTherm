using LumaTherm.Core.Colors;

namespace LumaTherm.Infrastructure.Lighting;

public interface ILampArrayPlatform : IAsyncDisposable
{
    event EventHandler? DevicesChanged;

    Task<IReadOnlyList<ILampArrayHandle>> FindAllAsync(CancellationToken cancellationToken);
}

public interface ILampArrayHandle
{
    string Id { get; }
    string Name { get; }
    int LampCount { get; }
    bool IsAvailable { get; }
    bool IsPresent { get; }

    void Enable();
    void SetColor(RgbColor color);
    void Disable();
}
