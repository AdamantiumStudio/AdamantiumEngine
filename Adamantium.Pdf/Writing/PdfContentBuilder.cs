using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Filters;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Shadings;

namespace Adamantium.Pdf.Writing;

/// <summary>Builds a content stream (ISO 32000-2, 8.2): paths, their paint, line style and opacity, pictures, forms,
/// shadings and soft masks - a page (<see cref="ToContentStream"/> and <see cref="ToResources"/>) or an annotation's
/// appearance (<see cref="ToFormXObject(PdfRectangle)"/>). Text is drawn in fonts a new document carries
/// (<see cref="PdfDocumentWriter.Font"/>), or by its outlines, as paths. Opacity, blend modes
/// and soft masks go through graphics state parameter dictionaries the builder names itself, and so do the pictures,
/// forms, shadings and patterns drawn: each one object, named once however often it is drawn.</summary>
public sealed class PdfContentBuilder
{
    private readonly StringBuilder _operations = new();
    private readonly Dictionary<(double Stroke, double Fill), string> _opacities = [];
    private readonly Dictionary<PdfBlendMode, string> _blends = [];
    private readonly Dictionary<(PdfStream Group, bool Luminosity), string> _masks = [];
    private readonly Dictionary<PdfObject, string> _xObjects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfObject, string> _shadings = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfObject, string> _patterns = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfObject, string> _fonts = new(ReferenceEqualityComparer.Instance);
    private PdfEmbeddedFont _font;

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

    /// <summary>How what is painted from here on mixes with what lies under it (ISO 32000-2, 11.3.5).</summary>
    public PdfContentBuilder BlendMode(PdfBlendMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!_blends.TryGetValue(mode, out var name))
        {
            name = "BM" + _blends.Count;
            _blends[mode] = name;
        }

