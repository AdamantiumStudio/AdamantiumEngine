using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Adamantium.Fonts;
using Adamantium.Fonts.Subsetting;
using Adamantium.Pdf.Filters;
using Adamantium.Pdf.Fonts;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Writing;

/// <summary>A font a new document carries (ISO 32000-2, 9.7): the glyphs drawn and no more, as a composite font whose
/// codes are the glyphs' indices, with their widths and the text they stand for - what is drawn in it is found and copied
/// as text. Written when the document is finished, its glyphs known by then.</summary>
public sealed class PdfEmbeddedFont
{
    private const int MaxRanges = 100;   // a bfchar section holds no more (ISO 32000-2, 9.10.3)
    private const ushort PostScriptName = 6;   // in the 'name' table

    private readonly PdfDocumentWriter _writer;
    private readonly SortedDictionary<uint, string> _glyphs = new();
    private readonly Dictionary<uint, bool> _drawable = [];
    private PdfReference _reference;

    internal PdfEmbeddedFont(IFont font, PdfDocumentWriter writer)
    {
        Font = font;
        _writer = writer;
    }

    public IFont Font { get; }

    /// <summary>Whether text was begun in it: one only asked for is not written.</summary>
    internal bool IsDrawnIn => _reference != null;

    internal PdfReference Reference => _reference ??= _writer.Reserve();

    /// <summary>Whether a document may carry <paramref name="font"/>.</summary>
    public static bool CanEmbed(IFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return font.Embedding.IsAllowed && font.Typeface is { GlyphCount: > 0 } && font.UnitsPerEm > 0;
    }

    /// <summary>Whether <paramref name="glyph"/>'s outline can be drawn in the font: not one made beside its file's own, as
    /// a color layer's is, nor one that is a picture alone.</summary>
    public bool CanDraw(uint glyph)
    {
        if (!_drawable.TryGetValue(glyph, out var drawable))
        {
            drawable = CffSubset.CanHold(Font, glyph) && (Font.GetGlyphByIndex(glyph).HasOutlines ||
                Font.GetColorBitmap(glyph, Font.UnitsPerEm) == null && Font.GetColorLayers(glyph).Count == 0 &&
                Font.GetColorPaint(glyph).Count == 0);
            _drawable[glyph] = drawable;
        }

        return drawable;
    }

    /// <summary>Has the font carry <paramref name="glyph"/>, standing here for <paramref name="text"/> - empty for nothing,
    /// null for what the font says it stands for. Returns whether that is other than what it stands for wherever it is
    /// drawn: the first text given of those the font has for it, or the first given for one it has none for.</summary>
    internal bool Use(uint glyph, string text)
    {
        if (!CanDraw(glyph))
        {
            throw new ArgumentOutOfRangeException(nameof(glyph), glyph, "Not a glyph the font can draw.");
        }

        if (!_glyphs.TryGetValue(glyph, out var known) || known == null && !string.IsNullOrEmpty(text))
        {
            var own = Font.GetGlyphText(glyph);
            known = own.Contains(text) ? text : own.FirstOrDefault(item => item.Length > 0);
            // Nothing said of it stays so: what says otherwise later says it for itself. Null waits for the first text.
            known ??= text == null ? string.Empty : text.Length > 0 ? text : null;
            _glyphs[glyph] = known;
        }

        return text != null && text != known;
    }

    internal void Write(PdfDocumentWriter writer)
    {
        var asked = Font.Embedding.MaySubset
            ? _glyphs.Keys
            : Enumerable.Range(0, (int)Font.Typeface.GlyphCount).Select(index => (uint)index);
        var glyphs = CffSubset.IndicesOf(Font, asked);
        var name = Name();

        var program = Stream(CffSubset.Write(Font, glyphs, name));
        program.Dictionary.Set(PdfFontKeys.Subtype, new PdfName("CIDFontType0C"));
        var descriptor = new PdfDictionary();
        descriptor.Set(PdfKeys.Type, new PdfName("FontDescriptor"));
        descriptor.Set(PdfFontKeys.FontName, new PdfName(name));
        descriptor.Set(PdfFontKeys.Flags, new PdfInteger((long)Flags()));
        descriptor.Set(PdfFontKeys.FontBBox, Box(glyphs));
        descriptor.Set(PdfFontKeys.ItalicAngle, new PdfInteger(Font.Style == FontStyle.Normal ? 0 : -12));
        // A font without 'OS/2' has the line's alone.
        var metrics = Font.Ascender != 0 || Font.Descender != 0;
        descriptor.Set(PdfFontKeys.Ascent, new PdfInteger(Em(metrics ? Font.Ascender : Font.LineAscent)));
        descriptor.Set(PdfFontKeys.Descent, new PdfInteger(Em(metrics ? Font.Descender : -Font.LineDescent)));
        descriptor.Set("CapHeight", new PdfInteger(Em(Font.CapsHeight)));
        // Not said by any font: as heavy as its weight, by the estimate in common use.
        descriptor.Set("StemV", new PdfInteger(10 + 220 * Math.Max(0, Font.Weight.Value - 50) / 900));
        descriptor.Set(PdfFontKeys.FontFile3, program);

        var system = new PdfDictionary();
        system.Set(PdfFontKeys.Registry, PdfString.FromText("Adobe"));
        system.Set(PdfFontKeys.Ordering, PdfString.FromText("Identity"));
        system.Set("Supplement", new PdfInteger(0));
        var descendant = new PdfDictionary();
        descendant.Set(PdfKeys.Type, new PdfName(PdfKeys.Font));
        descendant.Set(PdfFontKeys.Subtype, new PdfName("CIDFontType0"));
        descendant.Set(PdfFontKeys.BaseFont, new PdfName(name));
        descendant.Set(PdfFontKeys.CIDSystemInfo, system);
        descendant.Set(PdfFontKeys.FontDescriptor, writer.Add(descriptor));
        descendant.Set(PdfFontKeys.W, Widths(glyphs));

        var font = new PdfDictionary();
        font.Set(PdfKeys.Type, new PdfName(PdfKeys.Font));
        font.Set(PdfFontKeys.Subtype, new PdfName("Type0"));
        font.Set(PdfFontKeys.BaseFont, new PdfName(name + "-Identity-H"));
        font.Set(PdfFontKeys.Encoding, new PdfName("Identity-H"));
        font.Set(PdfFontKeys.DescendantFonts, new PdfArray([writer.Add(descendant)]));
        font.Set(PdfFontKeys.ToUnicode, Stream(Encoding.ASCII.GetBytes(ToUnicode(name))));
        writer.WriteReserved(Reference, font);
    }

