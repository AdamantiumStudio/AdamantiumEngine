using System;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Pdf;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Images;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;
using Adamantium.Pdf.Writing;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>New documents written page by page, each read back by opening what was written: pages, their content and
/// resources, pictures with alpha, gradients, soft masks and groups, a picture shared by pages written once.</summary>
public class PdfDocumentWriterTests
{
    private static readonly PdfRectangle A4 = new(0, 0, 595.276, 841.89);

    private static byte[] Write(Action<PdfDocumentWriter> fill)
    {
        using var output = new MemoryStream();
        var writer = new PdfDocumentWriter(output);
        fill(writer);
        writer.Finish();
        return output.ToArray();
    }

    private static string Content(PdfDocument document, PdfPage page) =>
        Encoding.ASCII.GetString(page.GetContents().Span);

    [Test]
    public void Pages_AreReadBack_InOrder_WithTheirBoxesAndContent()
    {
        var bytes = Write(writer =>
        {
            writer.AddPage(A4, new PdfContentBuilder().FillColor(1, 0, 0).Rectangle(10, 20, 30, 40).Fill());
            writer.AddPage(new PdfRectangle(0, 0, 612, 792),
                new PdfContentBuilder().StrokeColor(0, 0, 1).MoveTo(0, 0).LineTo(100, 100).Stroke());
        });

        var document = PdfDocument.Open(bytes);
        Assert.That(document.IsRepaired, Is.False, "the cross-reference table is read as written");
        Assert.That(document.PageCount, Is.EqualTo(2));
        Assert.That(document.Pages[0].MediaBox, Is.EqualTo(A4));
        Assert.That(document.Pages[1].MediaBox, Is.EqualTo(new PdfRectangle(0, 0, 612, 792)));
        Assert.That(Content(document, document.Pages[0]), Does.Contain("10 20 30 40 re").And.Contain("1 0 0 rg"));
        Assert.That(Content(document, document.Pages[1]), Does.Contain("100 100 l").And.Contain("S"));
    }

    [Test]
    public void Information_IsWritten_AndTheFileHasAnIdentifier()
    {
        var created = new DateTimeOffset(2026, 11, 24, 21, 0, 0, TimeSpan.FromHours(3));
        var bytes = Write(writer =>
        {
            writer.Title = "Отчёт (ноябрь)";
            writer.Author = "Тестер";
            writer.Created = created;
            writer.AddPage(A4, new PdfContentBuilder());
        });

        var document = PdfDocument.Open(bytes);
        Assert.That(document.Properties.Title, Is.EqualTo("Отчёт (ноябрь)"));
        Assert.That(document.Properties.Author, Is.EqualTo("Тестер"));
        Assert.That(document.Properties.Producer, Is.EqualTo("Adamantium"));
        Assert.That(document.Properties.Created, Is.EqualTo(created));
        Assert.That(document.Trailer[PdfKeys.Id], Is.TypeOf<PdfArray>());
    }

    [Test]
    public void APicture_WithAlpha_IsReadBack_PixelForPixel()
    {
        var rgba = new byte[]
        {
            255, 0, 0, 255, 0, 255, 0, 128,
            0, 0, 255, 0, 10, 20, 30, 255,
        };
        var picture = PdfImages.FromRgba(2, 2, rgba);
        var bytes = Write(writer => writer.AddPage(A4,
            new PdfContentBuilder().SaveState().Transform(100, 0, 0, 100, 50, 50).Draw(picture).RestoreState()));

        var document = PdfDocument.Open(bytes);
        var page = document.Pages[0];
        var resources = new PdfResources(page.Resources, document);
        Assert.That(Content(document, page), Does.Contain("/Im0 Do"));
        var image = resources.XObject("Im0");
        Assert.That(image.Dictionary[PdfKeys.SMask], Is.TypeOf<PdfReference>(), "the alpha is a stream of its own");
        var bitmap = PdfImageDecoder.Decode(image, resources, document);
        Assert.That(bitmap.Width, Is.EqualTo(2));
        Assert.That(bitmap.Height, Is.EqualTo(2));
        Assert.That(bitmap.Rgba, Is.EqualTo(rgba));
    }

