using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;

namespace LumaTherm.Infrastructure.Sensors;

public sealed class MahmMemoryReader : IMahmMemoryReader
{
    private const string MapName = "Global\\MAHMSharedMemory";
    private const int HeaderLength = 32;
    private const ulong MaximumSnapshotLength = 4 * 1024 * 1024;
    private readonly string _mapName;

    public MahmMemoryReader()
        : this(MapName)
    {
    }

    public MahmMemoryReader(string mapName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);
        _mapName = mapName;
    }

    public bool TryRead(out byte[] snapshot)
    {
        snapshot = Array.Empty<byte>();
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var memory = MemoryMappedFile.OpenExisting(_mapName, MemoryMappedFileRights.Read);
            using var view = memory.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            var header = new byte[HeaderLength];
            view.ReadArray(0, header, 0, header.Length);

            if (!TryGetSnapshotLength(header, out var snapshotLength))
            {
                return false;
            }

            if (snapshotLength > view.Capacity)
            {
                return false;
            }

            snapshot = new byte[snapshotLength];
            view.ReadArray(0, snapshot, 0, snapshot.Length);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryGetSnapshotLength(ReadOnlySpan<byte> header, out int snapshotLength)
    {
        snapshotLength = 0;
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8, sizeof(uint)));
        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(12, sizeof(uint)));
        var entrySize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(16, sizeof(uint)));
        var gpuEntryCount = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(24, sizeof(uint)));
        var gpuEntrySize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(28, sizeof(uint)));

        if (headerSize != HeaderLength)
        {
            return false;
        }

        var entryBytes = checked((ulong)entryCount * entrySize);
        var gpuEntryBytes = checked((ulong)gpuEntryCount * gpuEntrySize);
        if (headerSize > MaximumSnapshotLength || entryBytes > MaximumSnapshotLength || gpuEntryBytes > MaximumSnapshotLength)
        {
            return false;
        }

        var total = checked((ulong)headerSize + entryBytes + gpuEntryBytes);
        if (total > MaximumSnapshotLength)
        {
            return false;
        }

        snapshotLength = checked((int)total);
        return snapshotLength >= HeaderLength;
    }
}
