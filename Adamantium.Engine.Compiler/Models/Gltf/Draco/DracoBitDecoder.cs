// Ported from Draco (src/draco/compression/bit_coders/rans_bit_decoder.cc, src/draco/compression/entropy/ans.h).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal sealed class DracoBitDecoder
{
    private const uint Base = 4096;

    private byte[] bytes = [];
    private int offset;
    private uint state;
    private uint probabilityOfOne;

    public void Start(DracoReader reader)
    {
        probabilityOfOne = 256u - reader.Byte();
        (bytes, offset, state) = Load(reader, reader.VarUInt32(), Base, false);
    }

    public bool Next()
    {
        if (state < Base && offset > 0)
        {
            state = state * 256 + bytes[--offset];
        }

        var quotient = state / 256;
        var remainder = state % 256;
        var scaled = quotient * probabilityOfOne;
        var one = remainder < probabilityOfOne;
        state = one ? scaled + remainder : state - scaled - probabilityOfOne;
        return one;
    }

    public static (byte[] Bytes, int Offset, uint State) Load(DracoReader reader, ulong count, uint lowerBound, bool fourBytes)
    {
        var bytes = reader.Bytes(count).ToArray();
        var size = bytes.Length;
        if (size < 1)
        {
            throw new InvalidDataException("Draco rANS data is empty");
        }

        var length = (bytes[size - 1] >> 6) + 1;
        if (length > size || (length == 4 && !fourBytes))
        {
            throw new InvalidDataException("Draco rANS data has a broken state");
        }

        var value = 0u;
        for (var i = 0; i < length; i++)
        {
            value |= (uint)bytes[size - length + i] << (8 * i);
        }

        var state = (value & ((1u << (8 * length - 2)) - 1)) + lowerBound;
        if (state >= lowerBound * 256)
        {
            throw new InvalidDataException("Draco rANS data has a broken state");
        }

        return (bytes, size - length, state);
    }
}