    [Test]
    public void AnOpaquePicture_HasNoSoftMask()
    {
        var picture = PdfImages.FromRgba(1, 1, [1, 2, 3, 255]);
        Assert.That(picture.Dictionary.ContainsKey(PdfKeys.SMask), Is.False);
    }

    [Test]
    public void APictureOnEveryPage_IsWrittenOnce()
    {
        var rgba = Enumerable.Repeat((byte)200, 64 * 64 * 4).ToArray();
        var picture = PdfImages.FromRgba(64, 64, rgba);
        var bytes = Write(writer =>
        {
            for (var index = 0; index < 3; index++)
            {
                writer.AddPage(A4, new PdfContentBuilder().Draw(picture));
            }
        });

        var document = PdfDocument.Open(bytes);
        var references = document.Pages.Select(page =>
            ((PdfDictionary)page.Resources[PdfKeys.XObject])["Im0"]).Cast<PdfReference>().Select(r => r.Id).Distinct();
        Assert.That(references.Count(), Is.EqualTo(1));
    }

    [Test]
    public void Gradients_ThroughStops_AreStitched_AndEndsCarryOn()
    {
        var axial = PdfShadings.Axial(0, 0, 100, 0,
        [
            new PdfGradientStop(0.2, 1, 0, 0), new PdfGradientStop(0.5, 0, 1, 0), new PdfGradientStop(0.9, 0, 0, 1),
        ]);
        var function = (PdfDictionary)axial["Function"];
        Assert.That(((PdfInteger)function[PdfKeys.FunctionType]).Value, Is.EqualTo(3));
        Assert.That(((PdfArray)function[PdfKeys.Functions]).Count, Is.EqualTo(4), "0..0.2, 0.2..0.5, 0.5..0.9, 0.9..1");
        var bounds = ((PdfArray)function[PdfKeys.Bounds]).Cast<PdfReal>().Select(real => real.Value);
        Assert.That(bounds, Is.EqualTo(new[] { 0.2, 0.5, 0.9 }));

        var two = PdfShadings.Radial(50, 50, 0, 50, 50, 40, [new PdfGradientStop(0, 1, 1, 1), new PdfGradientStop(1, 0, 0, 0)]);
        Assert.That(((PdfInteger)((PdfDictionary)two["Function"])[PdfKeys.FunctionType]).Value, Is.EqualTo(2));

        var bytes = Write(writer => writer.AddPage(A4, new PdfContentBuilder()
            .SaveState().Rectangle(0, 0, 100, 100).Clip().Shade(axial).RestoreState()
            .SaveState().FillPattern(PdfShadings.Pattern(two, new PdfMatrix(1, 0, 0, 1, 0, 0)))
            .Rectangle(100, 0, 100, 100).Fill().RestoreState()));

        var document = PdfDocument.Open(bytes);
        var resources = new PdfResources(document.Pages[0].Resources, document);
        Assert.That(resources.Shading("Sh0"), Is.TypeOf<PdfDictionary>());
        Assert.That(resources.Pattern("P0"), Is.TypeOf<PdfDictionary>());
        Assert.That(Content(document, document.Pages[0]), Does.Contain("/Sh0 sh").And.Contain("/Pattern cs /P0 scn"));
    }

