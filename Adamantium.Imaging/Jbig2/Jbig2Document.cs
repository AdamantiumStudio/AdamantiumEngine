using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Imaging.Mq;

namespace Adamantium.Imaging.Jbig2;

/// <summary>Reads JBIG2 segments one after another and draws the first page they describe: what each segment leaves
/// - symbols, patterns, tables, intermediate regions - is kept by its number for the segments that refer to it. Once
/// the page has begun, a broken segment ends the stream and the page keeps what it has.</summary>
internal sealed class Jbig2Document
{
    private const uint UnknownHeight = 0xFFFFFFFF;

    private readonly Dictionary<uint, object> _results = [];
    private readonly Jbig2Limits _limits;
    private Jbig2Page _page;
    private bool _pageEnded;

    public Jbig2Document(Jbig2Limits limits)
    {
        _limits = limits;
    }

    /// <summary>The page, or null when no page information segment came.</summary>
    public Jbig2Page Page => _page;

    /// <summary>Reads the segments of <paramref name="data"/>, each header followed by its data.</summary>
    public void ReadSequential(byte[] data)
    {
        var bytes = new BigEndianReader(data, 0, data.Length);
        while (bytes.Remaining > 0 && !_pageEnded)
        {
            Jbig2Segment segment;
            try
            {
                segment = Jbig2Segment.Read(bytes);
                Process(segment, data);
            }
            catch (InvalidDataException) when (_page != null)
            {
                break;
            }

            if (segment.Type == Jbig2SegmentTypes.EndOfFile)
            {
                break;
            }
        }
    }

    /// <summary>Reads a JBIG2 file (ITU-T T.88, annex D): its header, then segments in either organization.
    /// </summary>
    public void ReadFile(byte[] data)
    {
        var bytes = new BigEndianReader(data, 8, data.Length);
        var flags = bytes.ReadByte();
        if ((flags & 2) == 0)
        {
            bytes.Skip(4);
        }

        if ((flags & 1) != 0)
        {
            ReadSequential(data.AsSpan(bytes.Position).ToArray());
            return;
        }

        var headers = new List<Jbig2Segment>();
        while (bytes.Remaining > 0)
        {
            var segment = Jbig2Segment.ReadHeader(bytes);
            headers.Add(segment);
            if (segment.Type == Jbig2SegmentTypes.EndOfFile)
            {
                break;
            }
        }

        var at = bytes.Position;
        foreach (var header in headers)
        {
            var segment = header.At(at, data.Length);
            at = segment.DataEnd;
            Process(segment, data);
            if (_pageEnded)
            {
                break;
            }
        }
    }

    private void Process(Jbig2Segment segment, byte[] data)
    {
        var bytes = new BigEndianReader(data, segment.DataStart, segment.DataEnd);
        switch (segment.Type)
        {
            case Jbig2SegmentTypes.SymbolDictionary:
                _results[segment.Number] = Jbig2SymbolDictionary.Read(bytes, Symbols(segment), Tables(segment),
                    LastDictionary(segment), _limits);
                break;
            case Jbig2SegmentTypes.IntermediateTextRegion:
            case Jbig2SegmentTypes.ImmediateTextRegion:
            case Jbig2SegmentTypes.ImmediateLosslessTextRegion:
                Region(segment, bytes, TextRegion);
                break;
            case Jbig2SegmentTypes.PatternDictionary:
                _results[segment.Number] = Jbig2PatternDictionary.Read(bytes, _limits);
                break;
            case Jbig2SegmentTypes.IntermediateHalftoneRegion:
            case Jbig2SegmentTypes.ImmediateHalftoneRegion:
            case Jbig2SegmentTypes.ImmediateLosslessHalftoneRegion:
                Region(segment, bytes, (s, b, info) =>
                    Jbig2HalftoneRegion.Read(b, Width(info), Height(info), Patterns(s), _limits));
                break;
            case Jbig2SegmentTypes.IntermediateGenericRegion:
            case Jbig2SegmentTypes.ImmediateGenericRegion:
            case Jbig2SegmentTypes.ImmediateLosslessGenericRegion:
                Region(segment, bytes, GenericRegion);
                break;
            case Jbig2SegmentTypes.IntermediateRefinementRegion:
            case Jbig2SegmentTypes.ImmediateRefinementRegion:
            case Jbig2SegmentTypes.ImmediateLosslessRefinementRegion:
                Region(segment, bytes, RefinementRegion);
                break;
            case Jbig2SegmentTypes.PageInformation:
                if (_page == null)
                {
                    _page = Jbig2Page.Read(bytes, _limits);
                }
                else
                {
                    _pageEnded = true;
                }

                break;
            case Jbig2SegmentTypes.EndOfStripe:
                _page?.EndStripe(bytes.ReadUInt32());
                break;
            case Jbig2SegmentTypes.EndOfPage:
                _pageEnded = _page != null;
                break;
            case Jbig2SegmentTypes.Tables:
                _results[segment.Number] = Jbig2HuffmanTable.Read(bytes);
                break;
        }
    }

