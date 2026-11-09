using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Filters;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Writing;

/// <summary>Builds a content stream (ISO 32000-2, 8.2): paths, their paint, line style and opacity - what an
/// annotation's appearance is drawn with (<see cref="ToFormXObject"/>). Text is drawn by its outlines, as paths.
/// Opacity goes through graphics state parameter dictionaries the builder names itself.</summary>
public sealed class PdfContentBuilder
{
    private readonly StringBuilder _operations = new();
    private readonly Dictionary<(double Stroke, double Fill), string> _opacities = [];

    public PdfContentBuilder SaveState() => Operator("q");

    public PdfContentBuilder RestoreState() => Operator("Q");

    /// <summary>Concatenates the matrix [<paramref name="a"/> <paramref name="b"/> <paramref name="c"/>
    /// <paramref name="d"/> <paramref name="e"/> <paramref name="f"/>] to the current transformation.</summary>
    public PdfContentBuilder Transform(double a, double b, double c, double d, double e, double f) =>
        Operator("cm", a, b, c, d, e, f);

    public PdfContentBuilder LineWidth(double width) => Operator("w", Math.Max(0, width));

    /// <summary>0 butt, 1 round, 2 projecting square.</summary>
    public PdfContentBuilder LineCap(int cap) => Operator("J", Math.Clamp(cap, 0, 2));

    /// <summary>0 miter, 1 round, 2 bevel.</summary>
    public PdfContentBuilder LineJoin(int join) => Operator("j", Math.Clamp(join, 0, 2));

    public PdfContentBuilder MiterLimit(double limit) => Operator("M", Math.Max(1, limit));

    /// <summary>Dashes of <paramref name="pattern"/> - on, off, on... - starting <paramref name="phase"/> into it; an
    /// empty pattern draws solid.</summary>
    public PdfContentBuilder Dash(IReadOnlyList<double> pattern, double phase = 0)
    {
        _operations.Append('[');
        for (var i = 0; i < (pattern?.Count ?? 0); i++)
        {
            _operations.Append(i > 0 ? " " : "").Append(PdfObjectWriter.Number(Math.Max(0, pattern[i])));
        }

        _operations.Append("] ").Append(PdfObjectWriter.Number(phase)).Append(" d\n");
        return this;
    }

    /// <summary>The stroke color in DeviceRGB, each component 0..1.</summary>
    public PdfContentBuilder StrokeColor(double red, double green, double blue) =>
        Operator("RG", Unit(red), Unit(green), Unit(blue));

    /// <summary>The fill color in DeviceRGB, each component 0..1.</summary>
    public PdfContentBuilder FillColor(double red, double green, double blue) =>
        Operator("rg", Unit(red), Unit(green), Unit(blue));

    /// <summary>The opacity of strokes and of fills, each 0..1, from here on.</summary>
    public PdfContentBuilder Opacity(double stroke, double fill)
    {
        var key = (Unit(stroke), Unit(fill));
        if (!_opacities.TryGetValue(key, out var name))
        {
            name = "GS" + _opacities.Count;
            _opacities[key] = name;
        }

        _operations.Append('/').Append(name).Append(" gs\n");
        return this;
    }

    public PdfContentBuilder MoveTo(double x, double y) => Operator("m", x, y);

    public PdfContentBuilder LineTo(double x, double y) => Operator("l", x, y);

    /// <summary>A cubic Bézier curve to (<paramref name="x3"/>, <paramref name="y3"/>) through its two controls.
    /// </summary>
    public PdfContentBuilder CurveTo(double x1, double y1, double x2, double y2, double x3, double y3) =>
        Operator("c", x1, y1, x2, y2, x3, y3);

    public PdfContentBuilder Rectangle(double x, double y, double width, double height) =>
        Operator("re", x, y, width, height);

    public PdfContentBuilder ClosePath() => Operator("h");

    public PdfContentBuilder Stroke() => Operator("S");

    /// <summary>Fills the path, by the nonzero winding rule or, with <paramref name="evenOdd"/>, the even-odd one.
    /// </summary>
    public PdfContentBuilder Fill(bool evenOdd = false) => Operator(evenOdd ? "f*" : "f");

    public PdfContentBuilder FillAndStroke(bool evenOdd = false) => Operator(evenOdd ? "B*" : "B");

    /// <summary>Clips what follows to the path, ending it unpainted.</summary>
    public PdfContentBuilder Clip(bool evenOdd = false) => Operator(evenOdd ? "W* n" : "W n");

    public PdfContentBuilder EndPath() => Operator("n");

    /// <summary>The operators built so far.</summary>
    public byte[] ToContent() => Encoding.ASCII.GetBytes(_operations.ToString());

    /// <summary>The operators as a form XObject of <paramref name="bbox"/>, its resources named - an appearance
    /// stream (ISO 32000-2, 12.5.5).</summary>
    public PdfStream ToFormXObject(PdfRectangle bbox)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfKeys.Type, new PdfName(PdfKeys.XObject));
        dictionary.Set(PdfKeys.Subtype, new PdfName(PdfKeys.Form));
        dictionary.Set(PdfKeys.BBox, new PdfArray([new PdfReal(bbox.Left), new PdfReal(bbox.Bottom),
            new PdfReal(bbox.Right), new PdfReal(bbox.Top)]));
        var resources = new PdfDictionary();
        if (_opacities.Count > 0)
        {
            var states = new PdfDictionary();
            foreach (var ((stroke, fill), name) in _opacities)
            {
                var state = new PdfDictionary();
                state.Set(PdfKeys.Type, new PdfName(PdfKeys.ExtGState));
                state.Set(PdfKeys.StrokeOpacity, new PdfReal(stroke));
                state.Set(PdfKeys.FillOpacity, new PdfReal(fill));
                states.Set(name, state);
            }

            resources.Set(PdfKeys.ExtGState, states);
        }

        dictionary.Set(PdfKeys.Resources, resources);
        dictionary.Set(PdfKeys.Filter, new PdfName(PdfFilterNames.FlateDecode));
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
        {
            zlib.Write(ToContent());
        }

        return new PdfStream(dictionary, output.ToArray());
    }

    private static double Unit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

    private PdfContentBuilder Operator(string name, params double[] operands)
    {
        foreach (var operand in operands)
        {
            _operations.Append(PdfObjectWriter.Number(operand)).Append(' ');
        }

        _operations.Append(name).Append('\n');
        return this;
    }
}
