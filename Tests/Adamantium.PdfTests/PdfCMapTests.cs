using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Fonts;
using Adamantium.Pdf.Objects;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfCMapTests
{
    private static PdfCMap Parse(string text) => PdfCMapReader.Parse(Encoding.ASCII.GetBytes(text));

    private const string MixedCodes = """
        /CIDInit /ProcSet findresource begin 12 dict begin begincmap
        /CMapName /Test def
        2 begincodespacerange <00> <80> <8140> <9FFC> endcodespacerange
        2 begincidrange <00> <7F> 1 <8140> <817E> 633 endcidrange
        1 begincidchar <80> 99 endcidchar
        endcmap CMapName currentdict /CMap defineresource pop end end
        """;

    [Test]
    public void CodesTakeTheLengthOfTheirCodespaceRange()
    {
        var map = Parse(MixedCodes);
        byte[] text = [0x41, 0x81, 0x40, 0x80, 0xA0];

        var length = map.ReadCode(text, 0, out var first);
        Assert.That((first, length), Is.EqualTo((0x41u, 1)));
        length = map.ReadCode(text, 1, out var second);
        Assert.That((second, length), Is.EqualTo((0x8140u, 2)));
        length = map.ReadCode(text, 3, out var third);
        Assert.That((third, length), Is.EqualTo((0x80u, 1)));
        length = map.ReadCode(text, 4, out var outside);
        Assert.That((outside, length), Is.EqualTo((0xA0u, 1)), "no range: the shortest length");

        Assert.That(map.Name, Is.EqualTo("Test"));
        Assert.That(map.CidOf(0x41, 1), Is.EqualTo(66));
        Assert.That(map.CidOf(0x8141, 2), Is.EqualTo(634));
        Assert.That(map.CidOf(0x80, 1), Is.EqualTo(99));
        Assert.That(map.CidOf(0xA0, 1), Is.Zero);
        Assert.That(map.CidOf(0x41, 2), Is.Zero, "a code of another length is another code");
    }

    [Test]
    public void ToUnicodeMapsCharsRangesArraysAndNames()
    {
        var map = Parse("""
            1 begincodespacerange <0000> <FFFF> endcodespacerange
            3 beginbfchar <0003> <0020> <0024> <00410042> <0030> /Aacute endbfchar
            2 beginbfrange <0010> <0012> <0061> <0020> <0021> [<0058> <D83DDE00>] endbfrange
            """);

        Assert.That(map.TextOf(0x0003, 2), Is.EqualTo(" "));
        Assert.That(map.TextOf(0x0024, 2), Is.EqualTo("AB"));
        Assert.That(map.TextOf(0x0030, 2), Is.EqualTo("Á"));
        Assert.That(map.TextOf(0x0011, 2), Is.EqualTo("b"));
        Assert.That(map.TextOf(0x0012, 2), Is.EqualTo("c"));
        Assert.That(map.TextOf(0x0021, 2), Is.EqualTo("😀"));
        Assert.That(map.TextOf(0x0013, 2), Is.Null);
    }

    [Test]
    public void IdentityMapsEachTwoByteCodeToItself()
    {
        var map = PdfCMap.Identity(vertical: true);
        var length = map.ReadCode([0x12, 0x34], 0, out var code);

        Assert.That((code, length), Is.EqualTo((0x1234u, 2)));
        Assert.That(map.CidOf(code, length), Is.EqualTo(0x1234));
        Assert.That(map.IsVertical, Is.True);
    }

    [Test]
    public void EmbeddedCMapUsesTheOneItNames()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        var parent = builder.Add(PdfFileBuilder.Stream("/Type /CMap",
            PdfFileBuilder.Latin1("1 begincodespacerange <00> <FF> endcodespacerange " +
                                  "1 begincidrange <00> <FF> 1000 endcidrange")));
        var child = builder.Add(PdfFileBuilder.Stream($"/Type /CMap /UseCMap {parent} 0 R",
            PdfFileBuilder.Latin1("1 begincidchar <41> 7 endcidchar")));
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));

        var map = PdfCMapReader.Read(new PdfReference(child, 0), document);

        Assert.That(map.CidOf(0x41, 1), Is.EqualTo(7));
        Assert.That(map.CidOf(0x42, 1), Is.EqualTo(1066));
    }

    [Test]
    public void CMapUsingItselfStopsAtTheChainLimit()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        var number = builder.Next;
        builder.Add(PdfFileBuilder.Stream($"/Type /CMap /UseCMap {number} 0 R",
            PdfFileBuilder.Latin1("1 begincidchar <41> 7 endcidchar")));
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));

        var map = PdfCMapReader.Read(new PdfReference(number, 0), document);

        Assert.That(map.CidOf(0x41, 1), Is.EqualTo(7));
    }

    [Test]
    public void InvalidRangeSkipsItsWholeArray()
    {
        var map = Parse("""
            1 begincodespacerange <0000> <FFFF> endcodespacerange
            2 beginbfrange <0002> <0001> [<0041> <0042>] <0010> <0010> <0058> endbfrange
            """);

        Assert.That(map.TextOf(0x0010, 2), Is.EqualTo("X"), "the array's strings are not taken for a range");
        Assert.That(map.TextOf(0x0041, 2), Is.Null);
    }

    [Test]
    public void ChildCMapOverIdentityKeepsIdentityForTheRest()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        var child = builder.Add(PdfFileBuilder.Stream("/Type /CMap /UseCMap /Identity-H",
            PdfFileBuilder.Latin1("1 begincidchar <0041> 7 endcidchar")));
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));

        var map = PdfCMapReader.Read(new PdfReference(child, 0), document);

        Assert.That(map.CidOf(0x0041, 2), Is.EqualTo(7));
        Assert.That(map.CidOf(0x0102, 2), Is.EqualTo(0x0102));
    }

    [Test]
    public void ManyCharsInDescendingOrderSealInNearLinearTime()
    {
        var text = new StringBuilder("1 begincodespacerange <0000> <FFFF> endcodespacerange 40000 beginbfchar ");
        for (var code = 40000; code > 0; code--)
        {
            text.Append($"<{code:X4}> <0041> ");
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var map = Parse(text.Append("endbfchar").ToString());

        Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(3));
        Assert.That(map.TextOf(12345, 2), Is.EqualTo("A"));
    }

    [Test]
    public void ManyCodespaceRangesDoNotSlowReading()
    {
        var text = new StringBuilder("50000 begincodespacerange ");
        for (var i = 0; i < 50000; i++)
        {
            text.Append("<8000> <80FF> ");
        }

        var map = Parse(text.Append("endcodespacerange").ToString());
        var data = new byte[200_000];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var at = 0; at < data.Length;)
        {
            at += map.ReadCode(data, at, out _);
        }

        Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(2));
    }

    [TestCase("1 beginbfrange <0000> <FFFF> [<0041>] endbfrange")]
    [TestCase("1 beginbfrange <FFFF> <0000> <0041> endbfrange")]
    [TestCase("2 begincidrange <00> <0000> 5 <0102030405> <0102030406> 1 endcidrange")]
    [TestCase("begincodespacerange <00> endcodespacerange begincidchar <41> abc endcidchar")]
    [TestCase("beginbfchar <41> [<42> endbfchar")]
    [TestCase("1 beginbfrange <41> <42> <> endbfrange")]
    public void MalformedEntriesAreSkipped(string text)
    {
        Assert.DoesNotThrow(() =>
        {
            var map = Parse(text);
            map.ReadCode([0x41, 0x42, 0x43], 0, out var code);
            _ = map.TextOf(code, 1);
            _ = map.TextOf(0x0001, 2);
            _ = map.CidOf(code, 1);
        });
    }
}
