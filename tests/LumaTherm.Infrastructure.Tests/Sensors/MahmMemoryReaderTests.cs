using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using LumaTherm.Infrastructure.Sensors;

namespace LumaTherm.Infrastructure.Tests.Sensors;

public sealed class MahmMemoryReaderTests
{
    [Fact]
    public void TryRead_WhenHeaderSizeIsNot32_ReturnsFalse()
    {
        var mapName = $"Local\\LumaThermMahm_{Guid.NewGuid():N}";
        using var memory = MemoryMappedFile.CreateNew(mapName, 33 + 1324 + 1304, MemoryMappedFileAccess.ReadWrite);
        using var view = memory.CreateViewAccessor(0, 33 + 1324 + 1304, MemoryMappedFileAccess.Write);
        var header = CreateHeader(33);
        view.WriteArray(0, header, 0, header.Length);
        var reader = new MahmMemoryReader(mapName);

        var read = reader.TryRead(out var snapshot);

        Assert.False(read);
        Assert.Empty(snapshot);
    }

    [Fact]
    public void TryRead_WhenDeclaredSnapshotExceedsViewCapacity_ReturnsFalseWithoutCopying()
    {
        var mapName = $"Local\\LumaThermMahm_{Guid.NewGuid():N}";
        using var memory = MemoryMappedFile.CreateNew(mapName, 32, MemoryMappedFileAccess.ReadWrite);
        using var view = memory.CreateViewAccessor(0, 32, MemoryMappedFileAccess.Write);
        var header = CreateHeader(32, entryCount: 3000);
        view.WriteArray(0, header, 0, header.Length);
        var reader = new MahmMemoryReader(mapName);

        var read = reader.TryRead(out var snapshot);

        Assert.False(read);
        Assert.Empty(snapshot);
    }

    private static byte[] CreateHeader(uint headerSize, uint entryCount = 1)
    {
        var header = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0), 0x4D48414D);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 0x00020000);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), headerSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), entryCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 1324);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), 1304);
        return header;
    }
}