    private void Region(Jbig2Segment segment, BigEndianReader bytes,
        Func<Jbig2Segment, BigEndianReader, Jbig2RegionInfo, Jbig2Bitmap> decode)
    {
        var info = Jbig2RegionInfo.Read(bytes);
        var bitmap = decode(segment, bytes, info);
        var intermediate = segment.Type is Jbig2SegmentTypes.IntermediateTextRegion
            or Jbig2SegmentTypes.IntermediateHalftoneRegion or Jbig2SegmentTypes.IntermediateGenericRegion
            or Jbig2SegmentTypes.IntermediateRefinementRegion;
        if (intermediate)
        {
            _results[segment.Number] = new Jbig2Region(bitmap, info);
        }
        else
        {
            _page?.Draw(bitmap, info.X, info.Y, info.Combination);
        }
    }

    private Jbig2Bitmap GenericRegion(Jbig2Segment segment, BigEndianReader bytes, Jbig2RegionInfo info)
    {
        var flags = bytes.ReadByte();
        var mmr = (flags & 1) != 0;
        var template = (flags >> 1) & 3;
        if ((flags & 0x10) != 0)
        {
            throw new NotSupportedException("JBIG2 generic regions with the extended template are not read.");
        }

        int[] at = null;
        if (!mmr)
        {
            at = new int[template == 0 ? 8 : 2];
            for (var i = 0; i < at.Length; i++)
            {
                at[i] = bytes.ReadSByte();
            }
        }

        var end = bytes.End;
        var height = info.Height;
        if (height == UnknownHeight)
        {
            if (end - bytes.Position < 6)
            {
                throw new InvalidDataException("A JBIG2 generic region of unknown height has no row count.");
            }

            var rows = new BigEndianReader(bytes.Data, end - 4, end);
            height = rows.ReadUInt32();
            end -= 6;
        }

        if (mmr)
        {
            return Jbig2GenericRegion.DecodeMmr(bytes.Data, bytes.Position, end, Width(info), Size(height), _limits,
                out _);
        }

        return Jbig2GenericRegion.Decode(new MqDecoder(bytes.Data, bytes.Position, end - bytes.Position),
            new byte[Jbig2GenericRegion.ContextCount(template)], new Jbig2GenericParameters
            {
                Width = Width(info),
                Height = Size(height),
                Template = template,
                TypicalPrediction = (flags & 8) != 0,
                At = at,
            }, _limits);
    }

    private Jbig2Bitmap RefinementRegion(Jbig2Segment segment, BigEndianReader bytes, Jbig2RegionInfo info)
    {
        var flags = bytes.ReadByte();
        var template = flags & 1;
        var at = Jbig2RefinementRegion.DefaultAt;
        if (template == 0)
        {
            for (var i = 0; i < 4; i++)
            {
                at[i] = bytes.ReadSByte();
            }
        }

        Jbig2Bitmap reference = null;
        foreach (var number in segment.ReferredTo)
        {
            if (_results.GetValueOrDefault(number) is Jbig2Region region)
            {
                reference = region.Bitmap;
            }
        }

        reference ??= _page?.Under(info.X, info.Y, Width(info), Height(info)) ??
                      throw new InvalidDataException("A JBIG2 refinement region has nothing to refine.");
        return Jbig2RefinementRegion.Decode(new MqDecoder(bytes.Data, bytes.Position, bytes.Remaining),
            new byte[Jbig2RefinementRegion.ContextCount(template)], Width(info), Height(info), template, reference, 0,
            0, (flags & 2) != 0, at, _limits);
    }

