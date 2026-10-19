// Ported from Draco (src/draco/compression/entropy/rans_symbol_decoder.h, ans.h).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal sealed class DracoRansDecoder
{
    private readonly uint precision;
    private readonly uint lowerBound;
    private uint[] probabilities = [];
    private uint[] cumulative = [];
    private uint[] table = [];
    private byte[] bytes = [];
    private int offset;
    private uint state;

    public DracoRansDecoder(int symbolBits)
    {
        precision = 1u << Math.Clamp(3 * symbolBits / 2, 12, 20);
        lowerBound = precision * 4;
    }

    public int SymbolCount => probabilities.Length;

    public void ReadTable(DracoReader reader)
    {
        var count = reader.VarUInt32();
        if (count / 64 > reader.Remaining)
        {
            throw DracoReader.Truncated();
        }

        probabilities = new uint[count];
        for (var i = 0; i < count; i++)
        {
            var data = reader.Byte();
            var token = data & 3;
            if (token == 3)
            {
                var zeros = data >> 2;
                if (i + zeros >= count)
                {
                    throw new InvalidDataException("a Draco rANS table runs past its symbols");
                }

                i += zeros;
                continue;
            }

            var probability = (uint)(data >> 2);
            for (var b = 0; b < token; b++)
            {
                probability |= (uint)reader.Byte() << (8 * (b + 1) - 2);
            }

            probabilities[i] = probability;
        }

        if (count == 0)
        {
            return;
        }

        cumulative = new uint[count];
        table = new uint[precision];
        var sum = 0u;
        for (var i = 0u; i < count; i++)
        {
            cumulative[i] = sum;
            var next = sum + probabilities[i];
            if (next > precision || next < sum)
            {
                throw new InvalidDataException("Draco rANS probabilities exceed their precision");
            }

            table.AsSpan((int)sum, (int)(next - sum)).Fill(i);
            sum = next;
        }

        if (sum != precision)
        {
            throw new InvalidDataException("Draco rANS probabilities do not add up to their precision");
        }
    }

    public void Start(DracoReader reader)
    {
        (bytes, offset, state) = DracoBitDecoder.Load(reader, reader.VarUInt64(), lowerBound, true);
    }

    public uint Next()
    {
        while (state < lowerBound && offset > 0)
        {
            state = state * 256 + bytes[--offset];
        }

        var quotient = state / precision;
        var remainder = state % precision;
        var symbol = table[remainder];
        state = quotient * probabilities[symbol] + remainder - cumulative[symbol];
        return symbol;
    }
}
