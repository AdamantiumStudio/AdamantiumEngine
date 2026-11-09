using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Measurement;

/// <summary>A rectilinear measure (ISO 32000-2, 12.9.1, measure dictionary of subtype RL): the scale a viewport or a
/// measurement annotation is drawn at. <see cref="X"/> turns user space units into the drawing's units along x -
/// and <see cref="Y"/>, when given, along y; <see cref="Distance"/> and <see cref="Area"/> turn those into the units a
/// distance and an area are written in. Each is a chain of units, largest first, every next one taking the remainder
/// of the one before - feet, then inches.</summary>
public sealed class PdfMeasure
{
    private const string Type = "Measure";
    private const string Rectilinear = "RL";

    public PdfMeasure(string ratio, IReadOnlyList<PdfNumberFormat> x, IReadOnlyList<PdfNumberFormat> distance,
        IReadOnlyList<PdfNumberFormat> area)
    {
        if (x is not { Count: > 0 } || distance is not { Count: > 0 } || area is not { Count: > 0 })
        {
            throw new ArgumentException("A measure has an x axis, a distance and an area, each of a unit at least.");
        }

        Ratio = ratio ?? "";
        X = x;
        Distance = distance;
        Area = area;
    }

    /// <summary>The scale as text, as "1 in = 1 ft" (<c>R</c>).</summary>
    public string Ratio { get; }

    /// <summary>Units along x (<c>X</c>); the first one's factor turns user space units into it.</summary>
    public IReadOnlyList<PdfNumberFormat> X { get; }

    /// <summary>Units along y when they differ from x (<c>Y</c>); null otherwise.</summary>
    public IReadOnlyList<PdfNumberFormat> Y { get; init; }

    /// <summary>How many of x's first unit make one of y's first unit (<c>CYX</c>), when <see cref="Y"/> is given.
    /// </summary>
    public double YToX { get; init; } = 1;

    /// <summary>Units a distance is written in (<c>D</c>); the first one's factor turns x's first unit into it.
    /// </summary>
    public IReadOnlyList<PdfNumberFormat> Distance { get; }

    /// <summary>Units an area is written in (<c>A</c>); the first one's factor turns x's first unit, squared, into it.
    /// </summary>
    public IReadOnlyList<PdfNumberFormat> Area { get; }

    /// <summary>A scale of <paramref name="unitsPerPoint"/> <paramref name="unit"/> to a user space unit, written
    /// to <paramref name="decimals"/> places: what a calibration gives - a line of known length drawn on the page.
    /// </summary>
    public static PdfMeasure Scaled(string ratio, string unit, double unitsPerPoint, int decimals = 2)
    {
        var precision = (int)Math.Pow(10, Math.Clamp(decimals, 0, 9));
        return new PdfMeasure(ratio,
            [new PdfNumberFormat(unit, unitsPerPoint) { Precision = precision }],
            [new PdfNumberFormat(unit, 1) { Precision = precision }],
            [new PdfNumberFormat("sq " + unit, 1) { Precision = precision }]);
    }

    /// <summary>The rectilinear measure <paramref name="value"/> describes, or null when it is none - another kind
    /// (a geospatial one), or missing what it needs.</summary>
    public static PdfMeasure From(PdfObject value, IPdfObjectResolver resolver)
    {
        if (resolver.Resolve(value) is not PdfDictionary dictionary ||
            ((resolver.Resolve(dictionary[PdfKeys.Subtype]) as PdfName)?.Value ?? Rectilinear) != Rectilinear)
        {
            return null;
        }

        var x = Formats(dictionary["X"], resolver);
        var distance = Formats(dictionary["D"], resolver);
        var area = Formats(dictionary["A"], resolver);
        if (x == null || distance == null || area == null)
        {
            return null;
        }

        var yToX = PdfValues.Number(dictionary["CYX"], resolver, 1);
        return new PdfMeasure((resolver.Resolve(dictionary["R"]) as PdfString)?.ToText(), x, distance, area)
        {
            Y = Formats(dictionary["Y"], resolver),
            YToX = double.IsFinite(yToX) && yToX > 0 ? yToX : 1,
        };
    }

