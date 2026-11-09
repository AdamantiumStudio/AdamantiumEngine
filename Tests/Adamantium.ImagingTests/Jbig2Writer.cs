using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.ImagingTests;

/// <summary>Writes JBIG2 for tests (ITU-T T.88): segments, arithmetic generic regions with each template, the integer
/// and symbol number coders, symbol dictionaries and text regions. Contexts are formed pixel by pixel from the
/// standard's pictures, not the decoder's sliding windows.</summary>
internal static class Jbig2Writer
{
    private static readonly (int X, int Y)[][] Templates =
    [
        [(-1, 0), (-2, 0), (-3, 0), (-4, 0), (127, 0), (2, -1), (1, -1), (0, -1), (-1, -1), (-2, -1), (127, 1),
            (127, 2), (1, -2), (0, -2), (-1, -2), (127, 3)],
        [(-1, 0), (-2, 0), (-3, 0), (127, 0), (2, -1), (1, -1), (0, -1), (-1, -1), (-2, -1), (2, -2), (1, -2),
            (0, -2), (-1, -2)],
        [(-1, 0), (-2, 0), (127, 0), (1, -1), (0, -1), (-1, -1), (-2, -1), (1, -2), (0, -2), (-1, -2)],
        [(-1, 0), (-2, 0), (-3, 0), (-4, 0), (127, 0), (1, -1), (0, -1), (-1, -1), (-2, -1), (-3, -1)],
    ];

    private static readonly int[] TypicalContexts = [0x9B25, 0x0795, 0x00E5, 0x0195];

    public static int Pixel(byte[,] bitmap, int x, int y) =>
        x >= 0 && y >= 0 && y < bitmap.GetLength(0) && x < bitmap.GetLength(1) ? bitmap[y, x] : 0;

    /// <summary>Codes <paramref name="bitmap"/> as generic region data.</summary>
    public static void Generic(MqEncoder encoder, byte[] contexts, byte[,] bitmap, int template, int[] at,
        bool typical)
    {
        var height = bitmap.GetLength(0);
        var width = bitmap.GetLength(1);
        var predicted = 0;
        for (var y = 0; y < height; y++)
        {
            if (typical)
            {
                var same = 1;
                for (var x = 0; x < width && same == 1; x++)
                {
                    same = bitmap[y, x] == Pixel(bitmap, x, y - 1) ? 1 : 0;
                }

                encoder.Encode(contexts, TypicalContexts[template], same ^ predicted);
                predicted = same;
                if (same == 1)
                {
                    continue;
                }
            }

            for (var x = 0; x < width; x++)
            {
                var context = 0;
                var pixels = Templates[template];
                for (var bit = 0; bit < pixels.Length; bit++)
                {
                    var (dx, dy) = pixels[bit];
                    if (dx == 127)
                    {
                        (dx, dy) = (at[2 * dy], at[2 * dy + 1]);
                    }

                    context |= Pixel(bitmap, x + dx, y + dy) << bit;
                }

                encoder.Encode(contexts, context, bitmap[y, x]);
            }
        }
    }

    /// <summary>Codes an integer, or out-of-band for null, as an IAx coder (ITU-T T.88, A.2).</summary>
    public static void Integer(MqEncoder encoder, byte[] contexts, int? value)
    {
        var previous = 1;
        void Bit(int bit)
        {
            encoder.Encode(contexts, previous, bit);
            previous = previous < 256 ? (previous << 1) | bit : (((previous << 1) | bit) & 511) | 256;
        }

        if (value == null)
        {
            Bit(1);
            Bit(0);
            Bit(0);
            Bit(0);
            return;
        }

        var v = (long)value.Value;
        Bit(v < 0 ? 1 : 0);
        v = Math.Abs(v);
        (int Prefix, int Bits, long Offset)[] ranges =
            [(0, 2, 0), (1, 4, 4), (2, 6, 20), (3, 8, 84), (4, 12, 340), (5, 32, 4436)];
        foreach (var (prefix, bits, offset) in ranges)
        {
            if (prefix < 5 && v >= offset + (1L << bits))
            {
                continue;
            }

            for (var i = 0; i < prefix; i++)
            {
                Bit(1);
            }

            if (prefix < 5)
            {
                Bit(0);
            }

            for (var i = bits - 1; i >= 0; i--)
            {
                Bit((int)(((v - offset) >> i) & 1));
            }

            return;
        }
    }

    /// <summary>Codes a symbol number as IAID (ITU-T T.88, A.3).</summary>
    public static void SymbolId(MqEncoder encoder, byte[] contexts, int length, int id)
    {
        var previous = 1;
        for (var i = length - 1; i >= 0; i--)
        {
            var bit = (id >> i) & 1;
            encoder.Encode(contexts, previous, bit);
            previous = (previous << 1) | bit;
        }
    }

