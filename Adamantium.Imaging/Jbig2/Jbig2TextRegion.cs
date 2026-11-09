using System.IO;
using Adamantium.Imaging.Mq;

namespace Adamantium.Imaging.Jbig2;

/// <summary>Text region decoding (ITU-T T.88, 6.4): symbols placed strip by strip, each by its reference corner, some
/// refined against the symbol they name. Its numbers come from the arithmetic decoders or from Huffman tables.
/// </summary>
internal sealed class Jbig2TextRegion
{
    private readonly Jbig2TextParameters _parameters;
    private readonly MqDecoder _decoder;
    private readonly Jbig2TextDecoders _decoders;
    private readonly Jbig2BitReader _bits;
    private readonly Jbig2Limits _limits;

    private Jbig2TextRegion(Jbig2TextParameters parameters, MqDecoder decoder, Jbig2TextDecoders decoders,
        Jbig2BitReader bits, Jbig2Limits limits)
    {
        _parameters = parameters;
        _decoder = decoder;
        _decoders = decoders;
        _bits = bits;
        _limits = limits;
    }

    /// <summary>Decodes a region: arithmetically with <paramref name="decoder"/> and <paramref name="decoders"/>, or
    /// with Huffman codes from <paramref name="bits"/>.</summary>
    public static Jbig2Bitmap Decode(Jbig2TextParameters parameters, MqDecoder decoder, Jbig2TextDecoders decoders,
        Jbig2BitReader bits, Jbig2Limits limits) =>
        new Jbig2TextRegion(parameters, decoder, decoders, bits, limits).Decode();

    private Jbig2Bitmap Decode()
    {
        var p = _parameters;
        var region = _limits.Bitmap(p.Width, p.Height);
        if (p.DefaultPixel != 0)
        {
            region.Fill(1);
        }

        var strips = 1 << p.LogStrips;
        long stripT = -(long)Required(DeltaT()) * strips;
        long firstS = 0;
        long instances = 0;
        while (instances < p.Instances)
        {
            stripT += (long)Required(DeltaT()) * strips;
            var first = true;
            long currentS = 0;
            while (true)
            {
                if (first)
                {
                    firstS += Required(FirstS());
                    currentS = firstS;
                    first = false;
                }
                else
                {
                    var deltaS = DeltaS();
                    if (deltaS == null)
                    {
                        break;
                    }

                    currentS += deltaS.Value + p.DsOffset;
                }

                if (instances >= p.Instances)
                {
                    throw new InvalidDataException("A JBIG2 text region has more symbols than it says.");
                }

                var currentT = strips == 1 ? 0 : StripOffset();
                var t = stripT + currentT;
                var symbol = Symbol();
                _limits.Spend(16 + (long)symbol.Width * symbol.Height);
                var width = symbol.Width;
                var height = symbol.Height;
                var bottom = p.Corner is Jbig2Corner.BottomLeft or Jbig2Corner.BottomRight;
                var right = p.Corner is Jbig2Corner.TopRight or Jbig2Corner.BottomRight;
                if (!p.Transposed && right)
                {
                    currentS += width - 1;
                }
                else if (p.Transposed && bottom)
                {
                    currentS += height - 1;
                }

                var s = currentS;
                long x, y;
                if (!p.Transposed)
                {
                    x = right ? s - width + 1 : s;
                    y = bottom ? t - height + 1 : t;
                }
                else
                {
                    x = right ? t - width + 1 : t;
                    y = bottom ? s - height + 1 : s;
                }

                if (x > int.MinValue && x < int.MaxValue && y > int.MinValue && y < int.MaxValue)
                {
                    region.Combine(symbol, (int)x, (int)y, p.Combination);
                }

                if (!p.Transposed && !right)
                {
                    currentS += width - 1;
                }
                else if (p.Transposed && !bottom)
                {
                    currentS += height - 1;
                }

                instances++;
            }
        }

        return region;
    }

    private Jbig2Bitmap Symbol()
    {
        var p = _parameters;
        var id = !p.Huffman ? _decoders.SymbolId.Decode(_decoder)
            : p.SymbolCodes == null ? 0
            : p.SymbolCodes.Decode(_bits) ?? -1;
        if (id < 0 || id >= p.Symbols.Count)
        {
            throw new InvalidDataException("A JBIG2 text region names a symbol it does not have.");
        }

        var symbol = p.Symbols[id];
        var refine = p.Refine && (p.Huffman ? _bits.ReadBit() : Required(_decoders.Refine.Decode(_decoder))) != 0;
        if (!refine)
        {
            return symbol;
        }

        int dw, dh, rx, ry;
        if (p.Huffman)
        {
            dw = Required(p.RefinementWidth.Decode(_bits));
            dh = Required(p.RefinementHeight.Decode(_bits));
            rx = Required(p.RefinementX.Decode(_bits));
            ry = Required(p.RefinementY.Decode(_bits));
            var size = Required(p.RefinementSize.Decode(_bits));
            _bits.AlignToByte();
            var start = _bits.BytePosition;
            if (size < 0 || size > _bits.End - start)
            {
                throw new InvalidDataException("A JBIG2 refined symbol is longer than its region.");
            }

            var refined = Refine(new MqDecoder(_bits.Data, start, size),
                new byte[Jbig2RefinementRegion.ContextCount(p.RefinementTemplate)], symbol, dw, dh, rx, ry);
            _bits.MoveTo(start + size);
            return refined;
        }

        dw = Required(_decoders.RefinementWidth.Decode(_decoder));
        dh = Required(_decoders.RefinementHeight.Decode(_decoder));
        rx = Required(_decoders.RefinementX.Decode(_decoder));
        ry = Required(_decoders.RefinementY.Decode(_decoder));
        return Refine(_decoder, _decoders.RefinementContexts, symbol, dw, dh, rx, ry);
    }

    private Jbig2Bitmap Refine(MqDecoder decoder, byte[] contexts, Jbig2Bitmap symbol, int dw, int dh, int rx,
        int ry)
    {
        var p = _parameters;
        var width = (long)symbol.Width + dw;
        var height = (long)symbol.Height + dh;
        if (width < 0 || height < 0 || width > int.MaxValue || height > int.MaxValue)
        {
            throw new InvalidDataException("A JBIG2 refined symbol has no size.");
        }

        return Jbig2RefinementRegion.Decode(decoder, contexts, (int)width, (int)height, p.RefinementTemplate, symbol,
            (dw >> 1) + rx, (dh >> 1) + ry, false, p.RefinementAt, _limits);
    }

    private int? DeltaT() =>
        _parameters.Huffman ? _parameters.DeltaT.Decode(_bits) : _decoders.DeltaT.Decode(_decoder);

    private int? FirstS() =>
        _parameters.Huffman ? _parameters.FirstS.Decode(_bits) : _decoders.FirstS.Decode(_decoder);

    private int? DeltaS() =>
        _parameters.Huffman ? _parameters.DeltaS.Decode(_bits) : _decoders.DeltaS.Decode(_decoder);

    private int StripOffset() =>
        _parameters.Huffman
            ? (int)_bits.ReadBits(_parameters.LogStrips)
            : Required(_decoders.StripT.Decode(_decoder));

    private static int Required(int? value) =>
        value ?? throw new InvalidDataException("A JBIG2 text region has out-of-band where a number belongs.");
}
