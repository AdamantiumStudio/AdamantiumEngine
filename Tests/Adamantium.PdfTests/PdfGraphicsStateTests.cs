using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfGraphicsStateTests
{
    private static (PdfDocument Document, int First) Document(params byte[][] objects)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        var first = builder.Next;
        foreach (var body in objects)
        {
            builder.Add(body);
        }

        return (PdfDocument.Open(builder.Build("/Root 1 0 R")), first);
    }

    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text);

    private static PdfGraphicsStateParameters Read(PdfDocument document, int number) =>
        PdfGraphicsStateParameters.From(document.GetObject(number) as PdfDictionary,
            new PdfResources(new PdfDictionary(), document), document);

    [Test]
    public void TheParametersTheDictionarySetsAreRead_AndTheOthersLeftAsTheyAre()
    {
        var (document, first) = Document(Text(
            "<< /Type /ExtGState /LW 2.5 /LC 1 /LJ 2 /ML 7 /D [[3 1] 2] /CA 0.5 /ca 0.25 /BM /Multiply /AIS true >>"));
        var parameters = Read(document, first);
        Assert.That(parameters.LineWidth, Is.EqualTo(2.5));
        Assert.That(parameters.LineCap, Is.EqualTo(1));
        Assert.That(parameters.LineJoin, Is.EqualTo(2));
        Assert.That(parameters.MiterLimit, Is.EqualTo(7));
        Assert.That(parameters.Dash?.Lengths, Is.EqualTo(new[] { 3.0, 1 }));
        Assert.That(parameters.Dash?.Phase, Is.EqualTo(2));
        Assert.That(parameters.StrokeAlpha, Is.EqualTo(0.5));
        Assert.That(parameters.FillAlpha, Is.EqualTo(0.25));
        Assert.That(parameters.BlendMode, Is.EqualTo(PdfBlendMode.Multiply));
        Assert.That(parameters.AlphaIsShape, Is.True);
        Assert.That(parameters.TextKnockout, Is.Null);
        Assert.That(parameters.Font, Is.Null);
        Assert.That(parameters.SetsSoftMask, Is.False);
    }

    [Test]
    public void OutOfRangeValuesAreBroughtIntoRange()
    {
        var (document, first) = Document(Text("<< /LW -3 /LC 9 /ML 0 /CA 4 /ca -1 >>"));
        var parameters = Read(document, first);
        Assert.That(parameters.LineWidth, Is.EqualTo(0));
        Assert.That(parameters.LineCap, Is.EqualTo(2));
        Assert.That(parameters.MiterLimit, Is.EqualTo(1));
        Assert.That(parameters.StrokeAlpha, Is.EqualTo(1));
        Assert.That(parameters.FillAlpha, Is.EqualTo(0));
    }

    [TestCase("/Screen", PdfBlendMode.Screen)]
    [TestCase("/Compatible", PdfBlendMode.Normal)]
    [TestCase("[/Unheard /HardLight /Multiply]", PdfBlendMode.HardLight)]
    [TestCase("[/Unheard]", PdfBlendMode.Normal)]
    [TestCase("/Unheard", PdfBlendMode.Normal)]
    public void TheFirstKnownBlendModeIsTaken(string written, PdfBlendMode expected)
    {
        var (document, first) = Document(Text("<< /BM " + written + " >>"));
        Assert.That(Read(document, first).BlendMode, Is.EqualTo(expected));
    }

    [Test]
    public void ASoftMaskIsReadWithItsGroupBackdropAndTransfer()
    {
        var (document, first) = Document(
            Text("<< /SMask << /S /Luminosity /G 4 0 R /BC [0.5] /TR 5 0 R >> >>"),
            PdfFileBuilder.Stream("/Type /XObject /Subtype /Form /BBox [0 0 10 10] " +
                                  "/Group << /S /Transparency /CS /DeviceGray /I true >>", Text("0 g 0 0 10 10 re f")),
            Text("<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>"));
        var parameters = Read(document, first);
        Assert.That(parameters.SetsSoftMask, Is.True);
        var mask = parameters.SoftMask;
        Assert.That(mask.Kind, Is.EqualTo(PdfSoftMaskKind.Luminosity));
        Assert.That(mask.Group, Is.SameAs(document.GetObject(first + 1)));
        Assert.That(mask.Backdrop, Is.EqualTo(new[] { 0.5 }));
        Assert.That(mask.Transfer, Is.Not.Null);
        Assert.That(mask.Transparency.Isolated, Is.True);
        Assert.That(mask.Transparency.ColorSpace.Components, Is.EqualTo(1));
    }

    [TestCase("/SMask /None")]
    [TestCase("/SMask << /S /Unheard /G 4 0 R >>")]
    [TestCase("/SMask << /S /Alpha >>")]
    public void NoneOrABrokenSoftMaskTakesTheMaskAway(string entry)
    {
        var (document, first) = Document(Text("<< " + entry + " >>"),
            PdfFileBuilder.Stream("/Type /XObject /Subtype /Form /BBox [0 0 1 1]", Text("")));
        var parameters = Read(document, first);
        Assert.That(parameters.SetsSoftMask, Is.True);
        Assert.That(parameters.SoftMask, Is.Null);
    }

    [TestCase("[3 -1]")]
    [TestCase("[3 /x]")]
    [TestCase("7")]
    public void ABrokenDashArrayIsSolid(string lengths)
    {
        var (document, first) = Document(Text("<< /D [" + lengths + " 0] >>"));
        Assert.That(Read(document, first).Dash?.IsSolid, Is.True);
    }

    [Test]
    public void AllZeroDashesAreSolid()
    {
        Assert.That(new PdfDashPattern([0, 0], 0).IsSolid, Is.True);
        Assert.That(new PdfDashPattern([0, 2], 0).IsSolid, Is.False);
    }

    [Test]
    public void AFormsTransparencyGroupIsRead_AndAnotherKindOfGroupIsNone()
    {
        var (document, first) = Document(
            PdfFileBuilder.Stream("/Subtype /Form /BBox [0 0 1 1] /Group << /S /Transparency /K true >>", Text("")),
            PdfFileBuilder.Stream("/Subtype /Form /BBox [0 0 1 1] /Group << /S /Other >>", Text("")));
        var resources = new PdfResources(new PdfDictionary(), document);
        var group = PdfTransparencyGroup.Of(document.GetObject(first) as PdfStream, resources, document);
        Assert.That(group.Knockout, Is.True);
        Assert.That(group.Isolated, Is.False);
        Assert.That(group.ColorSpace, Is.Null);
        Assert.That(PdfTransparencyGroup.Of(document.GetObject(first + 1) as PdfStream, resources, document), Is.Null);
    }
}
