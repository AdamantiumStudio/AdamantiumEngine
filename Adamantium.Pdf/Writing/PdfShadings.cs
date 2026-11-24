using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Filters;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Shadings;

namespace Adamantium.Pdf.Writing;

/// <summary>Shadings (ISO 32000-2, 8.7.4.5): axial and radial gradients through any number of stops, and triangles
/// shaded between the colors of their corners. Painted with <see cref="PdfContentBuilder.Shade"/> over a clip, or made
/// a <see cref="Pattern"/> to fill or stroke with.</summary>
public static class PdfShadings
{
    /// <summary>A gradient along the line from (<paramref name="x0"/>, <paramref name="y0"/>) to
    /// (<paramref name="x1"/>, <paramref name="y1"/>); past its ends the end colors carry on.</summary>
    public static PdfDictionary Axial(double x0, double y0, double x1, double y1, IReadOnlyList<PdfGradientStop> stops,
        bool gray = false)
    {
        var shading = Gradient(2, gray, stops);
        shading.Set(PdfShadingKeys.Coords, Numbers(x0, y0, x1, y1));
        return shading;
    }

    /// <summary>A gradient from the circle (<paramref name="x0"/>, <paramref name="y0"/>, <paramref name="r0"/>) to
    /// the circle (<paramref name="x1"/>, <paramref name="y1"/>, <paramref name="r1"/>); past both the end colors carry
    /// on.</summary>
    public static PdfDictionary Radial(double x0, double y0, double r0, double x1, double y1, double r1,
        IReadOnlyList<PdfGradientStop> stops, bool gray = false)
    {
        var shading = Gradient(3, gray, stops);
        shading.Set(PdfShadingKeys.Coords, Numbers(x0, y0, Math.Max(0, r0), x1, y1, Math.Max(0, r1)));
        return shading;
    }

    /// <summary>Triangles, each three <paramref name="vertices"/> in a row, their colors blended across them (type 4,
    /// free-form).</summary>
    public static PdfStream Triangles(IReadOnlyList<PdfShadedVertex> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        var count = vertices.Count / 3 * 3;
        if (count == 0)
        {
            throw new ArgumentException("Triangles need three corners each.", nameof(vertices));
        }

        double left = double.PositiveInfinity, right = double.NegativeInfinity;
        double bottom = double.PositiveInfinity, top = double.NegativeInfinity;
        for (var index = 0; index < count; index++)
        {
            if (!double.IsFinite(vertices[index].X) || !double.IsFinite(vertices[index].Y))
            {
                throw new ArgumentException($"Corner {index} is not at a finite place.", nameof(vertices));
            }

            left = Math.Min(left, vertices[index].X);
            right = Math.Max(right, vertices[index].X);
            bottom = Math.Min(bottom, vertices[index].Y);
            top = Math.Max(top, vertices[index].Y);
        }

        right = Math.Max(right, left + 1e-6);
        top = Math.Max(top, bottom + 1e-6);
        var data = new byte[count * (1 + 4 + 4 + 3 * 2)];
        var at = 0;
        for (var index = 0; index < count; index++)
        {
            var vertex = vertices[index];
            data[at++] = 0;
            at = Big(data, at, (uint)Math.Round((vertex.X - left) / (right - left) * uint.MaxValue), 4);
            at = Big(data, at, (uint)Math.Round((vertex.Y - bottom) / (top - bottom) * uint.MaxValue), 4);
            at = Big(data, at, (uint)Math.Round(Unit(vertex.Red) * ushort.MaxValue), 2);
            at = Big(data, at, (uint)Math.Round(Unit(vertex.Green) * ushort.MaxValue), 2);
            at = Big(data, at, (uint)Math.Round(Unit(vertex.Blue) * ushort.MaxValue), 2);
        }

        var dictionary = new PdfDictionary();
        dictionary.Set(PdfShadingKeys.ShadingType, new PdfInteger(4));
        dictionary.Set(PdfShadingKeys.ColorSpace, new PdfName("DeviceRGB"));
        dictionary.Set(PdfShadingKeys.BitsPerCoordinate, new PdfInteger(32));
        dictionary.Set(PdfShadingKeys.BitsPerComponent, new PdfInteger(16));
        dictionary.Set(PdfShadingKeys.BitsPerFlag, new PdfInteger(8));
        dictionary.Set(PdfShadingKeys.Decode, Numbers(left, right, bottom, top, 0, 1, 0, 1, 0, 1));
        dictionary.Set(PdfKeys.Filter, new PdfName(PdfFilterNames.FlateDecode));
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
        {
            zlib.Write(data);
        }

        return new PdfStream(dictionary, output.ToArray());
    }

