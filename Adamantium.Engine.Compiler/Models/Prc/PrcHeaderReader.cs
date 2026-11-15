using System;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads the uncompressed header data of a PRC file: 32-bit little-endian unsigned integers and identifiers,
/// each read checked against the end of the file.</summary>
internal sealed class PrcHeaderReader
{
    private readonly byte[] _bytes;

    internal PrcHeaderReader(byte[] bytes)
    {
        _bytes = bytes;
    }

    internal int Position { get; set; }

    internal int Length => _bytes.Length;

    internal byte GetByte(int at) => _bytes[at];

    internal uint ReadUnsignedInteger(string what)
    {
        if (Position < 0 || Position + 4 > _bytes.Length)
        {
            throw new InvalidDataException($"The PRC header ends at byte {Position} before {what}");
        }

        var value = BitConverter.ToUInt32(_bytes, Position);
        Position += 4;
        return value;
    }

    internal PrcUniqueId ReadUniqueId(string what) =>
        new(ReadUnsignedInteger(what), ReadUnsignedInteger(what), ReadUnsignedInteger(what), ReadUnsignedInteger(what));

    internal byte[] ReadBytes(uint count, string what)
    {
        CheckCount(count, 1, what);
        var read = _bytes.AsSpan(Position, (int)count).ToArray();
        Position += (int)count;
        return read;
    }

    internal void CheckCount(uint count, int bytesEach, string what)
    {
        if ((long)count * bytesEach > _bytes.Length - (long)Position)
        {
            throw new InvalidDataException($"The PRC header names {count} {what} at byte {Position}, more than the file holds");
        }
    }
}
