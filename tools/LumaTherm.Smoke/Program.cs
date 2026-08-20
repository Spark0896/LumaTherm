using LumaTherm.Core.Lighting;
using LumaTherm.Core.Sensors;
using LumaTherm.Infrastructure.Lighting;
using LumaTherm.Infrastructure.Sensors;

namespace LumaTherm.Smoke;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        await using var normal = new TemperatureProvider(
        [
            new NvmlTemperatureSource(new NvmlApi(), TimeProvider.System),
            new AfterburnerTemperatureSource(new MahmMemoryReader(), TimeProvider.System),
        ]);
        await using var fallback = new TemperatureProvider(
        [new AfterburnerTemperatureSource(new MahmMemoryReader(), TimeProvider.System)]);

        if (args.FirstOrDefault() is "lights" or "cycle" or "simulate")
        {
            await using var lights = new LampArrayLightingController(new WindowsLampArrayPlatform());
            return await SmokeCommand.RunAsync(args, normal, fallback, lights, Console.Out, Console.In, cancellation.Token).ConfigureAwait(false);
        }

        await using var noLights = new NoLightingController();
        return await SmokeCommand.RunAsync(args, normal, fallback, noLights, Console.Out, Console.In, cancellation.Token).ConfigureAwait(false);
    }

    private sealed class NoLightingController : ILightingController
    {
        public bool IsConnected => false;
        public LightingDeviceInfo? ConnectedDevice => null;
        public event EventHandler? DevicesChanged
        {
            add { }
            remove { }
        }

        public Task<IReadOnlyList<LightingDeviceInfo>> DiscoverAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LightingDeviceInfo>>([]);
        public Task<bool> ConnectAsync(string? preferredDeviceId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SetColorAsync(LumaTherm.Core.Colors.RgbColor color, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReleaseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
