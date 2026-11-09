using System.Collections.Generic;
using System.IO;
using Adamantium.Imaging.Mq;

namespace Adamantium.Imaging.Jbig2;

/// <summary>Decodes the bitmaps of a symbol dictionary's new symbols one at a time (ITU-T T.88, 6.5.8): generic
/// region decoding, or with refinement and aggregation a refined symbol or a text region of symbols.</summary>
internal sealed class Jbig2SymbolDecoder
{
    private readonly Jbig2SymbolCoding _coding;
    private readonly MqDecoder _decoder;
    private readonly Jbig2BitReader _bits;
    private readonly Jbig2SymbolDictionaryDecoders _integers;
    private readonly Jbig2TextDecoders _text;
    private readonly byte[] _genericContexts;
    private readonly List<Jbig2Bitmap> _symbols;
    private readonly Jbig2Limits _limits;

    public Jbig2SymbolDecoder(Jbig2SymbolCoding coding, MqDecoder decoder, Jbig2BitReader bits,
        Jbig2SymbolDictionaryDecoders integers, Jbig2TextDecoders text, byte[] genericContexts,
        List<Jbig2Bitmap> symbols, Jbig2Limits limits)
    {
        _coding = coding;
        _decoder = decoder;
        _bits = bits;
        _integers = integers;
        _text = text;
        _genericContexts = genericContexts;
        _symbols = symbols;
        _limits = limits;
    }

    public Jbig2Bitmap Symbol(int width, int height)
    {
        if (!_coding.RefineAggregate)
        {
            return Jbig2GenericRegion.Decode(_decoder, _genericContexts, new Jbig2GenericParameters
            {
                Width = width,
                Height = height,
                Template = _coding.Template,
                At = _coding.At,
            }, _limits);
        }

        var instances = Required(_coding.Huffman
            ? _coding.AggregateTable.Decode(_bits)
            : _integers.Aggregate.Decode(_decoder));
        if (instances < 1)
        {
            throw new InvalidDataException("A JBIG2 aggregate symbol has no symbols.");
        }

        return instances == 1 ? Refined(width, height) : Aggregate(width, height, instances);
    }

    private Jbig2Bitmap Refined(int width, int height)
    {
        int id, dx, dy;
        if (_coding.Huffman)
        {
            id = (int)_bits.ReadBits(_coding.CodeLength);
            dx = Required(Jbig2StandardTables.Of(15).Decode(_bits));
            dy = Required(Jbig2StandardTables.Of(15).Decode(_bits));
            var size = Required(Jbig2StandardTables.Of(1).Decode(_bits));
            _bits.AlignToByte();
            var start = _bits.BytePosition;
            if (size < 0 || size > _bits.End - start)
            {
                throw new InvalidDataException("A JBIG2 refined symbol is longer than its segment.");
            }

            var refined = Refine(new MqDecoder(_bits.Data, start, size),
                new byte[Jbig2RefinementRegion.ContextCount(_coding.RefinementTemplate)], id, width, height, dx, dy);
            _bits.MoveTo(start + size);
            return refined;
        }

        id = _text.SymbolId.Decode(_decoder);
        dx = Required(_text.RefinementX.Decode(_decoder));
        dy = Required(_text.RefinementY.Decode(_decoder));
        return Refine(_decoder, _text.RefinementContexts, id, width, height, dx, dy);
    }

    private Jbig2Bitmap Refine(MqDecoder decoder, byte[] contexts, int id, int width, int height, int dx, int dy)
    {
        if (id < 0 || id >= _symbols.Count)
        {
            throw new InvalidDataException("A JBIG2 refined symbol names a symbol there is not.");
        }

        return Jbig2RefinementRegion.Decode(decoder, contexts, width, height, _coding.RefinementTemplate,
            _symbols[id], dx, dy, false, _coding.RefinementAt, _limits);
    }

    private Jbig2Bitmap Aggregate(int width, int height, int instances)
    {
        var parameters = new Jbig2TextParameters
        {
            Huffman = _coding.Huffman,
            Refine = true,
            Width = width,
            Height = height,
            Instances = instances,
            LogStrips = 0,
            Symbols = _symbols,
            SymbolCodeLength = _coding.CodeLength,
            SymbolCodes = _coding.Huffman ? FixedCodes(_coding.CodeLength, _symbols.Count) : null,
            Combination = Jbig2Combination.Or,
            Corner = Jbig2Corner.TopLeft,
            FirstS = Jbig2StandardTables.Of(6),
            DeltaS = Jbig2StandardTables.Of(8),
            DeltaT = Jbig2StandardTables.Of(11),
            RefinementWidth = Jbig2StandardTables.Of(15),
            RefinementHeight = Jbig2StandardTables.Of(15),
            RefinementX = Jbig2StandardTables.Of(15),
            RefinementY = Jbig2StandardTables.Of(15),
            RefinementSize = Jbig2StandardTables.Of(1),
            RefinementTemplate = _coding.RefinementTemplate,
            RefinementAt = _coding.RefinementAt,
        };
        return Jbig2TextRegion.Decode(parameters, _decoder, _text, _bits, _limits);
    }

    private static Jbig2HuffmanTable FixedCodes(int length, int count)
    {
        if (length == 0)
        {
            return null;
        }

        var lines = new Jbig2HuffmanLine[count];
        for (var i = 0; i < count; i++)
        {
            lines[i] = new Jbig2HuffmanLine(length, 0, i);
        }

        return new Jbig2HuffmanTable(lines);
    }

    private static int Required(int? value) =>
        value ?? throw new InvalidDataException("A JBIG2 symbol dictionary has out-of-band where a number belongs.");
}