    private Jbig2Bitmap TextRegion(Jbig2Segment segment, BigEndianReader bytes, Jbig2RegionInfo info)
    {
        var flags = bytes.ReadUInt16();
        var huffman = (flags & 1) != 0;
        var refine = (flags & 2) != 0;
        var logStrips = (flags >> 2) & 3;
        var corner = (Jbig2Corner)((flags >> 4) & 3);
        var transposed = (flags & 0x40) != 0;
        var combination = (Jbig2Combination)((flags >> 7) & 3);
        var defaultPixel = (flags >> 9) & 1;
        var dsOffset = (flags >> 10) & 0x1F;
        if (dsOffset > 15)
        {
            dsOffset -= 32;
        }

        var refinementTemplate = (flags >> 15) & 1;
        var huffmanFlags = huffman ? bytes.ReadUInt16() : 0;
        var refinementAt = Jbig2RefinementRegion.DefaultAt;
        if (refine && refinementTemplate == 0)
        {
            for (var i = 0; i < 4; i++)
            {
                refinementAt[i] = bytes.ReadSByte();
            }
        }

        var instances = bytes.ReadUInt32();
        var symbols = Symbols(segment);
        var codeLength = Jbig2SymbolDictionary.CodeLength(symbols.Count);
        var tables = Tables(segment);
        Jbig2BitReader bits = null;
        Jbig2HuffmanTable symbolCodes = null;
        if (huffman)
        {
            bits = new Jbig2BitReader(bytes.Data, bytes.Position, bytes.End);
            symbolCodes = Jbig2SymbolCodes.Read(bits, symbols.Count);
        }

        var parameters = new Jbig2TextParameters
        {
            Huffman = huffman,
            Refine = refine,
            Width = Width(info),
            Height = Height(info),
            Instances = instances,
            LogStrips = logStrips,
            Symbols = symbols,
            SymbolCodeLength = codeLength,
            SymbolCodes = symbolCodes,
            DefaultPixel = defaultPixel,
            Combination = combination,
            Transposed = transposed,
            Corner = corner,
            DsOffset = dsOffset,
            FirstS = huffman ? Table(huffmanFlags & 3, [6, 7], tables) : null,
            DeltaS = huffman ? Table((huffmanFlags >> 2) & 3, [8, 9, 10], tables) : null,
            DeltaT = huffman ? Table((huffmanFlags >> 4) & 3, [11, 12, 13], tables) : null,
            RefinementWidth = huffman ? Table((huffmanFlags >> 6) & 3, [14, 15], tables) : null,
            RefinementHeight = huffman ? Table((huffmanFlags >> 8) & 3, [14, 15], tables) : null,
            RefinementX = huffman ? Table((huffmanFlags >> 10) & 3, [14, 15], tables) : null,
            RefinementY = huffman ? Table((huffmanFlags >> 12) & 3, [14, 15], tables) : null,
            RefinementSize = huffman
                ? (huffmanFlags & 0x4000) != 0 ? Jbig2SymbolDictionary.Custom(tables) : Jbig2StandardTables.Of(1)
                : null,
            RefinementTemplate = refinementTemplate,
            RefinementAt = refinementAt,
        };
        var decoder = huffman ? null : new MqDecoder(bytes.Data, bytes.Position, bytes.Remaining);
        var decoders = huffman ? null : new Jbig2TextDecoders(codeLength, refinementTemplate);
        return Jbig2TextRegion.Decode(parameters, decoder, decoders, bits, _limits);
    }

    private static Jbig2HuffmanTable Table(int selection, int[] standard, Queue<Jbig2HuffmanTable> tables)
    {
        if (selection == 3)
        {
            return Jbig2SymbolDictionary.Custom(tables);
        }

        return selection < standard.Length
            ? Jbig2StandardTables.Of(standard[selection])
            : throw new InvalidDataException("A JBIG2 text region selects a Huffman table there is not.");
    }

    private List<Jbig2Bitmap> Symbols(Jbig2Segment segment)
    {
        var symbols = new List<Jbig2Bitmap>();
        foreach (var number in segment.ReferredTo)
        {
            if (_results.GetValueOrDefault(number) is Jbig2SymbolDictionary dictionary)
            {
                symbols.AddRange(dictionary.Exported);
            }
        }

        return symbols;
    }

    private Jbig2SymbolDictionary LastDictionary(Jbig2Segment segment)
    {
        Jbig2SymbolDictionary last = null;
        foreach (var number in segment.ReferredTo)
        {
            if (_results.GetValueOrDefault(number) is Jbig2SymbolDictionary dictionary)
            {
                last = dictionary;
            }
        }

        return last;
    }

    private Queue<Jbig2HuffmanTable> Tables(Jbig2Segment segment)
    {
        var tables = new Queue<Jbig2HuffmanTable>();
        foreach (var number in segment.ReferredTo)
        {
            if (_results.GetValueOrDefault(number) is Jbig2HuffmanTable table)
            {
                tables.Enqueue(table);
            }
        }

        return tables;
    }

    private List<Jbig2Bitmap> Patterns(Jbig2Segment segment)
    {
        foreach (var number in segment.ReferredTo)
        {
            if (_results.GetValueOrDefault(number) is List<Jbig2Bitmap> patterns)
            {
                return patterns;
            }
        }

        throw new InvalidDataException("A JBIG2 halftone region refers to no pattern dictionary.");
    }

    private static int Width(Jbig2RegionInfo info) => Size(info.Width);

    private static int Height(Jbig2RegionInfo info) => Size(info.Height);

    private static int Size(uint size) =>
        size <= int.MaxValue ? (int)size : throw new InvalidDataException("A JBIG2 region is too big.");
}
