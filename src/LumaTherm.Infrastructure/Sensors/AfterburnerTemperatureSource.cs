using LumaTherm.Core.Sensors;

namespace LumaTherm.Infrastructure.Sensors;

public sealed class AfterburnerTemperatureSource : ITemperatureSource
{
    private const string SourceName = "MSI Afterburner";

    private readonly IMahmMemoryReader _memoryReader;
    private readonly TimeProvider _timeProvider;

    public AfterburnerTemperatureSource(IMahmMemoryReader memoryReader, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(memoryReader);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _memoryReader = memoryReader;
        _timeProvider = timeProvider;
    }

    public string Name => SourceName;

    public ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_memoryReader.TryRead(out var snapshot) ||
            !MahmSnapshotParser.TryParseGpuTemperature(snapshot, out var temperature, out var deviceName))
        {
            return ValueTask.FromResult<TemperatureReading?>(null);
        }

        return ValueTask.FromResult<TemperatureReading?>(new TemperatureReading(temperature, SourceName, deviceName, _timeProvider.GetUtcNow()));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
