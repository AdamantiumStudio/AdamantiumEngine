using System.Text;
using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Parsing;
using Adamantium.Pdf.Resources;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfColorSpaceTests
{
    private static PdfDocument document;

    [OneTimeSetUp]
    public void Open()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        builder.Add("<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 1 1 0] /N 1 >>");
        builder.Add(PdfFileBuilder.Stream("/N 3", [1, 2, 3]));
        document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
    }

    private static PdfColorSpace Space(string written, string resources = "<< >>")
    {
        var value = new PdfObjectParser(new PdfLexer(Encoding.Latin1.GetBytes(written))).ReadObject();
        var dictionary = (PdfDictionary)new PdfObjectParser(new PdfLexer(Encoding.Latin1.GetBytes(resources)))
            .ReadObject();
        return PdfColorSpaces.Create(value, new PdfResources(dictionary, document), document);
    }

    private static double[] Rgb(PdfColorSpace space, params double[] components)
    {
        var rgb = new double[3];
        space.ToRgb(components, rgb);
        return rgb;
    }

    [Test]
    public void DeviceSpacesConvertByTheFormatsRules()
    {
        Assert.That(Rgb(Space("/DeviceGray"), 0.25), Is.EqualTo(new[] { 0.25, 0.25, 0.25 }));
        Assert.That(Rgb(Space("/DeviceCMYK"), 0, 1, 0.5, 0.2), Is.EqualTo(new[] { 0.8, 0, 0.3 }).Within(1e-12));
        var cmyk = Space("/DeviceCMYK");
        var initial = new double[4];
        cmyk.InitialColor(initial);
        Assert.That(initial, Is.EqualTo(new double[] { 0, 0, 0, 1 }));
    }

    [Test]
    public void LabWhiteAndBlackAreSrgbWhiteAndBlack()
    {
        var lab = Space("[/Lab << /WhitePoint [0.9505 1 1.089] >>]");
        Assert.That(Rgb(lab, 100, 0, 0), Is.EqualTo(new[] { 1.0, 1, 1 }).Within(2e-3));
        Assert.That(Rgb(lab, 0, 0, 0), Is.EqualTo(new[] { 0.0, 0, 0 }).Within(1e-9));
        var d50 = Space("[/Lab << /WhitePoint [0.9642 1 0.8249] >>]");
        Assert.That(Rgb(d50, 100, 0, 0), Is.EqualTo(new[] { 1.0, 1, 1 }).Within(2e-3));
    }

    [Test]
    public void CalRgbWithAnIdentityLikeSrgbMatrixKeepsPrimaries()
    {
        var space = Space("[/CalRGB << /WhitePoint [0.9505 1 1.089] /Matrix [0.4124 0.2126 0.0193 0.3576 0.7152 " +
                          "0.1192 0.1805 0.0722 0.9505] >>]");
        Assert.That(Rgb(space, 1, 0, 0), Is.EqualTo(new[] { 1.0, 0, 0 }).Within(5e-3));
        Assert.That(Rgb(space, 1, 1, 1), Is.EqualTo(new[] { 1.0, 1, 1 }).Within(5e-3));
    }

    [Test]
    public void IndexedLooksColorsUpInItsTable()
    {
        var space = Space("[/Indexed /DeviceRGB 1 <FF000000FF00>]");
        Assert.That(space.Components, Is.EqualTo(1));
        Assert.That(Rgb(space, 1), Is.EqualTo(new[] { 0.0, 1, 0 }));
        Assert.That(Rgb(space, 9), Is.EqualTo(new[] { 0.0, 1, 0 }));
        Assert.That(space.DefaultDecode(8), Is.EqualTo(new double[] { 0, 255 }));
    }

    [Test]
    public void SeparationGoesThroughItsTintTransform()
    {
        var space = Space("[/Separation /Spot /DeviceCMYK 3 0 R]");
        Assert.That(Rgb(space, 1), Is.EqualTo(new[] { 1.0, 0, 0 }).Within(1e-12));
        Assert.That(Rgb(Space("[/Separation /All /DeviceCMYK 3 0 R]"), 0.25),
            Is.EqualTo(new[] { 0.75, 0.75, 0.75 }));
        Assert.That(((PdfSeparationColorSpace)Space("[/Separation /None /DeviceGray 3 0 R]")).PaintsNothing, Is.True);
    }

    [Test]
    public void IccBasedFallsBackOnItsComponentCount()
    {
        var space = Space("[/ICCBased 4 0 R]");
        Assert.That(space.Components, Is.EqualTo(3));
        Assert.That(Rgb(space, 0.1, 0.2, 0.3), Is.EqualTo(new[] { 0.1, 0.2, 0.3 }));
    }

    [Test]
    public void NamedSpacesComeFromTheResourcesAndDefaultsReplaceDeviceSpaces()
    {
        var resources = "<< /ColorSpace << /CS0 [/Indexed /DeviceGray 0 <80>] /DefaultGray [/CalGray << " +
                        "/WhitePoint [0.9505 1 1.089] /Gamma 1 >>] >> >>";
        Assert.That(Space("/CS0", resources), Is.TypeOf<PdfIndexedColorSpace>());
        Assert.That(Space("/DeviceGray", resources), Is.TypeOf<PdfCalGrayColorSpace>());
        Assert.That(Space("/Missing", resources), Is.Null);
    }

    [Test]
    public void SpacesThatNameThemselvesStop()
    {
        var resources = "<< /ColorSpace << /A /B /B /A >> >>";
        Assert.That(Space("/A", resources), Is.Null);
    }
}