    [Test]
    public void ASoftMask_AndAGroup_AreWritten_WithTheirStreamsIndirect()
    {
        var mask = new PdfContentBuilder()
            .Shade(PdfShadings.Axial(0, 0, 100, 0, [PdfGradientStop.Gray(0, 1), PdfGradientStop.Gray(1, 0)], true))
            .ToFormXObject(new PdfRectangle(0, 0, 100, 100), PdfTransparencyGroup.GrayMask);
        var layer = new PdfContentBuilder().FillColor(0, 0, 1).Rectangle(0, 0, 100, 100).Fill()
            .Draw(PdfImages.FromRgba(1, 1, [1, 2, 3, 4]))
            .ToFormXObject(new PdfRectangle(0, 0, 100, 100), PdfTransparencyGroup.Layer);
        var bytes = Write(writer => writer.AddPage(A4, new PdfContentBuilder()
            .SaveState().SoftMask(mask, true).Opacity(0.5, 0.5).Draw(layer).RestoreState()));

        var document = PdfDocument.Open(bytes);
        var resources = new PdfResources(document.Pages[0].Resources, document);
        var state = resources.ExtGState("SM0");
        var softMask = (PdfDictionary)document.Resolve(state["SMask"]);
        Assert.That(((PdfName)softMask["S"]).Value, Is.EqualTo("Luminosity"));
        Assert.That(softMask["G"], Is.TypeOf<PdfReference>());
        var form = resources.XObject("Fm0");
        var group = (PdfDictionary)form.Dictionary["Group"];
        Assert.That(((PdfName)group["S"]).Value, Is.EqualTo("Transparency"));
        Assert.That(group["I"], Is.EqualTo(PdfBoolean.True));
        var inner = new PdfResources((PdfDictionary)document.Resolve(form.Dictionary[PdfKeys.Resources]), document);
        Assert.That(inner.XObject("Im0"), Is.Not.Null, "the group's own picture is written as an object of its own");
    }

    [Test]
    public void Triangles_AreAFreeFormShading()
    {
        var shading = PdfShadings.Triangles(
        [
            new PdfShadedVertex(0, 0, 1, 0, 0), new PdfShadedVertex(100, 0, 0, 1, 0), new PdfShadedVertex(0, 100, 0, 0, 1),
        ]);
        var bytes = Write(writer => writer.AddPage(A4, new PdfContentBuilder().Shade(shading)));
        var document = PdfDocument.Open(bytes);
        var written = (PdfStream)new PdfResources(document.Pages[0].Resources, document).Shading("Sh0");
        Assert.That(((PdfInteger)written.Dictionary["ShadingType"]).Value, Is.EqualTo(4));
        Assert.That(document.Decode(written).Data.Length, Is.EqualTo(3 * (1 + 4 + 4 + 6)));
    }

    // Two stops at one offset are a hard edge: it stays the way round it was drawn, whatever order the rest come in.
    [Test]
    public void AHardEdge_KeepsItsColorsTheWayRoundTheyWereGiven()
    {
        var axial = PdfShadings.Axial(0, 0, 1, 0,
        [
            new PdfGradientStop(0.5, 1, 0, 0), new PdfGradientStop(0.5, 0, 0, 1), new PdfGradientStop(0.2, 1, 1, 1),
        ]);

        var ramps = ((PdfArray)((PdfDictionary)axial["Function"])[PdfKeys.Functions]).Cast<PdfDictionary>().ToList();
        double First(PdfDictionary ramp, string key) => ((PdfReal)((PdfArray)ramp[key])[0]).Value;
        double Third(PdfDictionary ramp, string key) => ((PdfReal)((PdfArray)ramp[key])[2]).Value;
        Assert.Multiple(() =>
        {
            Assert.That(First(ramps[1], PdfKeys.C1), Is.EqualTo(1), "white runs into red");
            Assert.That(Third(ramps[1], PdfKeys.C1), Is.EqualTo(0));
            Assert.That(First(ramps[2], PdfKeys.C0), Is.EqualTo(1), "the edge goes from red...");
            Assert.That(Third(ramps[2], PdfKeys.C1), Is.EqualTo(1), "...to blue");
            Assert.That(Third(ramps[3], PdfKeys.C0), Is.EqualTo(1), "blue carries on");
        });
    }

    // What a stream read from a file refers to are that file's objects; written into a new file it would point at
    // whatever has those numbers there.
    [Test]
    public void AStreamReadFromAFile_IsRefused()
    {
        var first = PdfDocument.Open(Write(writer =>
            writer.AddPage(A4, new PdfContentBuilder().Draw(PdfImages.FromRgba(1, 1, [1, 2, 3, 4])))));
        var read = new PdfResources(first.Pages[0].Resources, first).XObject("Im0");

        using var output = new MemoryStream();
        var writer = new PdfDocumentWriter(output);
        Assert.Throws<PdfException>(() => writer.AddPage(A4, new PdfContentBuilder().Draw(read)));
    }

