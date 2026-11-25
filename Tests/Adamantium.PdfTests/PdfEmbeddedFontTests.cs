using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Pdf;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Text;
using Adamantium.Pdf.Writing;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>Fonts a new document carries, read back by opening what was written: the text drawn in them, the widths,
/// and each font once however many pages it is on.</summary>
public class PdfEmbeddedFontTests
{
    private const string Words = "Hamburgefonstiv & 2025";
    private static readonly PdfRectangle Page = new(0, 0, 595, 842);

    [TestCase("Fonts/SourceSans3-Regular.otf")]
    [TestCase("Fonts/SourceSans3-Bold.ttf")]
    public void TextIsReadBack_AsItWasDrawn(string path)
    {
        var font = Typeface.LoadFont(path, 3).GetFont(0);
        using var output = new MemoryStream();
        var writer = new PdfDocumentWriter(output);
        for (var page = 0; page < 2; page++)
        {
            var content = new PdfContentBuilder();
            Draw(writer.Font(font), content, Words, 24, 72, 700);
            writer.AddPage(Page, content);
        }

        writer.Finish();
        Keep(path, output.ToArray());

        var document = PdfDocument.Open(output.ToArray());
        Assert.That(document.IsRepaired, Is.False);
        for (var page = 0; page < 2; page++)
        {
            var text = document.Pages[page].ReadText();
            Assert.That(text.Text.Trim(), Is.EqualTo(Words));
            var first = text.Glyphs[0];
            Assert.That(first.Origin.X, Is.EqualTo(72).Within(0.01));
            Assert.That(first.End.X - first.Origin.X, Is.EqualTo(font.GetAdvanceWidth(font.GetGlyphByUnicode('H').Index) * 24.0 /
                                                            font.UnitsPerEm).Within(0.01), "its width");
        }

        var written = System.Text.Encoding.Latin1.GetString(output.ToArray());
        Assert.That(written.Split("/CIDFontType0C").Length - 1, Is.EqualTo(1), "one font program for both pages");
    }

    [Test]
    public void AGlyphsOwnText_IsWhatItIsCopiedAs()
    {
        var font = Typeface.LoadFont("Fonts/SourceSans3-Regular.otf", 3).GetFont(0);
        using var output = new MemoryStream();
        var writer = new PdfDocumentWriter(output);
        var content = new PdfContentBuilder();
        var embedded = writer.Font(font);
        var glyph = font.GetGlyphByUnicode('A').Index;
        var silent = font.GetGlyphByUnicode('B').Index;
        content.BeginText(embedded).Glyph(glyph, 20, 0, 0, 20, 50, 50, "ffi \U0001F600")
            .Glyph(silent, 20, 0, 0, 20, 62, 50, string.Empty).Glyph(glyph, 20, 0, 0, 20, 74, 50, "A")
            .Glyph(silent, 20, 0, 0, 20, 86, 50, "B").EndText();
        writer.AddPage(Page, content);
        writer.Finish();
        Keep("texts", output.ToArray());

        Assert.That(PdfDocument.Open(output.ToArray()).Pages[0].ReadText().Text.Trim(), Is.EqualTo("ffi \U0001F600AB"),
            "each use as it was said, whichever came first");
    }

