using System.Buffers.Binary;
using System.Text;

namespace LumaTherm.Infrastructure.Sensors;

public static class MahmSnapshotParser
{
    public const uint ValidSignature = 0x4D48414D;
    public const uint MinimumVersion = 0x00020000;
    public const uint GpuTemperatureSourceId = 0;

    private const int HeaderLength = 32;
    private const uint MinimumMonitoringEntrySize = 1324;
    private const uint MinimumGpuEntrySize = 1304;
    private const int MonitoringDataOffset = 1300;
    private const int MonitoringGpuIndexOffset = 1316;
    private const int MonitoringSourceIdOffset = 1320;
    private const int GpuDescriptionOffset = 520;
    private const int GpuDescriptionLength = 256;
    private const ulong MaximumSnapshotLength = 4 * 1024 * 1024;

    public static bool TryParseGpuTemperature(ReadOnlySpan<byte> snapshot, out double temperature, out string deviceName)
    {
        temperature = default;
        deviceName = string.Empty;

        if (snapshot.Length < HeaderLength ||
            BinaryPrimitives.ReadUInt32LittleEndian(snapshot) != ValidSignature ||
            BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Slice(4, sizeof(uint))) < MinimumVersion)
        {
            return false;
        }

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Slice(8, sizeof(uint)));
        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Slice(12, sizeof(uint)));
        var entrySize = BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Slice(16, sizeof(uint)));
        var gpuEntryCount = BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Slice(24, sizeof(uint)));
        var gpuEntrySize = BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Slice(28, sizeof(uint)));
        if (!TryGetLayout(snapshot.Length, headerSize, entryCount, entrySize, gpuEntryCount, gpuEntrySize, out var monitoringEntriesOffset, out var gpuEntriesOffset))
        {
            return false;
        }

        deviceName = DecodeAscii(snapshot.Slice(gpuEntriesOffset + GpuDescriptionOffset, GpuDescriptionLength));
        for (uint index = 0; index < entryCount; index++)
        {
            var entryOffset = checked(monitoringEntriesOffset + checked((int)(index * entrySize)));
            var gpuIndex = BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Slice(entryOffset + MonitoringGpuIndexOffset, sizeof(uint)));
            var sourceId = BinaryPrimitives.ReadUInt32LittleEndian(snapshot.Slice(entryOffset + MonitoringSourceIdOffset, sizeof(uint)));
            if (gpuIndex != 0 || sourceId != GpuTemperatureSourceId)
            {
                continue;
            }

            var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(snapshot.Slice(entryOffset + MonitoringDataOffset, sizeof(float))));
            if (!float.IsFinite(value) || value < 0 || value > 120)
            {
                return false;
            }

            temperature = value;
            return true;
        }

        return false;
    }

    private static bool TryGetLayout(
        int snapshotLength,
        uint headerSize,
        uint entryCount,
        uint entrySize,
        uint gpuEntryCount,
        uint gpuEntrySize,
        out int monitoringEntriesOffset,
        out int gpuEntriesOffset)
    {
        monitoringEntriesOffset = 0;
        gpuEntriesOffset = 0;
        if (headerSize < HeaderLength || entrySize < MinimumMonitoringEntrySize || gpuEntrySize < MinimumGpuEntrySize || gpuEntryCount == 0)
        {
            return false;
        }

        var entryBytes = checked((ulong)entryCount * entrySize);
        var gpuEntryBytes = checked((ulong)gpuEntryCount * gpuEntrySize);
        if (headerSize > MaximumSnapshotLength || entryBytes > MaximumSnapshotLength || gpuEntryBytes > MaximumSnapshotLength)
        {
            return false;
        }

        var totalLength = checked((ulong)headerSize + entryBytes + gpuEntryBytes);
        if (totalLength > MaximumSnapshotLength || totalLength > (ulong)snapshotLength)
        {
            return false;
        }

        monitoringEntriesOffset = checked((int)headerSize);
        gpuEntriesOffset = checked((int)(headerSize + entryBytes));
        return true;
    }

    private static string DecodeAscii(ReadOnlySpan<byte> bytes)
    {
        var terminator = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(bytes.Slice(0, terminator >= 0 ? terminator : bytes.Length));
    }
}
