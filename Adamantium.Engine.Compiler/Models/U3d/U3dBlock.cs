using System;
using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>One block of a U3D file (ECMA-363 9.2): its type, where its data lies, and its meta data.</summary>
internal sealed class U3dBlock
{
    private readonly byte[] _source;

    internal U3dBlock(byte[] source, uint type, int offset, int dataOffset, int dataSize, int end,
        IReadOnlyList<U3dMetaDataEntry> metaData)
    {
        _source = source;
        Type = type;
        Offset = offset;
        DataOffset = dataOffset;
        DataSize = dataSize;
        End = end;
        MetaData = metaData;
    }

    /// <summary>The block type.</summary>
    internal uint Type { get; }

    /// <summary>Where the block starts in the file.</summary>
    internal int Offset { get; }

    /// <summary>Where its data starts in the file.</summary>
    internal int DataOffset { get; }

    /// <summary>The size of its data, its padding left out.</summary>
    internal int DataSize { get; }

    /// <summary>Where the next block starts: past the data, the meta data and the padding of both.</summary>
    internal int End { get; }

    /// <summary>The key/value pairs of its meta data, in the order the block has them.</summary>
    internal IReadOnlyList<U3dMetaDataEntry> MetaData { get; }

    /// <summary>The data's bytes as the file has them.</summary>
    internal ReadOnlyMemory<byte> Data => new(_source, DataOffset, DataSize);

    internal byte[] Source => _source;

    /// <summary>A reader of the data from its start; compressed fields are read as plain values when
    /// <paramref name="noCompression"/>.</summary>
    internal U3dBitReader OpenData(bool noCompression) => new(_source, DataOffset, DataSize, noCompression);
}
