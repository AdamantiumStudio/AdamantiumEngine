using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Extensions;
using Adamantium.Fonts.Tables.CFF;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.Tables;

internal sealed class ColorPaintTable
{
    private const ushort ForegroundEntry = 0xFFFF;
    private const int MaxDepth = 64;
    private const int MaxVisits = 65536;

    private const uint NoVariation = 0xFFFFFFFF;

    private readonly byte[] data;
    private readonly int baseGlyphList;
    private readonly int layerList;
    private readonly int clipList;
    private readonly DeltaSetIndexMap variationMap;
    private readonly VariationStore variations;

    private ColorPaintTable(byte[] data, int baseGlyphList, int layerList, int clipList, DeltaSetIndexMap variationMap,
        VariationStore variations)
    {
        this.data = data;
        this.baseGlyphList = baseGlyphList;
        this.layerList = layerList;
        this.clipList = clipList;
        this.variationMap = variationMap;
        this.variations = variations;
    }

    public static ColorPaintTable Read(FontStreamReader reader, long offset, long length)
    {
        reader.Position = offset;
        var version = reader.ReadUInt16();
        if (version < 1)
        {
            return null;
        }

        reader.Position = offset + 14;
        var baseGlyphList = reader.ReadUInt32();
        var layerList = reader.ReadUInt32();
        if (baseGlyphList == 0 || baseGlyphList + 4 > length || layerList > length)
        {
            return null;
        }

        uint clipList = 0;
        DeltaSetIndexMap variationMap = null;
        VariationStore variations = null;
        if (length >= 34)
        {
            clipList = reader.ReadUInt32();
            var mapOffset = reader.ReadUInt32();
            var storeOffset = reader.ReadUInt32();
            clipList = clipList < length ? clipList : 0;
            variationMap = mapOffset != 0 && mapOffset < length ? reader.ReadDeltaSetIndexMap(offset + mapOffset) : null;
            variations = storeOffset != 0 && storeOffset < length ? reader.ReadItemVariationStore(offset + storeOffset) : null;
        }

        var data = new byte[length];
        reader.Position = offset;
        var read = 0;
        while (read < length)
        {
            var chunk = reader.Read(data, read, data.Length - read);
            if (chunk == 0)
            {
                return null;
            }

            read += chunk;
        }

        return new ColorPaintTable(data, (int)baseGlyphList, (int)layerList, (int)clipList, variationMap, variations);
    }

    public bool Varies => variations != null;

    public bool TryGetClipBox(uint glyphIndex, float[] coordinates, out RectangleF box)
    {
        box = default;
        if (clipList == 0)
        {
            return false;
        }

        try
        {
            var count = (int)UInt32(clipList + 1);
            var low = 0;
            var high = count - 1;
            while (low <= high)
            {
                var middle = (low + high) / 2;
                var record = clipList + 5 + middle * 7;
                if (glyphIndex < UInt16(record))
                {
                    high = middle - 1;
                }
                else if (glyphIndex > UInt16(record + 2))
                {
                    low = middle + 1;
                }
                else
                {
                    var at = clipList + (int)UInt24(record + 4);
                    var variable = data[at] == 2;
                    var context = new Context(this, null, 0, coordinates);
                    var baseIndex = variable ? UInt32(at + 9) : NoVariation;
                    var xMin = Int16(at + 1) + context.Delta(baseIndex, 0);
                    var yMin = Int16(at + 3) + context.Delta(baseIndex, 1);
                    var xMax = Int16(at + 5) + context.Delta(baseIndex, 2);
                    var yMax = Int16(at + 7) + context.Delta(baseIndex, 3);
                    box = new RectangleF(xMin, yMin, xMax - xMin, yMax - yMin);
                    return true;
                }
            }
        }
        catch (IndexOutOfRangeException)
        {
            box = default;
        }

        return false;
    }

    public ColorPaintOperation[] GetOperations(uint glyphIndex, ColorPaletteTable palettes, int palette,
        float[] coordinates = null)
    {
        try
        {
            var paint = FindPaint(glyphIndex);
            if (paint < 0)
            {
                return [];
            }

            var context = new Context(this, palettes, palette, coordinates);
            context.Visiting.Add(glyphIndex);
            Paint(paint, Matrix3x2<double>.Identity, context, 0);
            return context.Operations.ToArray();
        }
        catch (IndexOutOfRangeException)
        {
            return [];
        }
    }

