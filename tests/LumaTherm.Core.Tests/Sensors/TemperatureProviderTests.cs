using LumaTherm.Core.Sensors;

namespace LumaTherm.Core.Tests.Sensors;

public sealed class TemperatureProviderTests
{
    [Fact]
    public async Task TryRead_FallsBackToAfterburnerThenReturnsToNvmlOnTheNextPoll()
    {
        var nvml = new FakeTemperatureSource("NVML", null);
        var afterburner = new FakeTemperatureSource("MSI Afterburner", new TemperatureReading(67, "MSI Afterburner", "GPU 0", DateTimeOffset.UnixEpoch));
        await using var provider = new TemperatureProvider([nvml, afterburner]);

        var fallbackReading = await provider.TryReadAsync(CancellationToken.None);
        nvml.Reading = new TemperatureReading(68, "NVML", "GPU 0", DateTimeOffset.UnixEpoch);
        var recoveredReading = await provider.TryReadAsync(CancellationToken.None);

        Assert.NotNull(fallbackReading);
        Assert.Equal("MSI Afterburner", fallbackReading.SourceName);
        Assert.Equal(67, fallbackReading.Celsius);
        Assert.NotNull(recoveredReading);
        Assert.Equal("NVML", recoveredReading.SourceName);
        Assert.Equal(68, recoveredReading.Celsius);
        Assert.Equal(2, nvml.ReadCalls);
        Assert.Equal(1, afterburner.ReadCalls);
    }

    [Fact]
    public async Task TryRead_ReturnsNullWhenNoSourcesProduceAReading()
    {
        await using var provider = new TemperatureProvider([new FakeTemperatureSource("NVML", null), new FakeTemperatureSource("MSI Afterburner", null)]);

        Assert.Null(await provider.TryReadAsync(CancellationToken.None));
    }

    private sealed class FakeTemperatureSource(string name, TemperatureReading? reading) : ITemperatureSource
    {
        public string Name => name;

        public TemperatureReading? Reading { get; set; } = reading;

        public int ReadCalls { get; private set; }

        public ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            return ValueTask.FromResult(Reading);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