    /// <summary>The measure dictionary.</summary>
    public PdfDictionary ToDictionary()
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfKeys.Type, new PdfName(Type));
        dictionary.Set(PdfKeys.Subtype, new PdfName(Rectilinear));
        dictionary.Set("R", PdfString.FromText(Ratio));
        dictionary.Set("X", Array(X));
        if (Y != null)
        {
            dictionary.Set("Y", Array(Y));
            dictionary.Set("CYX", new PdfReal(YToX));
        }

        dictionary.Set("D", Array(Distance));
        dictionary.Set("A", Array(Area));
        return dictionary;
    }

    /// <summary>A displacement of (<paramref name="dx"/>, <paramref name="dy"/>) user space units as a length in x's
    /// first unit.</summary>
    public double Length(double dx, double dy)
    {
        var x = dx * X[0].Factor;
        var y = Y != null ? dy * Y[0].Factor * YToX : dy * X[0].Factor;
        return Math.Sqrt(x * x + y * y);
    }

    /// <summary>An area of <paramref name="userArea"/> square user space units in x's first unit, squared.</summary>
    public double AreaOf(double userArea) =>
        userArea * X[0].Factor * (Y != null ? Y[0].Factor * YToX : X[0].Factor);

    /// <summary>The distance of a displacement of (<paramref name="dx"/>, <paramref name="dy"/>) user space units,
    /// written in <see cref="Distance"/>'s units.</summary>
    public string FormatDistance(double dx, double dy) => Format(Length(dx, dy), Distance);

    /// <summary>An area of <paramref name="userArea"/> square user space units, written in <see cref="Area"/>'s
    /// units.</summary>
    public string FormatArea(double userArea) => Format(AreaOf(Math.Abs(userArea)), Area);

    /// <summary><paramref name="value"/>, in the unit before <paramref name="formats"/>' first, written through the
    /// chain: each unit but the last takes the whole part, the next the remainder.</summary>
    public static string Format(double value, IReadOnlyList<PdfNumberFormat> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        if (formats.Count == 0)
        {
            return "";
        }

        var count = formats.Count;
        var total = Math.Abs(value);
        foreach (var format in formats)
        {
            total *= format.Factor;
        }

        var lastFormat = formats[count - 1];
        var parts = new double[count];
        var rest = lastFormat.RoundLast(total);
        for (var i = count - 1; i > 0; i--)
        {
            var factor = formats[i].Factor;
            if (!(factor > 0))
            {
                parts[i] = i == count - 1 ? rest : 0;
                rest = 0;
                continue;
            }

            var carry = Math.Floor(rest / factor);
            var part = rest - carry * factor;
            var resolution = i == count - 1 ? lastFormat.Resolution : 1;
            if (part > factor - resolution / 2)
            {
                (carry, part) = (carry + 1, 0);
            }

            parts[i] = Math.Max(0, part);
            rest = carry;
        }

        parts[0] = rest;
        var text = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            text.Append(i > 0 ? " " : "").Append(formats[i].Format(parts[i], i < count - 1));
        }

        var written = text.ToString();
        return value < 0 && written.Any(c => c is >= '1' and <= '9') ? "-" + written : written;
    }

    private static PdfArray Array(IReadOnlyList<PdfNumberFormat> formats) =>
        new(formats.Select(format => (PdfObject)format.ToDictionary()).ToList());

    private static IReadOnlyList<PdfNumberFormat> Formats(PdfObject value, IPdfObjectResolver resolver)
    {
        if (resolver.Resolve(value) is not PdfArray array)
        {
            return null;
        }

        var formats = new List<PdfNumberFormat>();
        foreach (var item in array)
        {
            if (PdfNumberFormat.From(item, resolver) is { } format)
            {
                formats.Add(format);
            }
        }

        return formats.Count > 0 ? formats : null;
    }
}