    [Test]
    public void ASoftMask_IsATransparencyGroup_AndOneReadByLuminosityNamesItsSpace()
    {
        var plain = new PdfContentBuilder().ToFormXObject(new PdfRectangle(0, 0, 1, 1));
        var spaceless = new PdfContentBuilder().ToFormXObject(new PdfRectangle(0, 0, 1, 1),
            new PdfTransparencyGroup(null));

        Assert.Throws<ArgumentException>(() => new PdfContentBuilder().SoftMask(plain, false));
        Assert.Throws<ArgumentException>(() => new PdfContentBuilder().SoftMask(spaceless, true));
        Assert.DoesNotThrow(() => new PdfContentBuilder().SoftMask(spaceless, false), "an alpha mask needs no space");
    }

    // A stream that holds itself has no end: refused with an error, not a stack overflow.
    [Test]
    public void AStreamHoldingItself_IsRefused()
    {
        var form = new PdfContentBuilder().ToFormXObject(new PdfRectangle(0, 0, 1, 1));
        var self = new PdfDictionary();
        self.Set("Fm0", form);
        ((PdfDictionary)form.Dictionary[PdfKeys.Resources]).Set(PdfKeys.XObject, self);

        using var output = new MemoryStream();
        Assert.Throws<PdfException>(() => new PdfDocumentWriter(output).AddPage(A4, new PdfContentBuilder().Draw(form)));
    }

    // A tiling pattern: its cell, its steps and the matrix to the page - colored by what it draws.
    [Test]
    public void ATilingPattern_IsReadBack_WithItsCellAndSteps()
    {
        var picture = PdfImages.FromRgba(1, 1, [1, 2, 3, 255]);
        var cell = new PdfContentBuilder().SaveState().Transform(10, 0, 0, 10, 0, 0).Draw(picture).RestoreState();
        var pattern = cell.ToTilingPattern(new PdfRectangle(0, 0, 10, 10), 10, 12, new PdfMatrix(1, 0, 0, 1, 5, 5));
        var bytes = Write(writer => writer.AddPage(A4,
            new PdfContentBuilder().FillPattern(pattern).Rectangle(0, 0, 100, 100).Fill()));

        var document = PdfDocument.Open(bytes);
        var written = (PdfStream)new PdfResources(document.Pages[0].Resources, document).Pattern("P0");
        double Number(PdfObject value) => value is PdfInteger integer ? integer.Value : ((PdfReal)value).Value;
        Assert.Multiple(() =>
        {
            Assert.That(((PdfInteger)written.Dictionary["PatternType"]).Value, Is.EqualTo(1));
            Assert.That(((PdfInteger)written.Dictionary["PaintType"]).Value, Is.EqualTo(1), "colored by itself");
            Assert.That(Number(written.Dictionary["XStep"]), Is.EqualTo(10));
            Assert.That(Number(written.Dictionary["YStep"]), Is.EqualTo(12));
            var inner = (PdfDictionary)document.Resolve(written.Dictionary[PdfKeys.Resources]);
            Assert.That(((PdfDictionary)inner[PdfKeys.XObject])["Im0"], Is.TypeOf<PdfReference>());
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            cell.ToTilingPattern(new PdfRectangle(0, 0, 10, 10), 0, 10, PdfMatrix.Identity));
    }

    [Test]
    public void TrianglesAtNoPlace_AreRefused()
    {
        Assert.Throws<ArgumentException>(() => PdfShadings.Triangles(
        [
            new PdfShadedVertex(0, 0, 1, 0, 0), new PdfShadedVertex(double.NaN, 0, 0, 1, 0),
            new PdfShadedVertex(0, 100, 0, 0, 1),
        ]));
    }

    [Test]
    public void APictureOfNoPixels_NamesTheSideThatHasNone()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => PdfImages.FromRgba(10, 0, new byte[40]));
        Assert.That(error.ParamName, Is.EqualTo("height"));
    }

    [Test]
    public void Finished_TakesNoMorePages()
    {
        using var output = new MemoryStream();
        var writer = new PdfDocumentWriter(output);
        writer.Finish();
        Assert.Throws<InvalidOperationException>(() => writer.AddPage(A4, new PdfContentBuilder()));
        Assert.That(PdfDocument.Open(output.ToArray()).PageCount, Is.Zero);
    }
}