    /// <summary>A segment: header with page 1 and the given referrals, then the data.</summary>
    public static byte[] Segment(uint number, int type, byte[] data, params uint[] referredTo)
    {
        var output = new MemoryStream();
        UInt32(output, number);
        output.WriteByte((byte)type);
        output.WriteByte((byte)(referredTo.Length << 5));
        foreach (var referred in referredTo)
        {
            output.WriteByte((byte)referred);
        }

        output.WriteByte(1);
        UInt32(output, (uint)data.Length);
        output.Write(data);
        return output.ToArray();
    }

    public static byte[] PageInformation(int width, int height, int defaultPixel = 0)
    {
        var output = new MemoryStream();
        UInt32(output, (uint)width);
        UInt32(output, (uint)height);
        UInt32(output, 0);
        UInt32(output, 0);
        output.WriteByte((byte)(defaultPixel << 2));
        output.WriteByte(0);
        output.WriteByte(0);
        return output.ToArray();
    }

    public static byte[] RegionInfo(int width, int height, int x, int y, int combination)
    {
        var output = new MemoryStream();
        UInt32(output, (uint)width);
        UInt32(output, (uint)height);
        UInt32(output, (uint)x);
        UInt32(output, (uint)y);
        output.WriteByte((byte)combination);
        return output.ToArray();
    }

    /// <summary>The data of an immediate generic region segment coded arithmetically.</summary>
    public static byte[] GenericRegion(byte[,] bitmap, int x, int y, int template, int[] at, bool typical)
    {
        var output = new MemoryStream();
        output.Write(RegionInfo(bitmap.GetLength(1), bitmap.GetLength(0), x, y, 0));
        output.WriteByte((byte)((template << 1) | (typical ? 8 : 0)));
        foreach (var value in at)
        {
            output.WriteByte((byte)(sbyte)value);
        }

        var encoder = new MqEncoder();
        Generic(encoder, new byte[1 << 16], bitmap, template, at, typical);
        output.Write(encoder.Finish());
        return output.ToArray();
    }

    /// <summary>A page of one generic region filling it.</summary>
    public static byte[] GenericPage(byte[,] bitmap, int template, int[] at, bool typical) =>
    [
        .. Segment(0, 48, PageInformation(bitmap.GetLength(1), bitmap.GetLength(0))),
        .. Segment(1, 38, GenericRegion(bitmap, 0, 0, template, at, typical)),
        .. Segment(2, 49, []),
    ];

    /// <summary>An arithmetic symbol dictionary of one height class per symbol height, exporting all.</summary>
    public static byte[] SymbolDictionary(IReadOnlyList<byte[,]> symbols)
    {
        var output = new MemoryStream();
        output.WriteByte(0);
        output.WriteByte(0);
        foreach (var value in new sbyte[] { 3, -1, -3, -1, 2, -2, -2, -2 })
        {
            output.WriteByte((byte)value);
        }

        UInt32(output, (uint)symbols.Count);
        UInt32(output, (uint)symbols.Count);
        var encoder = new MqEncoder();
        var generic = new byte[1 << 16];
        byte[] height = new byte[512], width = new byte[512], export = new byte[512];
        var lastHeight = 0;
        var order = new List<int>();
        for (var i = 0; i < symbols.Count; i++)
        {
            order.Add(i);
        }

        foreach (var i in order)
        {
            var symbol = symbols[i];
            Integer(encoder, height, symbol.GetLength(0) - lastHeight);
            lastHeight = symbol.GetLength(0);
            Integer(encoder, width, symbol.GetLength(1));
            Generic(encoder, generic, symbol, 0, [3, -1, -3, -1, 2, -2, -2, -2], false);
            Integer(encoder, width, null);
        }

        Integer(encoder, export, 0);
        Integer(encoder, export, symbols.Count);
        output.Write(encoder.Finish());
        return output.ToArray();
    }

