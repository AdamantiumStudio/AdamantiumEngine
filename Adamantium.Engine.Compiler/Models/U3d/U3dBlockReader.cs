using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>Splits U3D bytes into blocks (ECMA-363 9.2): a U32 type, a U32 data size, a U32 meta data size, the data,
/// zero to three bytes to the next multiple of four, the meta data, padded the same way. Every size is checked against
/// the bytes there are, so a cut or broken file fails here with where it broke.</summary>
public static class U3dBlockReader
{
    private const int HeaderSize = 12;

    /// <summary>The block at <paramref name="offset"/>, which must end by <paramref name="end"/>.</summary>
    public static U3dBlock ReadBlock(byte[] bytes, int offset, int end)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (end > bytes.Length || offset < 0 || offset > end)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "The block lies outside the bytes given");
        }

        if (end - offset < HeaderSize)
        {
            throw new InvalidDataException($"The U3D data is cut short: a block at byte {offset} has no room for its sizes");
        }

        var type = BitConverter.ToUInt32(bytes, offset);
        var dataSize = (long)BitConverter.ToUInt32(bytes, offset + 4);
        var metaDataSize = (long)BitConverter.ToUInt32(bytes, offset + 8);
        var dataOffset = (long)offset + HeaderSize;
        var metaDataOffset = dataOffset + Padded(dataSize);
        var blockEnd = metaDataOffset + Padded(metaDataSize);
        if (dataOffset + dataSize > end || metaDataOffset + metaDataSize > end)
        {
            throw new InvalidDataException(
                $"The U3D data is cut short: block 0x{type:X8} at byte {offset} says it holds {dataSize} bytes of data " +
                $"and {metaDataSize} of meta data, and only {end - dataOffset} follow");
        }

        var metaData = metaDataSize == 0
            ? Array.Empty<U3dMetaDataEntry>()
            : ReadMetaData(bytes, (int)metaDataOffset, (int)metaDataSize, type, offset);
        return new U3dBlock(bytes, type, offset, (int)dataOffset, (int)dataSize, (int)Math.Min(blockEnd, end), metaData);
    }

    /// <summary>Every block from <paramref name="offset"/> to <paramref name="end"/>, one after another.</summary>
    public static List<U3dBlock> ReadBlocks(byte[] bytes, int offset, int end)
    {
        var blocks = new List<U3dBlock>();
        while (offset < end)
        {
            var block = ReadBlock(bytes, offset, end);
            blocks.Add(block);
            offset = block.End;
        }

        return blocks;
    }

    private static long Padded(long size) => (size + 3) & ~3L;

    private static U3dMetaDataEntry[] ReadMetaData(byte[] bytes, int offset, int size, uint type, int blockOffset)
    {
        var reader = new U3dBitReader(bytes, offset, size, true);
        try
        {
            var count = reader.ReadU32();
            reader.CheckCount(count, 64, "meta data pairs");
            var entries = new U3dMetaDataEntry[count];
            for (var index = 0; index < entries.Length; index++)
            {
                var attributes = reader.ReadU32();
                var key = reader.ReadString();
                if ((attributes & 0x1) != 0)
                {
                    var length = reader.ReadU32();
                    reader.CheckCount(length, 8, "bytes of a meta data value");
                    entries[index] = new U3dMetaDataEntry
                    {
                        Attributes = attributes, Key = key, BinaryValue = reader.ReadBytes((int)length)
                    };
                }
                else
                {
                    entries[index] = new U3dMetaDataEntry { Attributes = attributes, Key = key, Value = reader.ReadString() };
                }
            }

            reader.EnsureWithinData();
            return entries;
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"The meta data of U3D block 0x{type:X8} at byte {blockOffset} is broken: " +
                                           exception.Message, exception);
        }
    }
}