    /// <summary>A shading pattern of <paramref name="shading"/>, to fill or stroke with. <paramref name="matrix"/> maps
    /// the shading's space to the page's (or the form's) own default space - not to the transformation current when
    /// the pattern is used (ISO 32000-2, 8.7.2).</summary>
    public static PdfDictionary Pattern(PdfObject shading, PdfMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(shading);
        var pattern = new PdfDictionary();
        pattern.Set(PdfKeys.Type, new PdfName(PdfKeys.Pattern));
        pattern.Set(PdfShadingKeys.PatternType, new PdfInteger(2));
        pattern.Set(PdfShadingKeys.Shading, shading);
        pattern.Set(PdfShadingKeys.Matrix, Numbers(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F));
        return pattern;
    }

    private static PdfDictionary Gradient(int type, bool gray, IReadOnlyList<PdfGradientStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Count == 0)
        {
            throw new ArgumentException("A gradient has a stop at least.", nameof(stops));
        }

        var shading = new PdfDictionary();
        shading.Set(PdfShadingKeys.ShadingType, new PdfInteger(type));
        shading.Set(PdfShadingKeys.ColorSpace, new PdfName(gray ? "DeviceGray" : "DeviceRGB"));
        shading.Set(PdfShadingKeys.Function, Function(Ordered(stops), gray));
        shading.Set(PdfShadingKeys.Extend, new PdfArray([PdfBoolean.True, PdfBoolean.True]));
        return shading;
    }

    /// <summary>The stops by offset, each in 0..1, with the end colors at 0 and at 1; stops at one offset keep the
    /// order they were given in - a hard edge stays the way round it was drawn.</summary>
    private static List<PdfGradientStop> Ordered(IReadOnlyList<PdfGradientStop> stops)
    {
        var ordered = new List<PdfGradientStop>(stops.Count + 2);
        foreach (var stop in stops)
        {
            ordered.Add(stop with { Offset = Unit(stop.Offset) });
        }

        var stable = ordered.Select((stop, index) => new { Stop = stop, Index = index })
            .OrderBy(entry => entry.Stop.Offset).ThenBy(entry => entry.Index).Select(entry => entry.Stop).ToList();
        ordered.Clear();
        ordered.AddRange(stable);
        if (ordered[0].Offset > 0)
        {
            ordered.Insert(0, ordered[0] with { Offset = 0 });
        }

        if (ordered[^1].Offset < 1)
        {
            ordered.Add(ordered[^1] with { Offset = 1 });
        }

        return ordered;
    }

    private static PdfObject Function(List<PdfGradientStop> stops, bool gray)
    {
        if (stops.Count <= 2)
        {
            return Ramp(stops[0], stops[^1], gray);
        }

        var functions = new PdfArray();
        var bounds = new PdfArray();
        var encode = new PdfArray();
        for (var index = 0; index + 1 < stops.Count; index++)
        {
            functions.Add(Ramp(stops[index], stops[index + 1], gray));
            if (index > 0)
            {
                bounds.Add(new PdfReal(stops[index].Offset));
            }

            encode.Add(new PdfInteger(0));
            encode.Add(new PdfInteger(1));
        }

        var stitch = new PdfDictionary();
        stitch.Set(PdfKeys.FunctionType, new PdfInteger(3));
        stitch.Set(PdfKeys.Domain, Numbers(0, 1));
        stitch.Set(PdfKeys.Functions, functions);
        stitch.Set(PdfKeys.Bounds, bounds);
        stitch.Set(PdfKeys.Encode, encode);
        return stitch;
    }

    private static PdfDictionary Ramp(PdfGradientStop from, PdfGradientStop to, bool gray)
    {
        var ramp = new PdfDictionary();
        ramp.Set(PdfKeys.FunctionType, new PdfInteger(2));
        ramp.Set(PdfKeys.Domain, Numbers(0, 1));
        ramp.Set(PdfKeys.C0, gray ? Numbers(Unit(from.Red)) : Numbers(Unit(from.Red), Unit(from.Green), Unit(from.Blue)));
        ramp.Set(PdfKeys.C1, gray ? Numbers(Unit(to.Red)) : Numbers(Unit(to.Red), Unit(to.Green), Unit(to.Blue)));
        ramp.Set(PdfKeys.N, new PdfInteger(1));
        return ramp;
    }

    private static PdfArray Numbers(params double[] values)
    {
        var array = new PdfArray();
        foreach (var value in values)
        {
            array.Add(new PdfReal(value));
        }

        return array;
    }

    private static double Unit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private static int Big(byte[] data, int at, uint value, int bytes)
    {
        for (var index = bytes - 1; index >= 0; index--)
        {
            data[at++] = (byte)(value >> (index * 8));
        }

        return at;
    }
}