    /// <summary>An arithmetic text region with one strip, symbols placed by their top left corners.</summary>
    public static byte[] TextRegion(int width, int height, int symbolCount, IReadOnlyList<(int Id, int X, int Y)>
        placements, IReadOnlyList<byte[,]> symbols)
    {
        var output = new MemoryStream();
        output.Write(RegionInfo(width, height, 0, 0, 0));
        output.WriteByte(0);
        output.WriteByte(1 << 4);
        UInt32(output, (uint)placements.Count);
        var encoder = new MqEncoder();
        byte[] dt = new byte[512], fs = new byte[512], ds = new byte[512];
        var length = 0;
        while (1 << length < symbolCount)
        {
            length++;
        }

        var ids = new byte[1 << (length + 1)];
        Integer(encoder, dt, 0);
        var stripT = 0;
        var firstS = 0;
        foreach (var (id, x, y) in placements)
        {
            Integer(encoder, dt, y - stripT);
            stripT = y;
            Integer(encoder, fs, x - firstS);
            firstS = x;
            SymbolId(encoder, ids, length, id);
            Integer(encoder, ds, null);
        }

        output.Write(encoder.Finish());
        return output.ToArray();
    }

    /// <summary>Codes <paramref name="target"/> as a refinement of <paramref name="reference"/> (ITU-T T.88, 6.3),
    /// without typical prediction.</summary>
    public static void Refinement(MqEncoder encoder, byte[] contexts, byte[,] target, byte[,] reference, int dx,
        int dy, int template, int[] at)
    {
        for (var y = 0; y < target.GetLength(0); y++)
        {
            for (var x = 0; x < target.GetLength(1); x++)
            {
                int R(int i, int j) => Pixel(reference, x - dx + i, y - dy + j);
                int T(int i, int j) => Pixel(target, x + i, y + j);
                var context = template == 0
                    ? T(-1, 0) | (T(1, -1) << 1) | (T(0, -1) << 2) | (T(at[0], at[1]) << 3) | (R(1, 1) << 4) |
                      (R(0, 1) << 5) | (R(-1, 1) << 6) | (R(1, 0) << 7) | (R(0, 0) << 8) | (R(-1, 0) << 9) |
                      (R(1, -1) << 10) | (R(0, -1) << 11) | (R(at[2], at[3]) << 12)
                    : T(-1, 0) | (T(1, -1) << 1) | (T(0, -1) << 2) | (T(-1, -1) << 3) | (R(1, 1) << 4) |
                      (R(0, 1) << 5) | (R(1, 0) << 6) | (R(0, 0) << 7) | (R(-1, 0) << 8) | (R(0, -1) << 9);
                encoder.Encode(contexts, context, target[y, x]);
            }
        }
    }

    /// <summary>An arithmetic text region with refinement: each placement names a symbol and, when it has one, the
    /// bitmap to refine it into with its extra offsets.</summary>
    public static byte[] RefinedTextRegion(int width, int height, int symbolCount,
        IReadOnlyList<(int Id, int X, int Y, byte[,] Refined, int Rdx, int Rdy)> placements,
        IReadOnlyList<byte[,]> symbols, int template)
    {
        var output = new MemoryStream();
        output.Write(RegionInfo(width, height, 0, 0, 0));
        output.WriteByte((byte)(template << 7));
        output.WriteByte((1 << 4) | 2);
        int[] at = [-1, -1, -1, -1];
        if (template == 0)
        {
            foreach (var value in at)
            {
                output.WriteByte((byte)(sbyte)value);
            }
        }

        UInt32(output, (uint)placements.Count);
        var encoder = new MqEncoder();
        byte[] dt = new byte[512], fs = new byte[512], ds = new byte[512], ri = new byte[512];
        byte[] rdw = new byte[512], rdh = new byte[512], rdx = new byte[512], rdy = new byte[512];
        var refinement = new byte[1 << 13];
        var length = 0;
        while (1 << length < symbolCount)
        {
            length++;
        }

        var ids = new byte[1 << (length + 1)];
        Integer(encoder, dt, 0);
        var stripT = 0;
        var firstS = 0;
        foreach (var (id, x, y, refined, offsetX, offsetY) in placements)
        {
            Integer(encoder, dt, y - stripT);
            stripT = y;
            Integer(encoder, fs, x - firstS);
            firstS = x;
            SymbolId(encoder, ids, length, id);
            Integer(encoder, ri, refined == null ? 0 : 1);
            if (refined != null)
            {
                var symbol = symbols[id];
                var dw = refined.GetLength(1) - symbol.GetLength(1);
                var dh = refined.GetLength(0) - symbol.GetLength(0);
                Integer(encoder, rdw, dw);
                Integer(encoder, rdh, dh);
                Integer(encoder, rdx, offsetX);
                Integer(encoder, rdy, offsetY);
                Refinement(encoder, refinement, refined, symbol, (dw >> 1) + offsetX, (dh >> 1) + offsetY, template,
                    at);
            }

            Integer(encoder, ds, null);
        }

        output.Write(encoder.Finish());
        return output.ToArray();
    }

    private static void UInt32(Stream output, uint value)
    {
        output.WriteByte((byte)(value >> 24));
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }
}
