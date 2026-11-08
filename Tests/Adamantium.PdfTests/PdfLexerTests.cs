using System.Text;
using Adamantium.Pdf;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Parsing;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfLexerTests
{
    private static PdfObject Parse(string text) =>
        new PdfObjectParser(new PdfLexer(Encoding.Latin1.GetBytes(text))).ReadObject();

    [TestCase("123", 123L)]
    [TestCase("-17", -17L)]
    [TestCase("+7", 7L)]
    [TestCase("--5", -5L)]
    public void IntegersReadAsIntegers(string text, long expected)
    {
        Assert.That(Parse(text), Is.TypeOf<PdfInteger>());
        Assert.That(((PdfInteger)Parse(text)).Value, Is.EqualTo(expected));
    }

    [TestCase("34.5", 34.5)]
    [TestCase("-.002", -0.002)]
    [TestCase(".5", 0.5)]
    [TestCase("4.", 4.0)]
    [TestCase("-3.62", -3.62)]
    [TestCase("0.1234567890123", 0.1234567890123)]
    public void RealsReadExactly(string text, double expected)
    {
        Assert.That(Parse(text), Is.TypeOf<PdfReal>());
        Assert.That(((PdfReal)Parse(text)).Value, Is.EqualTo(expected).Within(1e-15));
    }

    [Test]
    public void NamesUndoTheirEscapes()
    {
        Assert.That(((PdfName)Parse("/Lime#20Green")).Value, Is.EqualTo("Lime Green"));
        Assert.That(((PdfName)Parse("/A#42")).Value, Is.EqualTo("AB"));
        Assert.That(((PdfName)Parse("/paired#28#29parentheses")).Value, Is.EqualTo("paired()parentheses"));
    }

    [Test]
    public void LiteralStringsKeepBalancedParenthesesAndEscapes()
    {
        var text = (PdfString)Parse(@"(Strings (with (nested)) parentheses \) and \\ and \101\60 end)");
        Assert.That(Encoding.Latin1.GetString(text.Bytes),
            Is.EqualTo(@"Strings (with (nested)) parentheses ) and \ and A0 end"));
    }

    [Test]
    public void LiteralStringsJoinBackslashedLinesAndNormalizeLineEnds()
    {
        var text = (PdfString)Parse("(one \\\r\ntwo\r\nthree\rfour)");
        Assert.That(Encoding.Latin1.GetString(text.Bytes), Is.EqualTo("one two\nthree\nfour"));
    }

    [Test]
    public void HexStringsPadAnOddDigitWithZero()
    {
        var text = (PdfString)Parse("<901FA>");
        Assert.That(text.Bytes, Is.EqualTo(new byte[] { 0x90, 0x1F, 0xA0 }));
        Assert.That(text.IsHex, Is.True);
    }

    [Test]
    public void ReferencesAreTwoIntegersAndR()
    {
        var array = (PdfArray)Parse("[1 0 R 2 3 4 5 0 R 6]");
        Assert.That(array.Count, Is.EqualTo(6));
        Assert.That(((PdfReference)array[0]).Id, Is.EqualTo(new PdfObjectId(1, 0)));
        Assert.That(((PdfInteger)array[1]).Value, Is.EqualTo(2));
        Assert.That(((PdfInteger)array[2]).Value, Is.EqualTo(3));
        Assert.That(((PdfInteger)array[3]).Value, Is.EqualTo(4));
        Assert.That(((PdfReference)array[4]).Id, Is.EqualTo(new PdfObjectId(5, 0)));
        Assert.That(((PdfInteger)array[5]).Value, Is.EqualTo(6));
    }

    [Test]
    public void DictionariesNestAndDropNullValues()
    {
        var dictionary = (PdfDictionary)Parse("<< /A << /B [1 (two) /Three] >> /Gone null % comment\n /C true >>");
        Assert.That(dictionary.ContainsKey("Gone"), Is.False);
        Assert.That(dictionary["C"], Is.SameAs(PdfBoolean.True));
        var inner = (PdfDictionary)dictionary["A"];
        var array = (PdfArray)inner["B"];
        Assert.That(((PdfName)array[2]).Value, Is.EqualTo("Three"));
    }

    [Test]
    public void AnUnclosedDictionaryEndsAtTheNextStructureKeyword()
    {
        var dictionary = (PdfDictionary)Parse("<< /A 1 /B [2 3 endobj");
        Assert.That(((PdfInteger)dictionary["A"]).Value, Is.EqualTo(1));
        Assert.That(((PdfArray)dictionary["B"]).Count, Is.EqualTo(2));
    }

    [Test]
    public void HostileNestingIsRefusedWithoutOverflowingTheStack()
    {
        var text = new string('[', 200_000);
        Assert.Throws<PdfException>(() => Parse(text));
    }

    [Test]
    public void TextStringsReadUtf16AndPdfDocEncoding()
    {
        Assert.That(PdfTextEncoding.Decode([0xFE, 0xFF, 0x04, 0x1F, 0x04, 0x40]), Is.EqualTo(new string([(char)0x041F, (char)0x0440])));
        Assert.That(PdfTextEncoding.Decode([0xEF, 0xBB, 0xBF, 0xD0, 0x9F]), Is.EqualTo(((char)0x041F).ToString()));
        Assert.That(PdfTextEncoding.Decode([0x80, 0x41, 0xA0, 0x18]), Is.EqualTo("•A€˘"));
    }
}
