// Ported from Draco (src/draco/core/decoder_buffer.cc, varint_decoding.h).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.Buffers.Binary;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal sealed class DracoReader
{
    private readonly byte[] data;
    private readonly int end;
    private int bitStart;
    private long bitOffset;

    public DracoReader(byte[] data) : this(data, 0, data.Length)
    {
    }

    private DracoReader(byte[] data, int position, int end)
    {
        this.data = data;
        Position = position;
        this.end = end;
    }

    public int Position { get; private set; }

    public int Remaining => end - Position;

    public DracoReader Copy() => new(data, Position, end) { bitStart = bitStart, bitOffset = bitOffset };

    public void MoveTo(DracoReader other) => Position = other.Position;

    public void Skip(long count)
    {
        if (count < 0 || count > Remaining)
        {
            throw Truncated();
        }

        Position += (int)count;
    }

    public ReadOnlySpan<byte> Bytes(int count)
    {
        if (count < 0 || count > Remaining)
        {
            throw Truncated();
        }

        var bytes = data.AsSpan(Position, count);
        Position += count;
        return bytes;
    }

    public ReadOnlySpan<byte> Bytes(ulong count) => Bytes(count > int.MaxValue ? -1 : (int)count);

    public byte Byte() => Bytes(1)[0];

    public sbyte SByte() => (sbyte)Byte();

    public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2));

    public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4));

    public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));

    public float Single() => BinaryPrimitives.ReadSingleLittleEndian(Bytes(4));

    public uint VarUInt32()
    {
        var value = VarUInt64(5);
        if (value > uint.MaxValue)
        {
            throw new InvalidDataException("a Draco 32-bit varint is too long");
        }

        return (uint)value;
    }

    public ulong VarUInt64() => VarUInt64(10);

    public ulong StartBits(bool sized)
    {
        var size = sized ? VarUInt64() : 0;
        bitStart = Position;
        bitOffset = 0;
        return size;
    }

    public uint Bits(int count)
    {
        if (count > 32)
        {
            throw new InvalidDataException($"{count} Draco bits in one value");
        }

        var value = 0u;
        for (var bit = 0; bit < count; bit++)
        {
            var at = bitStart + (bitOffset >> 3);
            if (at < end)
            {
                value |= (uint)((data[at] >> (int)(bitOffset & 7)) & 1) << bit;
                bitOffset++;
            }
        }

        return value;
    }

    public void EndBits() => Skip((bitOffset + 7) / 8);

    public static InvalidDataException Truncated() => new("Draco data ends too early");

    private ulong VarUInt64(int maxBytes)
    {
        var value = 0ul;
        for (var i = 0; i < maxBytes; i++)
        {
            var b = Byte();
            value |= (ulong)(b & 127) << (7 * i);
            if (b < 128)
            {
                return value;
            }
        }

        throw new InvalidDataException("a Draco varint is too long");
    }
}
