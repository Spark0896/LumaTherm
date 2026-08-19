using LumaTherm.Infrastructure.Sensors;

namespace LumaTherm.Infrastructure.Tests.Sensors;

public sealed class NvmlTemperatureSourceTests
{
    [Fact]
    public async Task TryRead_ReturnsGpuTemperatureAndName()
    {
        var api = new FakeNvmlApi(0, 68, "NVIDIA GeForce RTX 5070");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        await using var source = new NvmlTemperatureSource(api, clock);

        var reading = await source.TryReadAsync(CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal(68, reading.Celsius);
        Assert.Equal("NVML", reading.SourceName);
        Assert.Equal("NVIDIA GeForce RTX 5070", reading.DeviceName);
        Assert.Equal(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero), reading.Timestamp);
    }

    [Fact]
    public async Task TryRead_DecodesNullTerminatedUtf8DeviceName()
    {
        var api = new FakeNvmlApi(0, 68, "NVIDIA GeForce RTX 5070 Ti™");
        await using var source = new NvmlTemperatureSource(api, TimeProvider.System);

        var reading = await source.TryReadAsync(CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal("NVIDIA GeForce RTX 5070 Ti™", reading.DeviceName);
    }

    [Fact]
    public async Task TryRead_WhenInitializationFails_ReturnsNullWithoutThrowing()
    {
        var api = new FakeNvmlApi(3, 0, string.Empty);
        await using var source = new NvmlTemperatureSource(api, TimeProvider.System);

        Assert.Null(await source.TryReadAsync(CancellationToken.None));
        Assert.Equal(1, api.InitializeCalls);
    }

    [Fact]
    public async Task TryRead_RetriesInitializationAfterTransientFailure()
    {
        var api = new RecoveringFakeNvmlApi(firstInitializeResult: 3, temperature: 67);
        await using var source = new NvmlTemperatureSource(api, TimeProvider.System);

        Assert.Null(await source.TryReadAsync(CancellationToken.None));
        Assert.NotNull(await source.TryReadAsync(CancellationToken.None));
        Assert.Equal(2, api.InitializeCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task TryRead_WhenNativeOperationReturnsError_ReturnsNull(int failedOperation)
    {
        var api = new FakeNvmlApi(0, 68, "NVIDIA GeForce RTX 5070");
        switch (failedOperation)
        {
            case 0:
                api.DeviceHandleResult = 3;
                break;
            case 1:
                api.DeviceNameResult = 3;
                break;
            default:
                api.TemperatureResult = 3;
                break;
        }

        await using var source = new NvmlTemperatureSource(api, TimeProvider.System);

        Assert.Null(await source.TryReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TryRead_WhenTemperatureIsOutsideSupportedRange_ReturnsNull()
    {
        var api = new FakeNvmlApi(0, 121, "NVIDIA GeForce RTX 5070");
        await using var source = new NvmlTemperatureSource(api, TimeProvider.System);

        Assert.Null(await source.TryReadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(typeof(DllNotFoundException))]
    [InlineData(typeof(EntryPointNotFoundException))]
    [InlineData(typeof(BadImageFormatException))]
    public async Task TryRead_WhenNativeLoadingFails_ReturnsNull(Type exceptionType)
    {
        var api = new FakeNvmlApi(0, 68, "NVIDIA GeForce RTX 5070")
        {
            ExceptionToThrow = (Exception)Activator.CreateInstance(exceptionType)!
        };
        await using var source = new NvmlTemperatureSource(api, TimeProvider.System);

        Assert.Null(await source.TryReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DisposeAsync_AfterSuccessfulInitialization_ShutsDownExactlyOnce()
    {
        var api = new FakeNvmlApi(0, 68, "NVIDIA GeForce RTX 5070");
        var source = new NvmlTemperatureSource(api, TimeProvider.System);

        Assert.NotNull(await source.TryReadAsync(CancellationToken.None));
        await source.DisposeAsync();
        await source.DisposeAsync();

        Assert.Equal(1, api.ShutdownCalls);
    }

    [Fact]
    public async Task TryRead_AfterDispose_ReturnsNull()
    {
        var source = new NvmlTemperatureSource(new FakeNvmlApi(0, 68, "NVIDIA GeForce RTX 5070"), TimeProvider.System);

        await source.DisposeAsync();

        Assert.Null(await source.TryReadAsync(CancellationToken.None));
    }

    private class FakeNvmlApi : INvmlApi
    {
        private readonly Queue<int> _initializeResults;
        private readonly uint _temperature;
        private readonly string _deviceName;

        public FakeNvmlApi(int initializeResult, uint temperature, string deviceName)
        {
            _initializeResults = new Queue<int>([initializeResult]);
            _temperature = temperature;
            _deviceName = deviceName;
        }

        public int InitializeCalls { get; private set; }

        public int ShutdownCalls { get; private set; }

        public int DeviceHandleResult { get; set; }

        public int DeviceNameResult { get; set; }

        public int TemperatureResult { get; set; }

        public Exception? ExceptionToThrow { get; set; }

        public int Initialize()
        {
            ThrowIfNeeded();
            InitializeCalls++;
            return _initializeResults.Count > 0 ? _initializeResults.Dequeue() : 0;
        }

        public int Shutdown()
        {
            ShutdownCalls++;
            return 0;
        }

        public int GetDeviceHandle(uint index, out nint handle)
        {
            ThrowIfNeeded();
            handle = (nint)42;
            return DeviceHandleResult;
        }

        public int GetDeviceName(nint handle, byte[] buffer)
        {
            ThrowIfNeeded();
            var bytes = System.Text.Encoding.UTF8.GetBytes(_deviceName);
            Array.Copy(bytes, buffer, bytes.Length);
            return DeviceNameResult;
        }

        public int GetTemperature(nint handle, out uint result)
        {
            ThrowIfNeeded();
            result = _temperature;
            return TemperatureResult;
        }

        private void ThrowIfNeeded()
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }
        }
    }

    private sealed class RecoveringFakeNvmlApi : FakeNvmlApi
    {
        public RecoveringFakeNvmlApi(int firstInitializeResult, uint temperature)
            : base(firstInitializeResult, temperature, "NVIDIA GeForce RTX 5070")
        {
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
