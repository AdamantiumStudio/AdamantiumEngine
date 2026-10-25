using System;
using System.Buffers.Binary;

namespace Adamantium.Imaging.Tiff;

/// <summary>Numbers read in a TIFF file's byte order.</summary>
internal static class TiffBytes
{
    /// <summary>A 16-bit unsigned number at the start of <paramref name="at"/>.</summary>
    public static ushort Read16(ReadOnlySpan<byte> at, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(at) : BinaryPrimitives.ReadUInt16BigEndian(at);

    /// <summary>A 32-bit unsigned number at the start of <paramref name="at"/>.</summary>
    public static uint Read32(ReadOnlySpan<byte> at, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(at) : BinaryPrimitives.ReadUInt32BigEndian(at);

    /// <summary>A 64-bit unsigned number at the start of <paramref name="at"/>.</summary>
    public static ulong Read64(ReadOnlySpan<byte> at, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(at) : BinaryPrimitives.ReadUInt64BigEndian(at);
}
