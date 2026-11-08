using System;
using System.Diagnostics;
using System.Text;
using Adamantium.Pdf;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Filters;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>Files made to hang, crash or exhaust a reader: each must open or fail quickly, in linear time.</summary>
public class PdfHostileFileTests
{
    private static long Milliseconds(Action action)
    {
        var watch = Stopwatch.StartNew();
        action();
        return watch.ElapsedMilliseconds;
    }

    private static void AssertLinear(Func<int, Action> run, int size)
    {
        run(size / 4)();
        var small = Milliseconds(run(size));
        var large = Milliseconds(run(size * 2));
        Assert.That(large, Is.LessThan(small * 4 + 300), "x2 input took " + small + " ms, then " + large + " ms");
    }

    [Test]
    public void AChainOfIndirectLengthsDoesNotOverflowTheStack()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        const int depth = 20_000;
        for (var i = 0; i < depth; i++)
        {
            builder.Add("<< /Length " + (builder.Next + 1) + " 0 R >>\nstream\nxx\nendstream");
        }

        builder.Add("2");
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        Assert.That(document.GetObject(3), Is.Not.Null);
    }

    [Test]
    public void ObjectStreamsWhoseParametersLiveInOtherObjectStreamsDoNotOverflowTheStack()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        const int depth = 5_000;
        for (var i = 0; i < depth; i++)
        {
            builder.Add(PdfFileBuilder.Stream("/Type /ObjStm /N " + (builder.Next + 1) + " 0 R /First 0",
                Encoding.ASCII.GetBytes("")));
        }

        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        for (var number = 3; number < depth + 3; number++)
        {
            document.GetObject(number);
        }

        Assert.Pass();
    }

    [Test]
    public void RepairWithManyUnclosedTrailersStaysLinear()
    {
        AssertLinear(count =>
        {
            var file = new StringBuilder("%PDF-1.4\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");
            for (var i = 0; i < count; i++)
            {
                file.Append("trailer(");
            }

            var bytes = Encoding.Latin1.GetBytes(file.ToString());
            return () => Assert.That(PdfDocument.Open(bytes).IsRepaired, Is.True);
        }, 20_000);
    }

    [Test]
    public void StreamsWithoutEndstreamReadInDescendingOrderStayLinear()
    {
        AssertLinear(count =>
        {
            var builder = new PdfFileBuilder();
            builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
            builder.Add("<< /Type /Pages /Kids [] >>");
            for (var i = 0; i < count; i++)
            {
                builder.Add("<< /Length 99999999 >>\nstream\n" + new string('x', 40));
            }

            var file = builder.Build("/Root 1 0 R");
            var text = Encoding.Latin1.GetString(file).Replace("endobj", "endob_");
            var bytes = Encoding.Latin1.GetBytes(text);
            return () =>
            {
                var document = PdfDocument.Open(bytes);
                for (var number = count + 2; number >= 3; number--)
                {
                    document.GetObject(number);
                }
            };
        }, 5_000);
    }

    [Test]
    public void ManyObjectsWithUnclosedStringsStayLinear()
    {
        AssertLinear(count =>
        {
            var builder = new PdfFileBuilder();
            builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
            builder.Add("<< /Type /Pages /Kids [] >>");
            for (var i = 0; i < count; i++)
            {
                builder.Add("<< /Title (never closed >>");
            }

            var bytes = builder.Build("/Root 1 0 R");
            return () =>
            {
                var document = PdfDocument.Open(bytes);
                for (var number = 3; number < count + 3; number++)
                {
                    document.GetObject(number);
                }
            };
        }, 10_000);
    }

    [Test]
    public void ABrokenObjectHidesNothingFromRepair()
    {
        var file = "%PDF-1.4\n1 0 obj << /Title (broken >> endobj\n" +
                   "2 0 obj << /Type /Catalog /Pages 3 0 R >> endobj\n" +
                   "3 0 obj << /Type /Pages /Kids [4 0 R] >> endobj\n" +
                   "4 0 obj << /Type /Page /Parent 3 0 R >> endobj\n";
        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(file));
        Assert.That(document.IsRepaired, Is.True);
        Assert.That(document.PageCount, Is.EqualTo(1));
    }

    [Test]
    public void AStreamWithoutEndstreamHidesNothingFromRepair()
    {
        var file = "%PDF-1.4\n1 0 obj << /Length 999 >>\nstream\nbinary\nendobj\n" +
                   "2 0 obj << /Type /Catalog /Pages 4 0 R >> endobj\n" +
                   "3 0 obj << /Length 4 >>\nstream\nabcd\nendstream\nendobj\n" +
                   "4 0 obj << /Type /Pages /Kids [5 0 R] >> endobj\n" +
                   "5 0 obj << /Type /Page /Parent 4 0 R /Contents 3 0 R >> endobj\n";
        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(file));
        Assert.That(document.PageCount, Is.EqualTo(1));
        Assert.That(Encoding.ASCII.GetString(document.Pages[0].GetContents().Span), Is.EqualTo("abcd"));
    }

    [Test]
    public void ContentPastTheDecodeLimitIsReportedNotDropped()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>");
        builder.Add(PdfFileBuilder.Stream("/Filter /FlateDecode", PdfFileBuilder.Flate(new byte[5_000_000])));
        var options = new PdfReadOptions { DecodeLimits = new PdfDecodeLimits { MaxDecodedLength = 1_000_000 } };
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"), options);
        Assert.Throws<PdfDecodeLimitException>(() => document.Pages[0].GetContents());
    }
}