        _operations.Append('/').Append(name).Append(" gs\n");
        return this;
    }

    /// <summary>What is painted from here on is masked by <paramref name="group"/>, a transparency group form
    /// (<see cref="ToFormXObject(PdfRectangle, PdfTransparencyGroup)"/>): by its luminosity, or with
    /// <paramref name="luminosity"/> false by its alpha (ISO 32000-2, 11.6.5.2). It lasts until the state is restored.
    /// </summary>
    public PdfContentBuilder SoftMask(PdfStream group, bool luminosity)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Dictionary[PdfGraphicsKeys.FormGroup] is not PdfDictionary attributes ||
            attributes[PdfGraphicsKeys.Kind] is not PdfName { Value: PdfGraphicsKeys.Transparency })
        {
            throw new ArgumentException("A soft mask is a transparency group form (ISO 32000-2, 11.6.5.2).",
                nameof(group));
        }

        if (luminosity && attributes[PdfGraphicsKeys.ColorSpace] == null)
        {
            throw new ArgumentException("A mask read by luminosity names the space its group is blended in.",
                nameof(group));
        }

        if (!_masks.TryGetValue((group, luminosity), out var name))
        {
            name = "SM" + _masks.Count;
            _masks[(group, luminosity)] = name;
        }

        _operations.Append('/').Append(name).Append(" gs\n");
        return this;
    }

    /// <summary>Paints the external object <paramref name="xObject"/> - a picture fills the unit square of the current
    /// transformation, a form is drawn under it (ISO 32000-2, 8.8).</summary>
    public PdfContentBuilder Draw(PdfStream xObject)
    {
        ArgumentNullException.ThrowIfNull(xObject);
        var prefix = xObject.Dictionary[PdfKeys.Subtype] is PdfName { Value: PdfKeys.Form } ? "Fm" : "Im";
        _operations.Append('/').Append(Named(_xObjects, xObject, prefix)).Append(" Do\n");
        return this;
    }

    /// <summary>Paints <paramref name="shading"/> (<see cref="PdfShadings"/>) over the whole clip, in the current
    /// transformation (ISO 32000-2, 8.7.4.2).</summary>
    public PdfContentBuilder Shade(PdfObject shading)
    {
        ArgumentNullException.ThrowIfNull(shading);
        _operations.Append('/').Append(Named(_shadings, shading, "Sh")).Append(" sh\n");
        return this;
    }

    /// <summary>Fills from here on with <paramref name="pattern"/> - a shading pattern (<see
    /// cref="PdfShadings.Pattern"/>).</summary>
    public PdfContentBuilder FillPattern(PdfObject pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        _operations.Append("/Pattern cs /").Append(Named(_patterns, pattern, "P")).Append(" scn\n");
        return this;
    }

    /// <summary>Strokes from here on with <paramref name="pattern"/>, a shading pattern.</summary>
    public PdfContentBuilder StrokePattern(PdfObject pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        _operations.Append("/Pattern CS /").Append(Named(_patterns, pattern, "P")).Append(" SCN\n");
        return this;
    }

    private static string Named(Dictionary<PdfObject, string> names, PdfObject value, string prefix)
    {
        if (!names.TryGetValue(value, out var name))
        {
            name = prefix + names.Count;
            names[value] = name;
        }

        return name;
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

    /// <summary>Begins text (ISO 32000-2, 9.4) set in <paramref name="font"/>, one unit to the em: the size is in the
    /// matrix of each <see cref="Glyph"/>. Ended by <see cref="EndText"/>; paths are not drawn between the two.</summary>
    public PdfContentBuilder BeginText(PdfEmbeddedFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (_font != null)
        {
            throw new InvalidOperationException("Text is begun already.");
        }

        var name = Named(_fonts, font.Reference, "F");
        _font = font;
        _operations.Append("BT\n/").Append(name).Append(" 1 Tf\n");
        return this;
    }

    /// <summary>Draws the glyph of index <paramref name="glyph"/>, its em square placed by the matrix, standing for
    /// <paramref name="text"/> - empty for nothing, null for what the font says it stands for. One the font cannot draw
    /// (<see cref="PdfEmbeddedFont.CanDraw"/>) is refused.</summary>
    public PdfContentBuilder Glyph(uint glyph, double a, double b, double c, double d, double e, double f,
        string text = null)
    {
        if (_font == null)
        {
            throw new InvalidOperationException("A glyph is drawn between BeginText and EndText.");
        }

        // Standing here for other than what the font has it stand for, it says so itself (ISO 32000-2, 14.9.4).
        var saysItsText = _font.Use(glyph, text);
        if (saysItsText)
        {
            _operations.Append("/Span <</ActualText <FEFF").Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text)))
                .Append(">>> BDC\n");
        }

        Operator("Tm", a, b, c, d, e, f);
        _operations.Append('<').Append(glyph.ToString("X4")).Append("> Tj\n");
        return saysItsText ? Operator("EMC") : this;
    }

    public PdfContentBuilder EndText()
    {
        if (_font == null)
        {
            throw new InvalidOperationException("No text is begun.");
        }

        _font = null;
        return Operator("ET");
    }

    /// <summary>The operators built so far; not asked for within text.</summary>
    public byte[] ToContent() => _font == null
        ? Encoding.ASCII.GetBytes(_operations.ToString())
        : throw new InvalidOperationException("The text begun is not ended.");

    /// <summary>The operators as a form XObject of <paramref name="bbox"/>, its resources named - an appearance
    /// stream (ISO 32000-2, 12.5.5).</summary>
    public PdfStream ToFormXObject(PdfRectangle bbox) => ToFormXObject(bbox, null);

    /// <summary>The operators as a form XObject of <paramref name="bbox"/>; with <paramref name="group"/>, a
    /// transparency group (ISO 32000-2, 11.6.6) - drawn as one, faded or masked as one. The pictures, forms and
    /// masks it draws stay inside it as streams; a writer makes them indirect objects when it writes it.</summary>
    public PdfStream ToFormXObject(PdfRectangle bbox, PdfTransparencyGroup group)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfKeys.Type, new PdfName(PdfKeys.XObject));
        dictionary.Set(PdfKeys.Subtype, new PdfName(PdfKeys.Form));
        dictionary.Set(PdfKeys.BBox, new PdfArray([new PdfReal(bbox.Left), new PdfReal(bbox.Bottom),
            new PdfReal(bbox.Right), new PdfReal(bbox.Top)]));
        if (group != null)
        {
            var attributes = new PdfDictionary();
            attributes.Set(PdfKeys.Type, new PdfName(PdfGraphicsKeys.FormGroup));
            attributes.Set(PdfGraphicsKeys.Kind, new PdfName(PdfGraphicsKeys.Transparency));
            if (group.ColorSpace != null)
            {
                if (group.ColorSpace.Family is not (PdfColorSpaceFamilies.DeviceGray or PdfColorSpaceFamilies.DeviceRGB
                    or PdfColorSpaceFamilies.DeviceCMYK))
                {
                    throw new NotSupportedException(
                        $"A group is written blended in a device space, not in {group.ColorSpace.Family}.");
                }

                attributes.Set(PdfGraphicsKeys.ColorSpace, new PdfName(group.ColorSpace.Family));
            }
            if (group.Isolated)
            {
                attributes.Set(PdfGraphicsKeys.Isolated, PdfBoolean.True);
            }

            if (group.Knockout)
            {
                attributes.Set(PdfGraphicsKeys.Knockout, PdfBoolean.True);
            }

            dictionary.Set(PdfGraphicsKeys.FormGroup, attributes);
        }

        dictionary.Set(PdfKeys.Resources, ToResources());
        dictionary.Set(PdfKeys.Filter, new PdfName(PdfFilterNames.FlateDecode));
        return new PdfStream(dictionary, Deflate(ToContent()));
    }

    /// <summary>The operators as a tiling pattern (ISO 32000-2, 8.7.3.1): one cell of <paramref name="cell"/>, laid
    /// every <paramref name="xStep"/> across and <paramref name="yStep"/> down, colored by what it draws. Fill or stroke
    /// with it by <see cref="FillPattern"/> or <see cref="StrokePattern"/>. <paramref name="matrix"/> maps the cell's
    /// space to the page's (or the form's) own default space - not to the transformation current when it is used.
    /// </summary>
    public PdfStream ToTilingPattern(PdfRectangle cell, double xStep, double yStep, PdfMatrix matrix)
    {
        if (!(cell.Width > 0) || !(cell.Height > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(cell), "A pattern's cell has a width and a height.");
        }

        if (!double.IsFinite(xStep) || xStep == 0 || !double.IsFinite(yStep) || yStep == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(xStep), "A pattern steps a finite, non-zero way each way.");
        }

        var dictionary = new PdfDictionary();
        dictionary.Set(PdfKeys.Type, new PdfName(PdfKeys.Pattern));
        dictionary.Set(PdfShadingKeys.PatternType, new PdfInteger(1));
        dictionary.Set(PdfShadingKeys.PaintType, new PdfInteger(1));
        dictionary.Set(PdfShadingKeys.TilingType, new PdfInteger(1));
        dictionary.Set(PdfKeys.BBox, new PdfArray([new PdfReal(cell.Left), new PdfReal(cell.Bottom),
            new PdfReal(cell.Right), new PdfReal(cell.Top)]));
        dictionary.Set(PdfShadingKeys.XStep, new PdfReal(xStep));
        dictionary.Set(PdfShadingKeys.YStep, new PdfReal(yStep));
        dictionary.Set(PdfShadingKeys.Matrix, new PdfArray([new PdfReal(matrix.A), new PdfReal(matrix.B),
            new PdfReal(matrix.C), new PdfReal(matrix.D), new PdfReal(matrix.E), new PdfReal(matrix.F)]));
        dictionary.Set(PdfKeys.Resources, ToResources());
        dictionary.Set(PdfKeys.Filter, new PdfName(PdfFilterNames.FlateDecode));
        return new PdfStream(dictionary, Deflate(ToContent()));
    }

    /// <summary>The operators as a page's content stream, compressed; its resources are <see cref="ToResources"/>.
    /// </summary>
    public PdfStream ToContentStream()
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfKeys.Filter, new PdfName(PdfFilterNames.FlateDecode));
        return new PdfStream(dictionary, Deflate(ToContent()));
    }

    /// <summary>The resources the operators name: graphics states, external objects, shadings, patterns, fonts.</summary>
    public PdfDictionary ToResources()
    {
        var resources = new PdfDictionary();
        if (_opacities.Count > 0 || _blends.Count > 0 || _masks.Count > 0)
        {
            var states = new PdfDictionary();
            foreach (var ((stroke, fill), name) in _opacities)
            {
                var state = State();
                state.Set(PdfKeys.StrokeOpacity, new PdfReal(stroke));
                state.Set(PdfKeys.FillOpacity, new PdfReal(fill));
                states.Set(name, state);
            }

            foreach (var (mode, name) in _blends)
            {
                var state = State();
                state.Set(PdfGraphicsKeys.BlendMode, new PdfName(mode.ToString()));
                states.Set(name, state);
            }

            foreach (var ((group, luminosity), name) in _masks)
            {
                var mask = new PdfDictionary();
                mask.Set(PdfKeys.Type, new PdfName(PdfKeys.Mask));
                mask.Set(PdfGraphicsKeys.Kind,
                    new PdfName(luminosity ? PdfGraphicsKeys.Luminosity : PdfGraphicsKeys.Alpha));
                mask.Set(PdfGraphicsKeys.Group, group);
                var state = State();
                state.Set(PdfGraphicsKeys.SoftMask, mask);
                states.Set(name, state);
            }

            resources.Set(PdfKeys.ExtGState, states);
        }

        Section(resources, PdfKeys.XObject, _xObjects);
        Section(resources, PdfKeys.Shading, _shadings);
        Section(resources, PdfKeys.Pattern, _patterns);
        Section(resources, PdfKeys.Font, _fonts);
        return resources;
    }

    private static PdfDictionary State()
    {
        var state = new PdfDictionary();
        state.Set(PdfKeys.Type, new PdfName(PdfKeys.ExtGState));
        return state;
    }

    private static void Section(PdfDictionary resources, string key, Dictionary<PdfObject, string> names)
    {
        if (names.Count == 0)
        {
            return;
        }

        var section = new PdfDictionary();
        foreach (var (value, name) in names)
        {
            section.Set(name, value);
        }

        resources.Set(key, section);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
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
