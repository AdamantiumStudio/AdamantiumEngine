using Adamantium.Pdf.Document;
using Adamantium.Pdf.Fonts;
using Adamantium.Pdf.Objects;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfAdobeDataTests
{
    private static PdfDocument Empty()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        return PdfDocument.Open(builder.Build("/Root 1 0 R"));
    }

    private static PdfFont Japanese(string encoding) =>
        Japanese(encoding, "/CIDSystemInfo << /Registry (Adobe) /Ordering (Japan1) /Supplement 2 >>");

    private static PdfFont Japanese(string encoding, string systemInfo)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        builder.Add($"<< /Type /Font /Subtype /Type0 /BaseFont /MS-Mincho /Encoding /{encoding} " +
                    "/DescendantFonts [4 0 R] >>");
        builder.Add($"<< /Type /Font /Subtype /CIDFontType0 /BaseFont /MS-Mincho {systemInfo} >>");
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        return PdfFonts.Create(new PdfReference(3, 0), document);
    }

    [TestCase("alpha", "α")]
    [TestCase("afii10017", "А")]
    [TestCase("Euro", "€")]
    [TestCase("dalethatafpatah", "דֲ")]
    [TestCase("alpha_beta.sc", "αβ")]
    public void GlyphNamesOfAdobesListsHaveTheirText(string name, string text)
    {
        Assert.That(PdfSimpleEncodings.TextOf(name), Is.EqualTo(text));
    }

    [TestCase("Asmall")]
    [TestCase("dollaroldstyle")]
    public void PrivateUseValuesOfTheGlyphListAreNoText(string name)
    {
        Assert.That(PdfSimpleEncodings.TextOf(name), Is.Null);
    }

    [Test]
    public void DingbatNamesHaveTextOnlyInTheZapfDingbatsFont()
    {
        var times = Simple("<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman " +
                           "/Encoding << /Differences [65 /a1] >> >>");
        var dingbats = Simple("<< /Type /Font /Subtype /Type1 /BaseFont /ZapfDingbats >>");

        Assert.That(times.TextOf(new PdfCharacter(65, 1)), Is.Null);
        Assert.That(dingbats.TextOf(new PdfCharacter(0x21, 1)), Is.EqualTo("✁"));
    }

    private static PdfFont Simple(string font)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        builder.Add(font);
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        return PdfFonts.Create(new PdfReference(3, 0), document);
    }

    [Test]
    public void SymbolFontCodesHaveTheirText()
    {
        var font = Simple("<< /Type /Font /Subtype /Type1 /BaseFont /Symbol >>");

        Assert.That(font.TextOf(new PdfCharacter(0x61, 1)), Is.EqualTo("α"));
        Assert.That(font.TextOf(new PdfCharacter(0xAE, 1)), Is.EqualTo("→"));
    }

    [Test]
    public void PredefinedCMapReadsShiftJisCodes()
    {
        var map = PdfCMapReader.Read(new PdfName("90ms-RKSJ-H"), Empty());
        byte[] text = [0x41, 0x82, 0xA0, 0x05];

        var length = map.ReadCode(text, 0, out var letter);
        Assert.That((letter, length), Is.EqualTo((0x41u, 1)));
        length = map.ReadCode(text, 1, out var hiragana);
        Assert.That((hiragana, length), Is.EqualTo((0x82A0u, 2)));
        map.ReadCode(text, 3, out var control);

        Assert.That(map.Name, Is.EqualTo("90ms-RKSJ-H"));
        Assert.That(map.IsVertical, Is.False);
        Assert.That(map.CidOf(letter, 1), Is.EqualTo(231 + 0x41 - 0x20));
        Assert.That(map.CidOf(hiragana, 2), Is.EqualTo(843));
        Assert.That(map.CidOf(control, 1), Is.EqualTo(231), "notdefrange");
    }

    [Test]
    public void VerticalPredefinedCMapUsesItsHorizontalOne()
    {
        var map = PdfCMapReader.Read(new PdfName("90ms-RKSJ-V"), Empty());

        Assert.That(map.IsVertical, Is.True);
        Assert.That(map.CidOf(0x82A0, 2), Is.EqualTo(843), "from 90ms-RKSJ-H");
    }

    [Test]
    public void PredefinedCMapIsReadOncePerDocument()
    {
        var document = Empty();

        var first = PdfCMapReader.Read(new PdfName("UniJIS-UCS2-H"), document);
        var second = PdfCMapReader.Read(new PdfName("UniJIS-UCS2-H"), document);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.SameAs(first));
    }

    [TestCase("Identity-X")]
    [TestCase("../90ms-RKSJ-H")]
    [TestCase("90ms-RKSJ-H.")]
    public void UnknownCMapNameReadsNone(string name)
    {
        Assert.That(PdfCMapReader.Read(new PdfName(name), Empty()), Is.Null);
    }

    [Test]
    public void UnicodeCMapCodeIsItsText()
    {
        var font = Japanese("UniJIS-UCS2-H");
        var character = font.ReadCharacter([0x30, 0x42], 0);

        Assert.That(character, Is.EqualTo(new PdfCharacter(0x3042, 2)));
        Assert.That(font.TextOf(character), Is.EqualTo("あ"));
    }

    [Test]
    public void CidOfAnAdobeCollectionHasItsText()
    {
        var font = Japanese("90ms-RKSJ-H");
        byte[] text = [0x82, 0xA0, 0x41];

        var hiragana = font.ReadCharacter(text, 0);
        var letter = font.ReadCharacter(text, hiragana.Length);

        Assert.That(font.TextOf(hiragana), Is.EqualTo("あ"));
        Assert.That(font.TextOf(letter), Is.EqualTo("A"));
    }

    [TestCase("Japan1", 843u, "あ")]
    [TestCase("Japan1", 34u, "A")]
    [TestCase("Japan1", 264u, "A")]
    [TestCase("Japan1", 7887u, "、")]
    [TestCase("Japan1", 8061u, "\U0001F100")]
    [TestCase("GB1", 1u, " ")]
    [TestCase("GB1", 4162u, "一")]
    [TestCase("CNS1", 595u, "一")]
    [TestCase("Korea1", 6460u, "一")]
    [TestCase("Korea1", 1087u, "각")]
    public void EachCollectionGivesItsCidsText(string ordering, uint cid, string text)
    {
        Assert.That(PdfCidTexts.Of(ordering).TextOf(cid), Is.EqualTo(text));
    }

    [Test]
    public void CollectionTheEngineDoesNotCarryGivesNoText()
    {
        Assert.That(PdfCidTexts.Of("Identity").TextOf(1), Is.Null);
    }

    [Test]
    public void SurrogatePairCodeOfAUtf16CMapIsItsText()
    {
        var font = Japanese("UniJIS-UTF16-H");
        var character = font.ReadCharacter([0xD8, 0x3C, 0xDD, 0x00], 0);

        Assert.That(character, Is.EqualTo(new PdfCharacter(0xD83CDD00, 4)));
        Assert.That(font.TextOf(character), Is.EqualTo("\U0001F100"));
    }

    [Test]
    public void ControlCodeOfAUnicodeCMapTakesItsCidsText()
    {
        var font = Japanese("UniJIS-UTF16-H");
        var character = font.ReadCharacter([0x00, 0x09], 0);

        Assert.That(font.TextOf(character), Is.EqualTo(" "), "notdefrange: CID 1");
    }

    [Test]
    public void CollectionComesFromThePredefinedCMap()
    {
        var font = Japanese("90ms-RKSJ-H", "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) >>");

        Assert.That(font.TextOf(font.ReadCharacter([0x82, 0xA0], 0)), Is.EqualTo("あ"));
    }

    [Test]
    public void OtherRegistryHasNoCollection()
    {
        var font = Japanese("Identity-H", "/CIDSystemInfo << /Registry (Example) /Ordering (Japan1) >>");

        Assert.That(font.TextOf(new PdfCharacter(843, 2)), Is.Null);
    }

    [Test]
    public void NotDefCharsGiveTheirCid()
    {
        var map = PdfCMapReader.Parse("""
            1 begincodespacerange <00> <FF> endcodespacerange
            1 begincidchar <41> 7 endcidchar
            1 beginnotdefchar <42> 3 endnotdefchar
            """u8.ToArray());

        Assert.That(map.CidOf(0x41, 1), Is.EqualTo(7));
        Assert.That(map.CidOf(0x42, 1), Is.EqualTo(3));
        Assert.That(map.CidOf(0x43, 1), Is.Zero);
    }

    [Test]
    public void HugeCidRangeDoesNotWrapAround()
    {
        var map = PdfCMapReader.Parse("""
            1 begincodespacerange <00> <FF> endcodespacerange
            1 begincidrange <00> <FF> 4294967295 endcidrange
            """u8.ToArray());

        Assert.That(map.CidOf(0x00, 1), Is.EqualTo(uint.MaxValue));
        Assert.That(map.CidOf(0x01, 1), Is.Zero);
    }

    [Test]
    public void EmbeddedCMapOverAPredefinedOneLeavesTheSharedOneAlone()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        var child = builder.Add(PdfFileBuilder.Stream("/Type /CMap",
            PdfFileBuilder.Latin1("/90ms-RKSJ-H usecmap 1 begincidchar <41> 999 endcidchar")));
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        var predefined = PdfCMapReader.Read(new PdfName("90ms-RKSJ-H"), document);

        var embedded = PdfCMapReader.Read(new PdfReference(child, 0), document);

        Assert.That(embedded.CidOf(0x41, 1), Is.EqualTo(999));
        Assert.That(embedded.CidOf(0x82A0, 2), Is.EqualTo(843));
        Assert.That(embedded.Ordering, Is.EqualTo("Japan1"));
        Assert.That(predefined.CidOf(0x41, 1), Is.EqualTo(264));
        Assert.That(PdfCMapReader.Read(new PdfName("90ms-RKSJ-H"), document), Is.SameAs(predefined));
    }
}
