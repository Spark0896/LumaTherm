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

    [Fact]
    public async Task TryRead_SourceFailureStillUsesTheHealthyFallback()
    {
        var failed = new FakeTemperatureSource("NVML", null) { ReadFailure = new IOException("source unavailable") };
        var fallback = new FakeTemperatureSource("MSI Afterburner", new TemperatureReading(67, "MSI Afterburner", "GPU 0", DateTimeOffset.UnixEpoch));
        await using var provider = new TemperatureProvider([failed, fallback]);

        var reading = await provider.TryReadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(reading);
        Assert.Equal(67, reading.Celsius);
        Assert.Equal("MSI Afterburner", reading.SourceName);
    }

    [Fact]
    public async Task TryRead_CallerCancellationDoesNotReadFallback()
    {
        using var cancellation = new CancellationTokenSource();
        var canceled = new FakeTemperatureSource("NVML", null)
        {
            BeforeRead = cancellation.Cancel,
            ReadFailure = new OperationCanceledException(cancellation.Token),
        };
        var fallback = new FakeTemperatureSource("MSI Afterburner", null);
        await using var provider = new TemperatureProvider([canceled, fallback]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await provider.TryReadAsync(cancellation.Token));

        Assert.Equal(0, fallback.ReadCalls);
    }

    [Fact]
    public async Task Dispose_FailedSourceDoesNotPreventRemainingSourcesFromBeingDisposed()
    {
        var failed = new FakeTemperatureSource("NVML", null) { DisposeFailure = new IOException("shutdown failed") };
        var fallback = new FakeTemperatureSource("MSI Afterburner", null);
        var provider = new TemperatureProvider([failed, fallback]);

        await Assert.ThrowsAsync<IOException>(async () => await provider.DisposeAsync());

        Assert.Equal(1, failed.DisposeCalls);
        Assert.Equal(1, fallback.DisposeCalls);
    }

    private sealed class FakeTemperatureSource(string name, TemperatureReading? reading) : ITemperatureSource
    {
        public string Name => name;

        public TemperatureReading? Reading { get; set; } = reading;

        public int ReadCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public Exception? ReadFailure { get; init; }
        public Exception? DisposeFailure { get; init; }
        public Action? BeforeRead { get; init; }

        public ValueTask<TemperatureReading?> TryReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            BeforeRead?.Invoke();
            if (ReadFailure is { } failure) throw failure;
            return ValueTask.FromResult(Reading);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return DisposeFailure is { } failure ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
        }
    }
}