    // The file with its license's flags ('OS/2' fsType, eight bytes in) set.
    private static IFont Licensed(ushort flags)
    {
        var data = File.ReadAllBytes("Fonts/SourceSans3-Regular.otf");
        for (var at = 12; ; at += 16)
        {
            if (data.AsSpan(at, 4).SequenceEqual("OS/2"u8))
            {
                var table = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 8));
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(table + 8), flags);
                return Typeface.LoadFont(data, 3).GetFont(0);
            }
        }
    }

    [Test]
    public void AFontNobodyMayCarry_IsRefused()
    {
        var font = Licensed(0x0002);

        Assert.That(PdfEmbeddedFont.CanEmbed(font), Is.False);
        Assert.That(() => new PdfDocumentWriter(new MemoryStream()).Font(font), Throws.ArgumentException);
    }

    [Test]
    public void AFontNotToBeSubset_IsCarriedWhole()
    {
        var font = Licensed(0x0100);
        using var output = new MemoryStream();
        var writer = new PdfDocumentWriter(output);
        var content = new PdfContentBuilder();
        Draw(writer.Font(font), content, "A", 20, 50, 50);
        writer.AddPage(Page, content);
        writer.Finish();

        var written = System.Text.Encoding.Latin1.GetString(output.ToArray());
        Assert.That(written, Does.Match("/BaseFont /SourceSans3-Regular-\\d+-Identity-H"), "no tag: it is the whole font");
        Assert.That(output.Length, Is.GreaterThan(50_000));
        Assert.That(PdfDocument.Open(output.ToArray()).Pages[0].ReadText().Text.Trim(), Is.EqualTo("A"));
    }

    // Glyphs no character maps to - ligatures, alternates - are drawn as any other.
    [Test]
    public void AGlyphNoCharacterMapsTo_IsCarried()
    {
        var font = Typeface.LoadFont("Fonts/SourceSans3-Regular.otf", 3).GetFont(0);
        var mapped = font.Unicodes.Select(code => font.GetGlyphByUnicode(code).Index).ToHashSet();
        var glyph = (uint)Enumerable.Range(1, (int)font.Typeface.GlyphCount - 1)
            .Last(index => !mapped.Contains((uint)index) && font.GetGlyphByIndex((uint)index).HasOutlines);
        using var output = new MemoryStream();
        var writer = new PdfDocumentWriter(output);
        var content = new PdfContentBuilder();
        content.BeginText(writer.Font(font)).Glyph(glyph, 20, 0, 0, 20, 50, 50, "x").EndText();
        writer.AddPage(Page, content);
        writer.Finish();

        var read = PdfDocument.Open(output.ToArray()).Pages[0].ReadText().Glyphs[0];
        Assert.That(read.End.X - read.Origin.X, Is.EqualTo(font.GetAdvanceWidth(glyph) * 20.0 / font.UnitsPerEm).Within(0.01));
    }

    [Test]
    public void WhatCannotBeDrawn_IsRefused()
    {
        var font = Typeface.LoadFont("Fonts/SourceSans3-Regular.otf", 3).GetFont(0);
        var writer = new PdfDocumentWriter(new MemoryStream());
        var embedded = writer.Font(font);
        var content = new PdfContentBuilder().BeginText(embedded);

        Assert.That(embedded.CanDraw(font.Typeface.GlyphCount), Is.False);
        Assert.That(() => content.Glyph(font.Typeface.GlyphCount, 1, 0, 0, 1, 0, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
        Assert.That(() => new PdfContentBuilder().Glyph(1, 1, 0, 0, 1, 0, 0), Throws.InvalidOperationException);
        Assert.That(() => content.BeginText(embedded), Throws.InvalidOperationException);
        Assert.That(() => content.ToContent(), Throws.InvalidOperationException);
        Assert.That(() => new PdfContentBuilder().EndText(), Throws.InvalidOperationException);

        var form = content.EndText().ToFormXObject(Page);
        Assert.That(() => new PdfDocumentWriter(new MemoryStream()).AddPage(Page, content), Throws.InstanceOf<PdfException>(),
            "a font of another document");
        Assert.That(() => new PdfDocumentWriter(new MemoryStream()).AddPage(Page, new PdfContentBuilder().Draw(form)),
            Throws.InstanceOf<PdfException>(), "a font of another document, in a form");
    }

    [Test]
    public void AFontOnlyAskedFor_IsNotCarried()
    {
        using var output = new MemoryStream();
        var writer = new PdfDocumentWriter(output);
        Assert.That(writer.Font(Typeface.LoadFont("Fonts/SourceSans3-Regular.otf", 3).GetFont(0)).CanDraw(1), Is.True);
        writer.AddPage(Page, new PdfContentBuilder());
        writer.Finish();

        Assert.That(System.Text.Encoding.Latin1.GetString(output.ToArray()), Does.Not.Contain("/Font"));
    }

    private static void Draw(PdfEmbeddedFont embedded, PdfContentBuilder content, string words, double size, double x, double y)
    {
        var font = embedded.Font;
        content.FillColor(0, 0, 0).BeginText(embedded);
        foreach (var letter in words)
        {
            var glyph = font.GetGlyphByUnicode(letter).Index;
            content.Glyph(glyph, size, 0, 0, size, x, y, letter.ToString());
            x += font.GetAdvanceWidth(glyph) * size / font.UnitsPerEm;
        }

        content.EndText();
    }

    // For an independent reader to look at, where a folder is named for it.
    private static void Keep(string path, byte[] data)
    {
        if (Environment.GetEnvironmentVariable("ADAM_KEEP_PDF") is { Length: > 0 } folder)
        {
            File.WriteAllBytes(Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + ".pdf"), data);
        }
    }
}