    /// <summary>The font's name after a tag of its own: two subsets of one font are two fonts (ISO 32000-2, 9.9.2).
    /// </summary>
    private string Name()
    {
        // A variable font's instance has the PostScript name of the font it is made of.
        var said = Font.Variations.Count == 0 ? Font.GetName(PostScriptName) : null;
        var letters = (said ?? Font.FullName ?? string.Empty).Where(letter => char.IsAsciiLetterOrDigit(letter) || letter is '-').ToArray();
        var name = letters.Length > 0 ? new string(letters, 0, Math.Min(letters.Length, 60)) : "Font";
        if (!Font.Embedding.MaySubset)
        {
            // Whole, it takes no tag; but two fonts may have one name, as instances of a variable font do.
            return name + "-" + Reference.Id.Number;
        }

        var number = (uint)Reference.Id.Number;
        foreach (var glyph in _glyphs.Keys)
        {
            number = number * 31 + glyph;
        }

        var tag = new char[6];
        for (var at = 0; at < tag.Length; at++)
        {
            tag[at] = (char)('A' + number % 26);
            number = number / 26 + (uint)Reference.Id.Number;
        }

        return new string(tag) + "+" + name;
    }

    private PdfFontFlags Flags()
    {
        var flags = PdfFontFlags.Symbolic;
        if (Font.IsMonospaced)
        {
            flags |= PdfFontFlags.FixedPitch;
        }

        return Font.Style == FontStyle.Normal ? flags : flags | PdfFontFlags.Italic;
    }

    private PdfArray Box(IReadOnlyList<uint> glyphs)
    {
        double left = 0, bottom = 0, right = 0, top = 0;
        foreach (var index in glyphs)
        {
            var box = Font.GetGlyphByIndex(index).BoundingRectangle;
            left = Math.Min(left, box.X);
            bottom = Math.Min(bottom, box.Y);
            right = Math.Max(right, box.X + box.Width);
            top = Math.Max(top, box.Y + box.Height);
        }

        var scale = 1000.0 / Font.UnitsPerEm;
        return new PdfArray([new PdfInteger((int)Math.Floor(left * scale)), new PdfInteger((int)Math.Floor(bottom * scale)),
            new PdfInteger((int)Math.Ceiling(right * scale)), new PdfInteger((int)Math.Ceiling(top * scale))]);
    }

    // [first [width width ...] ...], a run of neighbors together.
    private PdfArray Widths(IReadOnlyList<uint> glyphs)
    {
        var widths = new List<PdfObject>();
        for (var at = 0; at < glyphs.Count;)
        {
            var run = new List<PdfObject>();
            var first = at;
            do
            {
                run.Add(new PdfReal(Math.Round(Font.GetAdvanceWidth(glyphs[at]) * 1000.0 / Font.UnitsPerEm, 3)));
                at++;
            }
            while (at < glyphs.Count && glyphs[at] == glyphs[at - 1] + 1);

            widths.Add(new PdfInteger(glyphs[first]));
            widths.Add(new PdfArray(run));
        }

        return new PdfArray(widths);
    }

    private string ToUnicode(string name)
    {
        var map = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n" +
                                    "/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /");
        map.Append(name).Append("-UCS def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        var known = _glyphs.Where(glyph => !string.IsNullOrEmpty(glyph.Value)).ToList();
        for (var at = 0; at < known.Count; at += MaxRanges)
        {
            var section = known.Skip(at).Take(MaxRanges).ToList();
            map.Append(section.Count).Append(" beginbfchar\n");
            foreach (var (glyph, text) in section)
            {
                map.Append('<').Append(glyph.ToString("X4")).Append("> <")
                    .Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text))).Append(">\n");
            }

            map.Append("endbfchar\n");
        }

        return map.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n").ToString();
    }

    private int Em(double units) => (int)Math.Round(units * 1000.0 / Font.UnitsPerEm);

    private static PdfStream Stream(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
        {
            zlib.Write(data);
        }

        var dictionary = new PdfDictionary();
        dictionary.Set(PdfKeys.Filter, new PdfName(PdfFilterNames.FlateDecode));
        return new PdfStream(dictionary, output.ToArray());
    }
}
