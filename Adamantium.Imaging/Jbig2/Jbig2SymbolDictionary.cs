using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Imaging.Mq;

namespace Adamantium.Imaging.Jbig2;

/// <summary>A symbol dictionary segment (ITU-T T.88, 6.5 and 7.4.2): the symbols it exports and, when it says so, the
/// bitmap coding contexts it leaves for the next dictionary.</summary>
internal sealed class Jbig2SymbolDictionary
{
    private Jbig2SymbolDictionary(List<Jbig2Bitmap> exported, byte[] genericContexts, byte[] refinementContexts)
    {
        Exported = exported;
        GenericContexts = genericContexts;
        RefinementContexts = refinementContexts;
    }

    public IReadOnlyList<Jbig2Bitmap> Exported { get; }

    /// <summary>The generic region contexts it retains, or null.</summary>
    public byte[] GenericContexts { get; }

    /// <summary>The refinement region contexts it retains, or null.</summary>
    public byte[] RefinementContexts { get; }

    /// <summary>Reads the segment's data.</summary>
    /// <param name="bytes">The segment's data.</param>
    /// <param name="input">The symbols of the dictionaries it refers to.</param>
    /// <param name="tables">The Huffman tables of the table segments it refers to, in order.</param>
    /// <param name="previous">The last dictionary it refers to, whose contexts it may take up.</param>
    /// <param name="limits">The stream's bounds.</param>
    public static Jbig2SymbolDictionary Read(BigEndianReader bytes, IReadOnlyList<Jbig2Bitmap> input,
        Queue<Jbig2HuffmanTable> tables, Jbig2SymbolDictionary previous, Jbig2Limits limits)
    {
        var flags = bytes.ReadUInt16();
        var huffman = (flags & 1) != 0;
        var refineAggregate = (flags & 2) != 0;
        var contextUsed = (flags & 0x100) != 0;
        var contextRetained = (flags & 0x200) != 0;
        var template = (flags >> 10) & 3;
        var refinementTemplate = (flags >> 12) & 1;
        int[] at = null;
        if (!huffman)
        {
            at = new int[template == 0 ? 8 : 2];
            for (var i = 0; i < at.Length; i++)
            {
                at[i] = bytes.ReadSByte();
            }
        }

        var refinementAt = Jbig2RefinementRegion.DefaultAt;
        if (refineAggregate && refinementTemplate == 0)
        {
            for (var i = 0; i < 4; i++)
            {
                refinementAt[i] = bytes.ReadSByte();
            }
        }

        var exportedCount = bytes.ReadUInt32();
        var newCount = bytes.ReadUInt32();
        if (newCount > limits.MaxPixels || exportedCount > (long)input.Count + newCount)
        {
            throw new InvalidDataException("A JBIG2 symbol dictionary has more symbols than it can.");
        }

        Jbig2HuffmanTable heightTable = null, widthTable = null, sizeTable = null, aggregateTable = null;
        if (huffman)
        {
            heightTable = Table((flags >> 2) & 3, 4, 5, tables);
            widthTable = Table((flags >> 4) & 3, 2, 3, tables);
            sizeTable = (flags & 0x40) != 0 ? Custom(tables) : Jbig2StandardTables.Of(1);
            aggregateTable = (flags & 0x80) != 0 ? Custom(tables) : Jbig2StandardTables.Of(1);
        }

        var genericContexts = Contexts(contextUsed ? previous?.GenericContexts : null,
            Jbig2GenericRegion.ContextCount(template));
        var refinementContexts = Contexts(contextUsed ? previous?.RefinementContexts : null,
            Jbig2RefinementRegion.ContextCount(refinementTemplate));
        var codeLength = CodeLength(input.Count + (long)newCount);
        var decoder = huffman ? null : new MqDecoder(bytes.Data, bytes.Position, bytes.Remaining);
        var bits = huffman ? new Jbig2BitReader(bytes.Data, bytes.Position, bytes.End) : null;
        var integers = new Jbig2SymbolDictionaryDecoders();
        var text = new Jbig2TextDecoders(codeLength, refinementTemplate, refinementContexts);
        var symbols = new List<Jbig2Bitmap>(input);
        var newSymbols = new List<Jbig2Bitmap>((int)Math.Min(newCount, 1 << 16));
        var coding = new Jbig2SymbolCoding
        {
            Huffman = huffman,
            RefineAggregate = refineAggregate,
            Template = template,
            At = at,
            RefinementTemplate = refinementTemplate,
            RefinementAt = refinementAt,
            CodeLength = codeLength,
            AggregateTable = aggregateTable,
        };
        var symbolDecoder = new Jbig2SymbolDecoder(coding, decoder, bits, integers, text, genericContexts, symbols,
            limits);

        long height = 0;
        while (newSymbols.Count < newCount)
        {
            height += Required(huffman ? heightTable.Decode(bits) : integers.Height.Decode(decoder));
            if (height < 0 || height > int.MaxValue)
            {
                throw new InvalidDataException("A JBIG2 height class has no height.");
            }

            long width = 0;
            long totalWidth = 0;
            var classFirst = newSymbols.Count;
            List<int> widths = huffman && !refineAggregate ? [] : null;
            while (true)
            {
                var deltaWidth = huffman ? widthTable.Decode(bits) : integers.Width.Decode(decoder);
                if (deltaWidth == null)
                {
                    break;
                }

                if (newSymbols.Count >= newCount)
                {
                    throw new InvalidDataException("A JBIG2 symbol dictionary has more symbols than it says.");
                }

                width += deltaWidth.Value;
                totalWidth += width;
                if (width < 0 || width > int.MaxValue || totalWidth > limits.MaxPixels)
                {
                    throw new InvalidDataException("A JBIG2 symbol has no width.");
                }

                limits.Spend(64);
                if (widths != null)
                {
                    widths.Add((int)width);
                    newSymbols.Add(null);
                    continue;
                }

                var symbol = symbolDecoder.Symbol((int)width, (int)height);
                newSymbols.Add(symbol);
                symbols.Add(symbol);
            }

            if (widths != null)
            {
                Collective(bits, sizeTable, widths, (int)totalWidth, (int)height, newSymbols, classFirst, symbols,
                    limits);
            }
        }

        var exported = Export(input, newSymbols, exportedCount, huffman, bits, decoder, integers);
        return new Jbig2SymbolDictionary(exported, contextRetained ? genericContexts : null,
            contextRetained ? refinementContexts : null);
    }

