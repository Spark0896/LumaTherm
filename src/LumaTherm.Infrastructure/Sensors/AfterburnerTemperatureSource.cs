using System.Buffers.Binary;
using LumaTherm.Core.Sensors;

namespace LumaTherm.Infrastructure.Sensors;

public sealed class AfterburnerTemperatureSource : ITemperatureSource
{
    private const string SourceName = "MSI Afterburner";
    private static readonly TimeSpan MaximumPollingAge = TimeSpan.FromSeconds(5);

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

        // MAHM v2 stores a signed 32-bit Unix last-polling time at header offset 20.
        var pollingTime = BinaryPrimitives.ReadInt32LittleEndian(snapshot.AsSpan(20, sizeof(int)));
        if (pollingTime <= 0)
        {
            return ValueTask.FromResult<TemperatureReading?>(null);
        }

        var polledAt = DateTimeOffset.FromUnixTimeSeconds(pollingTime);
        var age = _timeProvider.GetUtcNow() - polledAt;
        if (age < TimeSpan.Zero || age > MaximumPollingAge)
        {
            return ValueTask.FromResult<TemperatureReading?>(null);
        }

        return ValueTask.FromResult<TemperatureReading?>(new TemperatureReading(temperature, SourceName, deviceName, polledAt));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
