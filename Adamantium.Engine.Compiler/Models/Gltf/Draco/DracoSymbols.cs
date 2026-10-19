// Ported from Draco (src/draco/compression/entropy/symbol_decoding.cc).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal static class DracoSymbols
{
    private const byte Tagged = 0;
    private const byte Raw = 1;

    public static void Decode(DracoReader reader, int count, int components, Span<uint> into)
    {
        if (count == 0)
        {
            return;
        }

        if (count > into.Length)
        {
            throw new InvalidDataException($"{count} Draco symbols for {into.Length} values");
        }

        switch (reader.Byte())
        {
            case Tagged:
                DecodeTagged(reader, count, components, into);
                break;
            case Raw:
                DecodeRaw(reader, count, into);
                break;
            default:
                throw new InvalidDataException("a Draco symbol coding of no known kind");
        }
    }

    private static void DecodeTagged(DracoReader reader, int count, int components, Span<uint> into)
    {
        var tags = new DracoRansDecoder(5);
        tags.ReadTable(reader);
        tags.Start(reader);
        if (tags.SymbolCount == 0)
        {
            throw new InvalidDataException("Draco tagged symbols have no tags");
        }

        reader.StartBits(false);
        var at = 0;
        for (var i = 0; i < count; i += components)
        {
            var bits = (int)tags.Next();
            for (var j = 0; j < components; j++)
            {
                if (at >= into.Length)
                {
                    throw new InvalidDataException("Draco tagged symbols run past their values");
                }

                into[at++] = reader.Bits(bits);
            }
        }

        reader.EndBits();
    }

    private static void DecodeRaw(DracoReader reader, int count, Span<uint> into)
    {
        var bits = reader.Byte();
        if (bits is < 1 or > 18)
        {
            throw new InvalidDataException($"Draco raw symbols of {bits} bits");
        }

        var symbols = new DracoRansDecoder(bits);
        symbols.ReadTable(reader);
        if (symbols.SymbolCount == 0)
        {
            throw new InvalidDataException("Draco raw symbols have no symbols");
        }

        symbols.Start(reader);
        for (var i = 0; i < count; i++)
        {
            into[i] = symbols.Next();
        }
    }
}