    /// <summary>The bits of a symbol number among <paramref name="count"/> symbols.</summary>
    public static int CodeLength(long count)
    {
        var length = 0;
        while (1L << length < count)
        {
            length++;
        }

        return length;
    }

    /// <summary>The next of the custom tables a segment refers to.</summary>
    public static Jbig2HuffmanTable Custom(Queue<Jbig2HuffmanTable> tables) =>
        tables.Count > 0
            ? tables.Dequeue()
            : throw new InvalidDataException("A JBIG2 segment uses a Huffman table it does not refer to.");

    private static byte[] Contexts(byte[] kept, int count) =>
        kept != null && kept.Length == count ? (byte[])kept.Clone() : new byte[count];

    private static void Collective(Jbig2BitReader bits, Jbig2HuffmanTable sizeTable, List<int> widths,
        int totalWidth, int height, List<Jbig2Bitmap> newSymbols, int classFirst, List<Jbig2Bitmap> symbols,
        Jbig2Limits limits)
    {
        var size = Required(sizeTable.Decode(bits));
        bits.AlignToByte();
        var start = bits.BytePosition;
        Jbig2Bitmap collective;
        if (size == 0)
        {
            var rowBytes = (totalWidth + 7) / 8;
            if ((long)rowBytes * height > bits.End - start)
            {
                throw new InvalidDataException("A JBIG2 collective bitmap is longer than its segment.");
            }

            collective = limits.Bitmap(totalWidth, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < totalWidth; x++)
                {
                    var b = bits.Data[start + y * rowBytes + (x >> 3)];
                    collective.Set(x, y, (b >> (7 - (x & 7))) & 1);
                }
            }

            bits.MoveTo(start + rowBytes * height);
        }
        else
        {
            if (size < 0 || size > bits.End - start)
            {
                throw new InvalidDataException("A JBIG2 collective bitmap is longer than its segment.");
            }

            collective = Jbig2GenericRegion.DecodeMmr(bits.Data, start, start + size, totalWidth, height, limits,
                out _);
            bits.MoveTo(start + size);
        }

        var x0 = 0;
        for (var i = 0; i < widths.Count; i++)
        {
            var symbol = collective.Crop(x0, 0, widths[i], height);
            newSymbols[classFirst + i] = symbol;
            symbols.Add(symbol);
            x0 += widths[i];
        }
    }

    private static List<Jbig2Bitmap> Export(IReadOnlyList<Jbig2Bitmap> input, List<Jbig2Bitmap> newSymbols,
        uint exportedCount, bool huffman, Jbig2BitReader bits, MqDecoder decoder,
        Jbig2SymbolDictionaryDecoders integers)
    {
        var exported = new List<Jbig2Bitmap>((int)Math.Min(exportedCount, 1 << 16));
        var total = input.Count + newSymbols.Count;
        var index = 0;
        var exporting = false;
        while (index < total)
        {
            var run = Required(huffman ? Jbig2StandardTables.Of(1).Decode(bits) : integers.Export.Decode(decoder));
            if (run < 0 || run > total - index)
            {
                throw new InvalidDataException("A JBIG2 symbol dictionary exports symbols it does not have.");
            }

            if (exporting)
            {
                for (var i = index; i < index + run; i++)
                {
                    exported.Add(i < input.Count ? input[i] : newSymbols[i - input.Count]);
                }
            }

            index += run;
            exporting = !exporting;
        }

        if (exported.Count > exportedCount)
        {
            throw new InvalidDataException("A JBIG2 symbol dictionary exports more symbols than it says.");
        }

        return exported;
    }

    private static Jbig2HuffmanTable Table(int selection, int first, int second, Queue<Jbig2HuffmanTable> tables) =>
        selection switch
        {
            0 => Jbig2StandardTables.Of(first),
            1 => Jbig2StandardTables.Of(second),
            3 => Custom(tables),
            _ => throw new InvalidDataException("A JBIG2 segment selects a Huffman table there is not."),
        };

    private static int Required(int? value) =>
        value ?? throw new InvalidDataException("A JBIG2 symbol dictionary has out-of-band where a number belongs.");
}
