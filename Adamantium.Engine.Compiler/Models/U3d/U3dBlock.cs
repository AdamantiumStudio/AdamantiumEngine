using System;
using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>One block of a U3D file (ECMA-363 9.2): its type, where its data lies in the file's bytes, and its meta
/// data. The data is read with <see cref="OpenData"/>.</summary>
public sealed class U3dBlock
{
    private readonly byte[] source;

    internal U3dBlock(byte[] source, uint type, int offset, int dataOffset, int dataSize, int end,
        IReadOnlyList<U3dMetaDataEntry> metaData)
    {
        this.source = source;
        Type = type;
        Offset = offset;
        DataOffset = dataOffset;
        DataSize = dataSize;
        End = end;
        MetaData = metaData;
    }

    /// <summary>The block type.</summary>
    public uint Type { get; }

    /// <summary>Where the block starts in the file.</summary>
    public int Offset { get; }

    /// <summary>Where its data starts in the file.</summary>
    public int DataOffset { get; }

    /// <summary>The size of its data, its padding left out.</summary>
    public int DataSize { get; }

    /// <summary>Where the next block starts: past the data, the meta data and the padding of both.</summary>
    public int End { get; }

    /// <summary>The key/value pairs of its meta data, in the order the block has them.</summary>
    public IReadOnlyList<U3dMetaDataEntry> MetaData { get; }

    /// <summary>The data's bytes as the file has them.</summary>
    public ReadOnlyMemory<byte> Data => new(source, DataOffset, DataSize);

    /// <summary>A reader of the data from its start, its compressed fields read as plain values when
    /// <paramref name="noCompression"/>.</summary>
    public U3dBitReader OpenData(bool noCompression) => new(source, DataOffset, DataSize, noCompression);

    internal byte[] Source => source;
}

/// <summary>A key/value pair of a block's meta data (ECMA-363 9.2.6).</summary>
public sealed class U3dMetaDataEntry
{
    /// <summary>The formatting bits: 0x1 a binary value, 0x2 hidden, and the viewer hints of 9.2.6.2.</summary>
    public uint Attributes { get; init; }

    /// <summary>The key, its #attributes as the file writes them.</summary>
    public string Key { get; init; }

    /// <summary>The value of a string pair; null for a binary one.</summary>
    public string Value { get; init; }

    /// <summary>The value of a binary pair; null for a string one.</summary>
    public byte[] BinaryValue { get; init; }

    /// <summary>Whether the value is a binary sequence rather than a string.</summary>
    public bool IsBinary => (Attributes & 0x1) != 0;
}
