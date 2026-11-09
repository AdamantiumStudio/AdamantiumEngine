using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Adamantium.Fonts;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Fonts;
using Adamantium.Pdf.Objects;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfFontTests
{
    private const string TrueTypePath = "Fonts/SourceSans3-Bold.ttf";
    private const string OpenTypePath = "Fonts/SourceSans3-Regular.otf";

    /// <summary>A file whose object 3 is <paramref name="font"/>, with <paramref name="more"/> after it.</summary>
    private static (PdfDocument Document, PdfFont Font) Open(string font, params byte[][] more)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        builder.Add(font);
        foreach (var body in more)
        {
            builder.Add(body);
        }

        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        return (document, PdfFonts.Create(new PdfReference(3, 0), document));
    }

    private static byte[] CffOf(string path)
    {
        var data = File.ReadAllBytes(path);
        var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var record = data.AsSpan(12 + i * 16);
            if (record[..4].SequenceEqual("CFF "u8))
            {
                return data.AsSpan((int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]),
                    (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..])).ToArray();
            }
        }

        throw new InvalidOperationException("No CFF table");
    }

    private static PdfCharacter Code(uint code) => new(code, 1);

    [Test]
    public void EmbeddedCffFindsGlyphsByTheNamesOfItsEncoding()
    {
        var (_, font) = Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /ABCDEF+SourceSans3-Regular /FirstChar 65 /LastChar 66 " +
            "/Widths [600 700] /Encoding << /BaseEncoding /WinAnsiEncoding /Differences [66 /Aacute] >> " +
            "/FontDescriptor 4 0 R >>",
            PdfFileBuilder.Latin1("<< /Type /FontDescriptor /FontName /ABCDEF+SourceSans3-Regular /Flags 32 " +
                                  "/FontFile3 5 0 R >>"),
            PdfFileBuilder.Stream("/Subtype /Type1C", CffOf(OpenTypePath)));

        Assert.That(font.IsEmbedded, Is.True);
        Assert.That(font.Name, Is.EqualTo("SourceSans3-Regular"));
        font.Typeface.GetGlyphByName("A", out var a);
        font.Typeface.GetGlyphByName("Aacute", out var aacute);
        Assert.That(font.GlyphOf(Code('A')), Is.EqualTo(a.Index));
        Assert.That(font.GlyphOf(Code('B')), Is.EqualTo(aacute.Index), "Differences put Aacute at 66");
        Assert.That(font.WidthOf(Code('A')), Is.EqualTo(0.6));
        Assert.That(font.WidthOf(Code('C')), Is.Zero, "outside FirstChar..LastChar: MissingWidth, 0 by default");
        Assert.That(font.TextOf(Code('B')), Is.EqualTo("Á"));
        Assert.That(font.TextOf(Code(0x80)), Is.EqualTo("€"), "WinAnsi 0x80");
    }

    [Test]
    public void EmbeddedTrueTypeFindsGlyphsThroughItsUnicodeCmap()
    {
        var program = File.ReadAllBytes(TrueTypePath);
        var expected = Typeface.LoadFont(TrueTypePath, 3).GetFont(0);
        var (_, font) = Open(
            "<< /Type /Font /Subtype /TrueType /BaseFont /SourceSans3-Bold /FirstChar 32 /LastChar 32 " +
            "/Widths [200] /Encoding /WinAnsiEncoding /FontDescriptor 4 0 R >>",
            PdfFileBuilder.Latin1("<< /Type /FontDescriptor /FontName /SourceSans3-Bold /Flags 32 /FontFile2 5 0 R >>"),
            PdfFileBuilder.Stream("", program));

        Assert.That(font.IsEmbedded, Is.True);
        Assert.That(font.GlyphOf(Code('A')), Is.EqualTo(expected.GetGlyphByUnicode('A').Index));
        Assert.That(font.GlyphOf(Code(0x80)), Is.EqualTo(expected.GetGlyphByUnicode(0x20AC).Index));
        Assert.That(font.GlyphOf(Code(0x80)), Is.Not.Zero);
    }

    private static (PdfDocument Document, PdfFont Font) Composite(string encoding, string cidFont)
    {
        var program = File.ReadAllBytes(TrueTypePath);
        return Open(
            $"<< /Type /Font /Subtype /Type0 /BaseFont /SourceSans3-Bold /Encoding /{encoding} " +
            "/DescendantFonts [4 0 R] >>",
            PdfFileBuilder.Latin1("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /SourceSans3-Bold " +
                                  "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                                  "/FontDescriptor 5 0 R " + cidFont + " >>"),
            PdfFileBuilder.Latin1("<< /Type /FontDescriptor /FontName /SourceSans3-Bold /Flags 4 /FontFile2 6 0 R >>"),
            PdfFileBuilder.Stream("", program));
    }

    [Test]
    public void IdentityHReadsTwoByteCodesAsCidsAndGlyphs()
    {
        var (_, font) = Composite("Identity-H", "/DW 900 /W [5 [500 600] 10 12 300]");
        byte[] text = [0x00, 0x05, 0x00, 0x0B];

        var first = font.ReadCharacter(text, 0);
        var second = font.ReadCharacter(text, first.Length);

        Assert.That(first, Is.EqualTo(new PdfCharacter(5, 2)));
        Assert.That(second, Is.EqualTo(new PdfCharacter(11, 2)));
        Assert.That(font.ReadCharacter(text, 4).Length, Is.Zero);
        Assert.That(font.GlyphOf(first), Is.EqualTo(5), "CIDToGIDMap Identity by default");
        Assert.That(font.WidthOf(first), Is.EqualTo(0.5));
        Assert.That(font.WidthOf(new PdfCharacter(6, 2)), Is.EqualTo(0.6));
        Assert.That(font.WidthOf(second), Is.EqualTo(0.3));
        Assert.That(font.WidthOf(new PdfCharacter(99, 2)), Is.EqualTo(0.9), "DW");
        Assert.That(font.IsVertical, Is.False);
    }

    [Test]
    public void IdentityVTakesVerticalMetrics()
    {
        var (_, font) = Composite("Identity-V", "/W2 [5 [-800 250 772]]");
        var type0 = (PdfType0Font)font;

        Assert.That(font.IsVertical, Is.True);
        Assert.That(font.WidthOf(new PdfCharacter(5, 2)), Is.EqualTo(0.8));
        Assert.That(type0.VerticalOriginOf(new PdfCharacter(5, 2)), Is.EqualTo((0.25, 0.772)));
        Assert.That(font.WidthOf(new PdfCharacter(6, 2)), Is.EqualTo(1.0), "DW2 [880 -1000] by default");
        Assert.That(type0.VerticalOriginOf(new PdfCharacter(6, 2)), Is.EqualTo((0.5, 0.88)));
    }

    [Test]
    public void VerticalDefaultsComeFromDw2()
    {
        var (_, font) = Composite("Identity-V", "/DW2 [900 -1100]");
        var type0 = (PdfType0Font)font;

        Assert.That(font.WidthOf(new PdfCharacter(6, 2)), Is.EqualTo(1.1).Within(1e-12));
        Assert.That(type0.VerticalOriginOf(new PdfCharacter(6, 2)).Y, Is.EqualTo(0.9).Within(1e-12));
    }

    [Test]
    public void OverlappingWidthRunsLetTheLaterWin()
    {
        var (_, font) = Composite("Identity-H", "/W [0 100 500 50 [600]]");

        Assert.That(font.WidthOf(new PdfCharacter(70, 2)), Is.EqualTo(0.5));
        Assert.That(font.WidthOf(new PdfCharacter(50, 2)), Is.EqualTo(0.6));
    }

    [Test]
    public void StandInForACompositeFontSurvivesBrokenToUnicodeText()
    {
        var toUnicode = PdfFileBuilder.Stream("", PdfFileBuilder.Latin1(
            "1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfrange <0001> <0003> <D7FF> endbfrange"));
        var (_, font) = Open("<< /Type /Font /Subtype /Type0 /BaseFont /Arial /Encoding /Identity-H " +
                             "/DescendantFonts [4 0 R] /ToUnicode 5 0 R >>",
            PdfFileBuilder.Latin1("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Arial >>"), toUnicode);

        for (uint code = 1; code <= 3; code++)
        {
            Assert.DoesNotThrow(() => font.GlyphOf(new PdfCharacter(code, 2)));
        }
    }

    [Test]
    public void EmbeddedOpenTypeDrawsFromItsCffOutlines()
    {
        var (_, font) = Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /SourceSans3-Regular /Encoding /WinAnsiEncoding " +
            "/FontDescriptor 4 0 R >>",
            PdfFileBuilder.Latin1("<< /Type /FontDescriptor /FontName /SourceSans3-Regular /Flags 32 /FontFile3 5 0 R >>"),
            PdfFileBuilder.Stream("/Subtype /OpenType", File.ReadAllBytes(OpenTypePath)));

        Assert.That(font.IsEmbedded, Is.True);
        font.Typeface.GetGlyphByName("A", out var a);
        Assert.That(font.GlyphOf(Code('A')), Is.EqualTo(a.Index));
    }

    [Test]
    public void BrokenOpenTypeIsNotEmbeddedInsteadOfThrowing()
    {
        byte[] garbage = [(byte)'O', (byte)'T', (byte)'T', (byte)'O', 0, 3, 0, 0, 0, 0, 0, 0, 1, 2, 3];
        var (_, font) = Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /F /FontDescriptor 4 0 R >>",
            PdfFileBuilder.Latin1("<< /Type /FontDescriptor /FontName /F /Flags 32 /FontFile3 5 0 R >>"),
            PdfFileBuilder.Stream("/Subtype /OpenType", garbage));

        Assert.That(font.IsEmbedded, Is.False);
    }

    [Test]
    public void LigatureAndVariantNamesFindTheirOwnGlyphs()
    {
        var trueType = Typeface.LoadFont(TrueTypePath, 3);
        string composed = null;
        foreach (var glyph in trueType.Glyphs)
        {
            if (glyph.Name != null && (glyph.Name.Contains('_') || glyph.Name.Contains('.')) &&
                PdfSimpleEncodings.TextOf(glyph.Name) != null)
            {
                composed = glyph.Name;
                break;
            }
        }

        Assert.That(composed, Is.Not.Null, "the test font names a ligature or a variant");
        trueType.GetGlyphByName(composed, out var expected);
        var (_, font) = Open(
            "<< /Type /Font /Subtype /TrueType /BaseFont /SourceSans3-Bold " +
            $"/Encoding << /Differences [65 /{composed}] >> /FontDescriptor 4 0 R >>",
            PdfFileBuilder.Latin1("<< /Type /FontDescriptor /FontName /SourceSans3-Bold /Flags 32 /FontFile2 5 0 R >>"),
            PdfFileBuilder.Stream("", File.ReadAllBytes(TrueTypePath)));

        Assert.That(font.GlyphOf(Code('A')), Is.EqualTo(expected.Index), composed + ", not its plain letter");
    }

    [Test]
    public void UnknownEncodingNameKeepsTheBuiltInOne()
    {
        var (_, font) = Open(
            "<< /Type /Font /Subtype /Type1 /BaseFont /SourceSans3-Regular /Encoding /NoSuchEncoding " +
            "/FontDescriptor 4 0 R >>",
            PdfFileBuilder.Latin1("<< /Type /FontDescriptor /FontName /SourceSans3-Regular /Flags 32 /FontFile3 5 0 R >>"),
            PdfFileBuilder.Stream("/Subtype /Type1C", CffOf(OpenTypePath)));

        font.Typeface.GetGlyphByName("A", out var a);
        Assert.That(font.GlyphOf(Code('A')), Is.EqualTo(a.Index), "the CFF's built-in encoding puts A at 65");
    }

    [Test]
    public void CodeWithoutGlyphIsTheMissingGlyph()
    {
        var (_, font) = Open(
            "<< /Type /Font /Subtype /TrueType /BaseFont /SourceSans3-Bold " +
            "/Encoding << /BaseEncoding /WinAnsiEncoding /Differences [65 /uniE000] >> /FontDescriptor 4 0 R >>",
            PdfFileBuilder.Latin1("<< /Type /FontDescriptor /FontName /SourceSans3-Bold /Flags 32 /FontFile2 5 0 R >>"),
            PdfFileBuilder.Stream("", File.ReadAllBytes(TrueTypePath)));

        Assert.That(font.GlyphOf(Code('A')), Is.Zero);
    }

    [Test]
    public void CidToGidMapStreamPicksTheGlyph()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        builder.Add("<< /Type /Font /Subtype /Type0 /BaseFont /F /Encoding /Identity-H /DescendantFonts [4 0 R] >>");
        builder.Add("<< /Type /Font /Subtype /CIDFontType2 /BaseFont /F /FontDescriptor 5 0 R /CIDToGIDMap 7 0 R >>");
        builder.Add("<< /Type /FontDescriptor /FontName /F /Flags 4 /FontFile2 6 0 R >>");
        builder.Add(PdfFileBuilder.Stream("", File.ReadAllBytes(TrueTypePath)));
        builder.Add(PdfFileBuilder.Stream("", [0, 0, 0, 0, 0, 42]));
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        var font = PdfFonts.Create(new PdfReference(3, 0), document);

        Assert.That(font.GlyphOf(new PdfCharacter(2, 2)), Is.EqualTo(42));
        Assert.That(font.GlyphOf(new PdfCharacter(3, 2)), Is.Zero, "past the map");
    }

    [Test]
    public void Type3DrawsWithContentStreamsInItsOwnGlyphSpace()
    {
        var (_, font) = Open(
            "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 500 500] /FontMatrix [0.002 0 0 0.002 0 0] " +
            "/CharProcs << /square 4 0 R >> /Encoding << /Type /Encoding /Differences [97 /square] >> " +
            "/FirstChar 97 /LastChar 97 /Widths [500] >>",
            PdfFileBuilder.Stream("", Encoding.ASCII.GetBytes("500 0 d0 0 0 500 500 re f")));
        var type3 = (PdfType3Font)font;

        Assert.That(font.WidthOf(Code('a')), Is.EqualTo(1.0).Within(1e-12), "500 x 0.002");
        Assert.That(type3.CharProcOf(Code('a')), Is.Not.Null);
        Assert.That(type3.CharProcOf(Code('b')), Is.Null);
        Assert.That(font.Typeface, Is.Null);
        Assert.That(font.GlyphOf(Code('a')), Is.Zero);
    }

    [Test]
    public void FontNotEmbeddedKeepsTheFilesWidthsAndSaysWhatStandsIn()
    {
        var (_, font) = Open("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /FirstChar 65 /LastChar 65 " +
                             "/Widths [722] /Encoding /WinAnsiEncoding >>");

        Assert.That(font.IsEmbedded, Is.False);
        Assert.That(font.WidthOf(Code('A')), Is.EqualTo(0.722));
        Assert.That(font.TextOf(Code(0x93)), Is.EqualTo("“"));
        if (font.Substitute != null)
        {
            Assert.That(font.Typeface, Is.Not.Null);
            Assert.That(font.GlyphOf(Code('A')), Is.Not.Zero, font.Substitute + " draws A");
        }
    }

    [Test]
    public void StandardFontWithoutWidthsTakesThemFromItsStandIn()
    {
        var (_, font) = Open("<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman >>");

        if (font.Substitute == null)
        {
            Assert.That(font.WidthOf(Code('A')), Is.Zero);
            return;
        }

        Assert.That(font.WidthOf(Code('A')), Is.InRange(0.5, 0.9), font.Substitute + "'s advance of A");
        Assert.That(font.TextOf(Code('A')), Is.EqualTo("A"), "StandardEncoding");
    }

    [Test]
    public void SymbolFontNotEmbeddedUsesItsBuiltInEncoding()
    {
        var (_, font) = Open("<< /Type /Font /Subtype /Type1 /BaseFont /Symbol >>");
        var simple = (PdfSimpleFont)font;

        Assert.That(simple.GlyphNameOf(0x61), Is.EqualTo("alpha"));
        Assert.That(simple.GlyphNameOf(0x41), Is.EqualTo("Alpha"));
    }

    [Test]
    public void ToUnicodeSaysWhatTheEncodingDoesNot()
    {
        var toUnicode = PdfFileBuilder.Stream("", PdfFileBuilder.Latin1(
            "1 begincodespacerange <00> <FF> endcodespacerange 1 beginbfchar <41> <0416> endbfchar"));
        var (_, font) = Open("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /ToUnicode 4 0 R >>", toUnicode);

        Assert.That(font.TextOf(Code('A')), Is.EqualTo("Ж"));
        Assert.That(font.TextOf(Code('B')), Is.EqualTo("B"), "the encoding where ToUnicode is silent");
    }

    [Test]
    public void AFontIsReadOncePerDocument()
    {
        var (document, font) = Open("<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>");

        Assert.That(PdfFonts.Create(new PdfReference(3, 0), document), Is.SameAs(font));
        Assert.That(PdfFonts.Create(new PdfName("Courier"), document), Is.Null, "not a font dictionary");
    }

    [Test]
    public void BrokenEmbeddedProgramFallsBackToAStandIn()
    {
        var (_, font) = Open(
            "<< /Type /Font /Subtype /TrueType /BaseFont /Arial /FontDescriptor 4 0 R >>",
            PdfFileBuilder.Latin1("<< /Type /FontDescriptor /FontName /Arial /Flags 32 /FontFile2 5 0 R >>"),
            PdfFileBuilder.Stream("", [0, 1, 0, 0, 0, 9, 9, 9]));

        Assert.That(font.IsEmbedded, Is.False);
        Assert.DoesNotThrow(() => font.GlyphOf(Code('A')));
    }

    [TestCase("ABCDEF+Arial,BoldItalic", "Arial")]
    [TestCase("Arial-BoldItalicMT", "Arial")]
    [TestCase("TimesNewRomanPSMT", "Times New Roman")]
    [TestCase("Helvetica-Oblique", "Helvetica")]
    [TestCase("ZapfDingbats", "Zapf Dingbats")]
    [TestCase("abcdef+Calibri", "abcdef+Calibri")]
    public void FamilyComesFromThePostScriptName(string name, string family)
    {
        Assert.That(PdfFontNames.FamilyOf(name), Is.EqualTo(family));
    }

    [TestCase("Arial,BoldItalic", 700, true)]
    [TestCase("Helvetica-Oblique", 400, true)]
    [TestCase("Times-Roman", 400, false)]
    [TestCase("MinionPro-Semibold", 600, false)]
    public void StyleComesFromTheNameAfterItsFamily(string name, int weight, bool italic)
    {
        Assert.That(PdfFontNames.WeightOf(name, null).Value, Is.EqualTo(weight));
        Assert.That(PdfFontNames.StyleOf(name, null) == Adamantium.Fonts.FontStyle.Italic, Is.EqualTo(italic));
    }

    [TestCase("Aacute", "Á")]
    [TestCase("uni0041", "A")]
    [TestCase("uni00410042", "AB")]
    [TestCase("u1F600", "😀")]
    [TestCase("f_i", "fi")]
    [TestCase("a.sc", "a")]
    [TestCase("uni00e9", null)]
    [TestCase("uniD800", null)]
    [TestCase("nonsense", null)]
    public void GlyphNamesStandForText(string name, string text)
    {
        Assert.That(PdfSimpleEncodings.TextOf(name), Is.EqualTo(text));
    }

    [Test]
    public void EncodingTablesFollowTheAnnex()
    {
        Assert.That(PdfSimpleEncodings.WinAnsi[0x80], Is.EqualTo("Euro"));
        Assert.That(PdfSimpleEncodings.WinAnsi[0xA0], Is.EqualTo("space"));
        Assert.That(PdfSimpleEncodings.WinAnsi[0xAD], Is.EqualTo("hyphen"));
        Assert.That(PdfSimpleEncodings.WinAnsi[0x7F], Is.EqualTo("bullet"), "unused codes above 32");
        Assert.That(PdfSimpleEncodings.WinAnsi[0x81], Is.EqualTo("bullet"));
        Assert.That(PdfSimpleEncodings.WinAnsi[0x20], Is.EqualTo("space"));
        Assert.That(PdfSimpleEncodings.MacRoman[0xCA], Is.EqualTo("space"));
        Assert.That(PdfSimpleEncodings.MacRoman[0xDB], Is.EqualTo("currency"));
        Assert.That(PdfSimpleEncodings.Symbol[0x41], Is.EqualTo("Alpha"));
        Assert.That(PdfSimpleEncodings.ZapfDingbats[0x21], Is.EqualTo("a1"));
        Assert.That(PdfSimpleEncodings.MacExpert[0x61], Is.EqualTo("Asmall"));
        Assert.That(PdfSimpleEncodings.Standard[0xA4], Is.EqualTo("fraction"));
    }
}