    private int FindPaint(uint glyphIndex)
    {
        var count = (int)UInt32(baseGlyphList);
        var low = 0;
        var high = count - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var record = baseGlyphList + 4 + middle * 6;
            var glyph = UInt16(record);
            if (glyph < glyphIndex)
            {
                low = middle + 1;
            }
            else if (glyph > glyphIndex)
            {
                high = middle - 1;
            }
            else
            {
                return baseGlyphList + (int)UInt32(record + 2);
            }
        }

        return -1;
    }

    private void Paint(int at, Matrix3x2<double> transform, Context context, int depth)
    {
        if (depth > MaxDepth || ++context.Visits > MaxVisits)
        {
            return;
        }

        var format = data[at];
        switch (format)
        {
            case 1:
                var count = data[at + 1];
                var first = (int)UInt32(at + 2);
                for (var i = 0; i < count; i++)
                {
                    PaintIsolated(layerList + (int)UInt32(layerList + 4 + (first + i) * 4), transform, context, depth + 1);
                }

                break;
            case >= 2 and <= 9:
                context.Operations.Add(ColorPaintOperation.FillWith(Fill(at, transform, context)));
                break;
            case 10:
                context.Operations.Add(ColorPaintOperation.PushClip(UInt16(at + 4), transform));
                Paint(Child(at), transform, context, depth + 1);
                context.Operations.Add(ColorPaintOperation.PopClip());
                break;
            case 11:
                var glyph = UInt16(at + 1);
                var paint = FindPaint(glyph);
                if (paint >= 0 && context.Visiting.Add(glyph))
                {
                    Paint(paint, transform, context, depth + 1);
                    context.Visiting.Remove(glyph);
                }

                break;
            case >= 12 and <= 31:
                Paint(Child(at), Matrix3x2<double>.Multiply(Transform(at, context), transform), context, depth + 1);
                break;
            case 32:
                Composite(at, transform, context, depth);
                break;
        }
    }

    private void Composite(int at, Matrix3x2<double> transform, Context context, int depth)
    {
        var source = Child(at);
        var mode = data[at + 4];
        var backdrop = at + (int)UInt24(at + 5);
        if (mode == (byte)ColorCompositeMode.SourceOver)
        {
            Paint(backdrop, transform, context, depth + 1);
            PaintIsolated(source, transform, context, depth + 1);
            return;
        }

        Paint(backdrop, transform, context, depth + 1);
        context.Operations.Add(ColorPaintOperation.PushGroup());
        Paint(source, transform, context, depth + 1);
        var known = mode <= (byte)ColorCompositeMode.Luminosity;
        context.Operations.Add(ColorPaintOperation.PopGroup(known ? (ColorCompositeMode)mode : ColorCompositeMode.SourceOver));
    }

    private void PaintIsolated(int at, Matrix3x2<double> transform, Context context, int depth)
    {
        var start = context.Operations.Count;
        Paint(at, transform, context, depth);
        if (HoldsAComposite(context.Operations, start))
        {
            context.Operations.Insert(start, ColorPaintOperation.PushGroup());
            context.Operations.Add(ColorPaintOperation.PopGroup(ColorCompositeMode.SourceOver));
        }
    }

    private static bool HoldsAComposite(List<ColorPaintOperation> operations, int start)
    {
        var depth = 0;
        for (var i = start; i < operations.Count; i++)
        {
            var operation = operations[i];
            if (operation.Kind == ColorPaintOperationKind.PushGroup)
            {
                depth++;
            }
            else if (operation.Kind == ColorPaintOperationKind.PopGroup)
            {
                depth--;
                if (depth == 0 && operation.Mode != ColorCompositeMode.SourceOver)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private ColorFill Fill(int at, Matrix3x2<double> transform, Context context)
    {
        var format = data[at];
        switch (format)
        {
            case 2 or 3:
                var alpha = F2Dot14(at + 3) + context.Delta(VarIndex(at + 5, format == 3), 0) / 16384;
                return new ColorFill(ColorFillKind.Solid, [Stop(0, UInt16(at + 1), alpha, context)], ColorExtend.Pad,
                    transform);
            case 4 or 5:
            {
                var index = VarIndex(at + 16, format == 5);
                return new ColorFill(ColorFillKind.LinearGradient, Stops(Child(at), format == 5, context),
                    Extend(Child(at)), transform)
                {
                    Point0 = new Vector2<double>(Int16(at + 4) + context.Delta(index, 0), Int16(at + 6) + context.Delta(index, 1)),
                    Point1 = new Vector2<double>(Int16(at + 8) + context.Delta(index, 2), Int16(at + 10) + context.Delta(index, 3)),
                    Point2 = new Vector2<double>(Int16(at + 12) + context.Delta(index, 4), Int16(at + 14) + context.Delta(index, 5)),
                };
            }
            case 8 or 9:
            {
                var index = VarIndex(at + 12, format == 9);
                return new ColorFill(ColorFillKind.SweepGradient, Stops(Child(at), format == 9, context),
                    Extend(Child(at)), transform)
                {
                    Point0 = new Vector2<double>(Int16(at + 4) + context.Delta(index, 0), Int16(at + 6) + context.Delta(index, 1)),
                    StartAngle = (F2Dot14(at + 8) + context.Delta(index, 2) / 16384 + 1) * 180,
                    EndAngle = (F2Dot14(at + 10) + context.Delta(index, 3) / 16384 + 1) * 180,
                };
            }
            default:
            {
                var index = VarIndex(at + 16, format == 7);
                return new ColorFill(ColorFillKind.RadialGradient, Stops(Child(at), format == 7, context),
                    Extend(Child(at)), transform)
                {
                    Point0 = new Vector2<double>(Int16(at + 4) + context.Delta(index, 0), Int16(at + 6) + context.Delta(index, 1)),
                    Radius0 = UInt16(at + 8) + context.Delta(index, 2),
                    Point1 = new Vector2<double>(Int16(at + 10) + context.Delta(index, 3), Int16(at + 12) + context.Delta(index, 4)),
                    Radius1 = UInt16(at + 14) + context.Delta(index, 5),
                };
            }
        }
    }

    private Matrix3x2<double> Transform(int at, Context context)
    {
        var format = data[at];
        var variable = format % 2 == 1;
        switch (format)
        {
            case 12 or 13:
                var affine = at + (int)UInt24(at + 4);
                var index = VarIndex(affine + 24, variable);
                return new Matrix3x2<double>(Fixed(affine) + context.Delta(index, 0) / 65536,
                    Fixed(affine + 4) + context.Delta(index, 1) / 65536, Fixed(affine + 8) + context.Delta(index, 2) / 65536,
                    Fixed(affine + 12) + context.Delta(index, 3) / 65536, Fixed(affine + 16) + context.Delta(index, 4) / 65536,
                    Fixed(affine + 20) + context.Delta(index, 5) / 65536);
            case 14 or 15:
            {
                var at8 = VarIndex(at + 8, variable);
                return Matrix3x2.CreateTranslation<double>(Int16(at + 4) + context.Delta(at8, 0), Int16(at + 6) + context.Delta(at8, 1));
            }
            case 16 or 17:
            {
                var at8 = VarIndex(at + 8, variable);
                return Matrix3x2.CreateScale<double>(Unit(at + 4, at8, 0, context), Unit(at + 6, at8, 1, context));
            }
            case 18 or 19:
            {
                var at12 = VarIndex(at + 12, variable);
                return AroundCenter(Matrix3x2.CreateScale<double>(Unit(at + 4, at12, 0, context), Unit(at + 6, at12, 1, context)),
                    at + 8, at12, 2, context);
            }
            case 20 or 21:
                return Matrix3x2.CreateScale<double>(Unit(at + 4, VarIndex(at + 6, variable), 0, context));
            case 22 or 23:
            {
                var at10 = VarIndex(at + 10, variable);
                return AroundCenter(Matrix3x2.CreateScale<double>(Unit(at + 4, at10, 0, context)), at + 6, at10, 1, context);
            }
            case 24 or 25:
                return Rotation(Unit(at + 4, VarIndex(at + 6, variable), 0, context));
            case 26 or 27:
            {
                var at10 = VarIndex(at + 10, variable);
                return AroundCenter(Rotation(Unit(at + 4, at10, 0, context)), at + 6, at10, 1, context);
            }
            case 28 or 29:
            {
                var at8 = VarIndex(at + 8, variable);
                return Skew(Unit(at + 4, at8, 0, context), Unit(at + 6, at8, 1, context));
            }
            default:
            {
                var at12 = VarIndex(at + 12, variable);
                return AroundCenter(Skew(Unit(at + 4, at12, 0, context), Unit(at + 6, at12, 1, context)), at + 8, at12, 2,
                    context);
            }
        }
    }

    private uint VarIndex(int at, bool variable) => variable ? UInt32(at) : NoVariation;

    private double Unit(int at, uint index, int item, Context context) =>
        F2Dot14(at) + context.Delta(index, item) / 16384;

    private Matrix3x2<double> AroundCenter(Matrix3x2<double> transform, int center, uint index, int item, Context context)
    {
        var x = Int16(center) + context.Delta(index, item);
        var y = Int16(center + 2) + context.Delta(index, item + 1);
        return Matrix3x2<double>.Multiply(Matrix3x2<double>.Multiply(Matrix3x2.CreateTranslation<double>(-x, -y), transform),
            Matrix3x2.CreateTranslation<double>(x, y));
    }

    private static Matrix3x2<double> Rotation(double halfTurns)
    {
        var cos = Math.Cos(halfTurns * Math.PI);
        var sin = Math.Sin(halfTurns * Math.PI);
        return new Matrix3x2<double>(cos, sin, -sin, cos, 0, 0);
    }

    private static Matrix3x2<double> Skew(double x, double y)
    {
        return new Matrix3x2<double>(1, Math.Tan(y * Math.PI), -Math.Tan(x * Math.PI), 1, 0, 0);
    }

    private ColorExtend Extend(int colorLine)
    {
        return data[colorLine] switch
        {
            1 => ColorExtend.Repeat,
            2 => ColorExtend.Reflect,
            _ => ColorExtend.Pad,
        };
    }

    private ColorStop[] Stops(int colorLine, bool variable, Context context)
    {
        var count = UInt16(colorLine + 1);
        var size = variable ? 10 : 6;
        var stops = new ColorStop[count];
        for (var i = 0; i < count; i++)
        {
            var stop = colorLine + 3 + i * size;
            var index = VarIndex(stop + 6, variable);
            stops[i] = Stop((float)Unit(stop, index, 0, context), UInt16(stop + 2), (float)Unit(stop + 4, index, 1, context),
                context);
        }

        return stops.OrderBy(s => s.Offset).ToArray();
    }

    private static ColorStop Stop(float offset, ushort entry, float alpha, Context context)
    {
        if (entry == ForegroundEntry)
        {
            return new ColorStop(offset, null, alpha);
        }

        var color = context.Palettes?.GetColor(context.Palette, entry);
        if (color is not { } found)
        {
            return new ColorStop(offset, null, alpha);
        }

        found.A = (byte)Math.Max(0, Math.Min(255, Math.Round(found.A * alpha)));
        return new ColorStop(offset, found, alpha);
    }

    private int Child(int at) => at + (int)UInt24(at + 1);

    private ushort UInt16(int at) => (ushort)(data[at] << 8 | data[at + 1]);

    private short Int16(int at) => (short)UInt16(at);

    private uint UInt24(int at) => (uint)(data[at] << 16 | data[at + 1] << 8 | data[at + 2]);

    private uint UInt32(int at) => (uint)(data[at] << 24 | data[at + 1] << 16 | data[at + 2] << 8 | data[at + 3]);

    private float F2Dot14(int at) => Int16(at) / 16384f;

    private double Fixed(int at) => (int)UInt32(at) / 65536.0;

    private sealed class Context
    {
        private readonly ColorPaintTable table;
        private readonly float[] coordinates;

        public Context(ColorPaintTable table, ColorPaletteTable palettes, int palette, float[] coordinates)
        {
            this.table = table;
            this.coordinates = coordinates;
            Palettes = palettes;
            Palette = palette;
        }

        public float Delta(uint varIndexBase, int item)
        {
            if (varIndexBase == NoVariation || coordinates == null || table.variations == null)
            {
                return 0;
            }

            var index = varIndexBase + (uint)item;
            var (outer, inner) = table.variationMap?.Map(index) ?? ((int)(index >> 16), (int)(index & 0xFFFF));
            return table.variations.GetDelta(outer, inner, coordinates);
        }

        public ColorPaletteTable Palettes { get; }

        public int Palette { get; }

        public List<ColorPaintOperation> Operations { get; } = [];

        public HashSet<uint> Visiting { get; } = [];

        public int Visits { get; set; }
    }
}
