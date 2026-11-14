using System.Buffers.Binary;
using System.Text;
using LumaTherm.Infrastructure.Sensors;

namespace LumaTherm.Infrastructure.Tests.Sensors;

public sealed class MahmSnapshotParserTests
{
    [Fact]
    public void Parse_ReturnsGpuZeroTemperatureAndNullTerminatedDeviceName()
    {
        var buffer = CreateValidSnapshot();
        WriteSingle(buffer, 32 + 1300, 68f);
        Encoding.ASCII.GetBytes("NVIDIA GeForce RTX 5070\0trailing bytes").CopyTo(buffer, 32 + 1324 + 520);

        var parsed = MahmSnapshotParser.TryParseGpuTemperature(buffer, out var temperature, out var deviceName);

        Assert.True(parsed);
        Assert.Equal(68, temperature);
        Assert.Equal("NVIDIA GeForce RTX 5070", deviceName);
    }

    [Fact]
    public void Parse_RejectsShortHeader()
    {
        var buffer = new byte[31];

        Assert.False(MahmSnapshotParser.TryParseGpuTemperature(buffer, out _, out _));
    }

    [Fact]
    public void Parse_RejectsDeallocatedSignature()
    {
        var buffer = CreateValidSnapshot();
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 0x0000DEAD);

        Assert.False(MahmSnapshotParser.TryParseGpuTemperature(buffer, out _, out _));
    }

    [Fact]
    public void Parse_RejectsOlderFormatVersion()
    {
        var buffer = CreateValidSnapshot();
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), 0x0001FFFF);

        Assert.False(MahmSnapshotParser.TryParseGpuTemperature(buffer, out _, out _));
    }

    [Fact]
    public void Parse_RejectsNonV2HeaderSizeEvenWhenShiftedEntriesAreOtherwiseValid()
    {
        var buffer = new byte[33 + 1324 + 1304];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), 0x4D48414D);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), 0x00020000);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), 33);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), 1324);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(28), 1304);
        WriteSingle(buffer, 33 + 1300, 68f);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(33 + 1316), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(33 + 1320), 0);

        Assert.False(MahmSnapshotParser.TryParseGpuTemperature(buffer, out _, out _));
    }

    [Theory]
    [InlineData(1323, 1304)]
    [InlineData(1324, 1303)]
    public void Parse_RejectsEntriesSmallerThanOfficialLayouts(uint monitoringEntrySize, uint gpuEntrySize)
    {
        var buffer = CreateValidSnapshot();
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), monitoringEntrySize);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(28), gpuEntrySize);

        Assert.False(MahmSnapshotParser.TryParseGpuTemperature(buffer, out _, out _));
    }

    [Fact]
    public void Parse_RejectsHostileHeaderSizeThatOverflowsOffsetArithmetic()
    {
        var buffer = CreateValidSnapshot();
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), uint.MaxValue);

        Assert.False(MahmSnapshotParser.TryParseGpuTemperature(buffer, out _, out _));
    }

    [Fact]
    public void Parse_RejectsHostileEntryCountThatOverflowsTotalSize()
    {
        var buffer = CreateValidSnapshot();
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), uint.MaxValue);

        Assert.False(MahmSnapshotParser.TryParseGpuTemperature(buffer, out _, out _));
    }

    [Fact]
    public void Parse_RejectsSnapshotWhoseDeclaredEntriesExceedAvailableBytes()
    {
        var buffer = CreateValidSnapshot();
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), 2);

        Assert.False(MahmSnapshotParser.TryParseGpuTemperature(buffer, out _, out _));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.MaxValue)]
    [InlineData(-0.1f)]
    [InlineData(120.1f)]
    public void Parse_RejectsNonFiniteOrOutOfRangeTemperature(float value)
    {
        var buffer = CreateValidSnapshot();
        WriteSingle(buffer, 32 + 1300, value);

        Assert.False(MahmSnapshotParser.TryParseGpuTemperature(buffer, out _, out _));
    }

    [Fact]
    public void Parse_RejectsNonZeroGpuIndex()
    {
        var buffer = CreateValidSnapshot();
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(32 + 1316), 1);

        Assert.False(MahmSnapshotParser.TryParseGpuTemperature(buffer, out _, out _));
    }

    [Fact]
    public void Parse_RejectsNonTemperatureMonitoringSource()
    {
        var buffer = CreateValidSnapshot();
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(32 + 1320), 1);

        Assert.False(MahmSnapshotParser.TryParseGpuTemperature(buffer, out _, out _));
    }

    [Fact]
    public async Task AfterburnerSource_ReturnsParsedReadingWithProducerTimestamp()
    {
        var buffer = CreateValidSnapshot();
        WriteSingle(buffer, 32 + 1300, 67f);
        Encoding.ASCII.GetBytes("GPU 0\0").CopyTo(buffer, 32 + 1324 + 520);
        var timestamp = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(20), checked((int)timestamp.ToUnixTimeSeconds()));
        await using var source = new AfterburnerTemperatureSource(new SnapshotReader(buffer), new FixedTimeProvider(timestamp.AddSeconds(1)));

        var reading = await source.TryReadAsync(CancellationToken.None);

        Assert.NotNull(reading);
        Assert.Equal(67, reading.Celsius);
        Assert.Equal("MSI Afterburner", reading.SourceName);
        Assert.Equal("GPU 0", reading.DeviceName);
        Assert.Equal(timestamp, reading.Timestamp);
    }

    [Fact]
    public async Task AfterburnerSource_WhenProducerStopsPolling_ReturnsNoReadingForFrozenSnapshot()
    {
        var buffer = CreateValidSnapshot();
        WriteSingle(buffer, 32 + 1300, 67f);
        var timestamp = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(20), checked((int)timestamp.ToUnixTimeSeconds()));
        var clock = new FixedTimeProvider(timestamp);
        await using var source = new AfterburnerTemperatureSource(new SnapshotReader(buffer), clock);
        Assert.NotNull(await source.TryReadAsync(CancellationToken.None));

        clock.UtcNow = timestamp.AddSeconds(10);

        Assert.Null(await source.TryReadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_792_238_401)]
    public async Task AfterburnerSource_RejectsInvalidOrFuturePollingTimes(int pollingTime)
    {
        var buffer = CreateValidSnapshot();
        WriteSingle(buffer, 32 + 1300, 67f);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(20), pollingTime);
        var timestamp = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        await using var source = new AfterburnerTemperatureSource(new SnapshotReader(buffer), new FixedTimeProvider(timestamp));

        Assert.Null(await source.TryReadAsync(CancellationToken.None));
    }

    private static byte[] CreateValidSnapshot()
    {
        var buffer = new byte[32 + 1324 + 1304];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), 0x4D48414D);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), 0x00020000);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), 1324);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(28), 1304);
        WriteSingle(buffer, 32 + 1300, 68f);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(32 + 1316), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(32 + 1320), 0);
        return buffer;
    }

    private static void WriteSingle(byte[] buffer, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), BitConverter.SingleToInt32Bits(value));

    private sealed class SnapshotReader(byte[] snapshot) : IMahmMemoryReader
    {
        public bool TryRead(out byte[] result)
        {
            result = snapshot;
            return true;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
