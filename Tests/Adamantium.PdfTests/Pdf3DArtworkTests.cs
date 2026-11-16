using System.Linq;
using System.Text;
using Adamantium.Pdf.Annotations;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.ThreeD;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>3D annotations read as their artwork: the model's stream, its own or another annotation's, its format and
/// bytes, its views - camera, projection, background, drawing, light - which view it opens on, when it comes to life.
/// </summary>
public class Pdf3DArtworkTests
{
    private const string Views =
        "/VA [<< /XN (Iso) /IN (iso) /MS /M /C2W [1 0 0 0 0 -1 0 1 0 0 -300 0] /CO 300 " +
        "/P << /Subtype /P /FOV 30 /PS /Min >> /BG << /Type /3DBG /CS /DeviceRGB /C [0.9 0.92 0.95] >> " +
        "/RM << /Type /3DRenderMode /Subtype /SolidWireframe /AC [/DeviceRGB 1 0 0] /O 0.25 /CV 30 >> " +
        "/LS << /Type /3DLightingScheme /Subtype /CAD >> >> " +
        "<< /XN (Top) /MS /M /C2W [1 0 0 0 1 0 0 0 -1 0 0 200] /CO 200 /P << /Subtype /O /OS 4 /OB /Max >> >> " +
        "<< /XN (From the model) /MS /U3D /U3DPath (CameraNode) >>]";

    private static PdfDocument OpenDocument(string firstAnnotationExtra, string secondAnnotation = null)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        var annotations = secondAnnotation == null ? "[4 0 R]" : "[4 0 R 6 0 R]";
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Annots " + annotations + " >>");
        builder.Add("<< /Type /Annot /Subtype /3D /Rect [100 100 400 400] /3DD 5 0 R " + firstAnnotationExtra + " >>");
        var model = Encoding.ASCII.GetBytes("U3D model bytes");
        builder.Add(PdfFileBuilder.Stream("/Type /3D /Subtype /U3D /DV 1 /Filter /FlateDecode " + Views,
            PdfFileBuilder.Flate(model)));
        if (secondAnnotation != null)
        {
            builder.Add(secondAnnotation);
        }

        return PdfDocument.Open(builder.Build("/Root 1 0 R"));
    }

    private static Pdf3DArtwork GetArtwork(PdfDocument document, int index = 0) =>
        document.Pages[0].Annotations[index].Artwork3D;

    [Test]
    public void AThreeDAnnotation_GivesItsModelItsFormatAndItsBytes()
    {
        var document = OpenDocument("");
        var annotation = document.Pages[0].Annotations.Single();
        var artwork = annotation.Artwork3D;

        Assert.Multiple(() =>
        {
            Assert.That(annotation.Subtype, Is.EqualTo(PdfAnnotationTypes.ThreeD));
            Assert.That(artwork.Format, Is.EqualTo("U3D"));
            Assert.That(artwork.IsShared, Is.False);
            Assert.That(Encoding.ASCII.GetString(artwork.ReadData().Span), Is.EqualTo("U3D model bytes"),
                "decoded from its filter");
            Assert.That(artwork.IsInteractive, Is.True, "by default");
            Assert.That(artwork.ViewBox, Is.Null, "the whole annotation by default");
        });
    }

    [Test]
    public void ItsViews_CarryTheirCameraProjectionBackgroundDrawingAndLight()
    {
        var views = GetArtwork(OpenDocument("")).Views;
        var iso = views[0];
        var top = views[1];
        var node = views[2];
        string[] names = ["Iso", "Top", "From the model"];

        Assert.Multiple(() =>
        {
            Assert.That(views.Select(view => view.Name), Is.EqualTo(names));
            Assert.That(iso.InternalName, Is.EqualTo("iso"));
            Assert.That(iso.UsesCamera, Is.True);
            Assert.That(iso.Camera.XAxis, Is.EqualTo(new Pdf3DVector(1, 0, 0)));
            Assert.That(iso.Camera.YAxis, Is.EqualTo(new Pdf3DVector(0, 0, -1)));
            Assert.That(iso.Camera.ZAxis, Is.EqualTo(new Pdf3DVector(0, 1, 0)));
            Assert.That(iso.Camera.Position, Is.EqualTo(new Pdf3DVector(0, -300, 0)));
            Assert.That(iso.CenterOfOrbit, Is.EqualTo(300));
            Assert.That(iso.Projection.Kind, Is.EqualTo(Pdf3DProjectionKind.Perspective));
            Assert.That(iso.Projection.FieldOfView, Is.EqualTo(30));
            Assert.That(iso.Projection.Binding, Is.EqualTo("Min"));
            Assert.That(iso.Background, Is.EqualTo(new Pdf3DColor(0.9, 0.92, 0.95)));
            Assert.That(iso.RenderMode.Name, Is.EqualTo("SolidWireframe"));
            Assert.That(iso.RenderMode.AuxiliaryColor, Is.EqualTo(new Pdf3DColor(1, 0, 0)));
            Assert.That(iso.RenderMode.FaceColor, Is.Null, "the background's");
            Assert.That(iso.RenderMode.Opacity, Is.EqualTo(0.25));
            Assert.That(iso.RenderMode.CreaseAngle, Is.EqualTo(30));
            Assert.That(iso.Lighting, Is.EqualTo("CAD"));

            Assert.That(top.Projection.Kind, Is.EqualTo(Pdf3DProjectionKind.Orthographic));
            Assert.That(top.Projection.Scale, Is.EqualTo(4));
            Assert.That(top.Projection.Binding, Is.EqualTo("Max"));
            Assert.That(top.Background, Is.EqualTo(new Pdf3DColor(1, 1, 1)), "white by default");
            Assert.That(top.RenderMode, Is.Null, "the model's own");
            Assert.That(top.Lighting, Is.Null, "the model's own lights");

            Assert.That(node.UsesCamera, Is.False, "the model's own view node");
            Assert.That(node.Camera, Is.Null);
            Assert.That(node.ViewNode, Is.EqualTo("CameraNode"));
        });
    }

    /// <summary>The annotation's 3DV names the view it opens on - by index, by name, as first or last, as the stream's
    /// default, or written out; without it, the stream's DV; without either, the first.</summary>
    [TestCase("", "Top")]
    [TestCase("/3DV 0", "Iso")]
    [TestCase("/3DV /F", "Iso")]
    [TestCase("/3DV /L", "From the model")]
    [TestCase("/3DV /D", "Top")]
    [TestCase("/3DV (From the model)", "From the model")]
    [TestCase("/3DV (iso)", "Iso")]
    [TestCase("/3DV << /XN (Written out) /MS /M /C2W [1 0 0 0 1 0 0 0 1 0 0 0] >>", "Written out")]
    [TestCase("/3DV 9", "Top")]
    public void TheViewItOpensOn_IsTheOneItsEntriesName(string entry, string expected)
    {
        Assert.That(GetArtwork(OpenDocument(entry)).DefaultView.Name, Is.EqualTo(expected));
    }

    /// <summary>A projection that names no binding: an orthographic one is not fitted at all - one unit of the artwork a
    /// unit of the annotation (table 305, Absolute) - a perspective's field of view spans the width.</summary>
    [TestCase("/Subtype /O /OS 4", "Absolute")]
    [TestCase("/Subtype /P /FOV 30", "W")]
    public void AProjectionNamingNoBinding_TakesTheFormatsDefault(string projection, string expected)
    {
        var document = OpenDocument($"/3DV << /XN (V) /MS /M /C2W [1 0 0 0 1 0 0 0 1 0 0 0] /P << {projection} >> >>");

        Assert.That(GetArtwork(document).DefaultView.Projection.Binding, Is.EqualTo(expected));
    }

    [Test]
    public void WhenItComesToLife_AndWhereItIsShown_AreItsOwn()
    {
        var artwork = GetArtwork(OpenDocument("/3DA << /A /PO /D /PI /TB false /NP true >> /3DB [10 20 290 280] /3DI false"));
        var defaults = GetArtwork(OpenDocument("")).Activation;

        Assert.Multiple(() =>
        {
            Assert.That(artwork.Activation.Activates, Is.EqualTo(Pdf3DActivationTime.PageOpened));
            Assert.That(artwork.Activation.Deactivates, Is.EqualTo(Pdf3DDeactivationTime.PageInvisible));
            Assert.That(artwork.Activation.ShowsToolbar, Is.False);
            Assert.That(artwork.Activation.ShowsNavigation, Is.True);
            Assert.That(artwork.ViewBox?.Left, Is.EqualTo(10));
            Assert.That(artwork.ViewBox?.Top, Is.EqualTo(280));
            Assert.That(artwork.IsInteractive, Is.False);
            Assert.That(defaults.Activates, Is.EqualTo(Pdf3DActivationTime.Explicitly), "when asked, by default");
            Assert.That(defaults.Deactivates, Is.EqualTo(Pdf3DDeactivationTime.PageClosed));
            Assert.That(defaults.ShowsToolbar, Is.True);
            Assert.That(defaults.ShowsNavigation, Is.False);
        });
    }

    /// <summary>One model, two annotations: the second names the first's stream through a 3DRef and opens on a view of
    /// its own.</summary>
    [Test]
    public void AnotherAnnotation_SharesTheModelThroughA3DRef()
    {
        var document = OpenDocument("", "<< /Type /Annot /Subtype /3D /Rect [100 450 400 750] " +
                                        "/3DD << /Type /3DRef /3DD 5 0 R >> /3DV (Iso) >>");
        var first = GetArtwork(document);
        var second = GetArtwork(document, 1);

        Assert.Multiple(() =>
        {
            Assert.That(second.IsShared, Is.True);
            Assert.That(ReferenceEquals(second.Stream, first.Stream), Is.True, "the same stream");
            Assert.That(second.Format, Is.EqualTo("U3D"));
            Assert.That(second.DefaultView.Name, Is.EqualTo("Iso"));
            Assert.That(first.DefaultView.Name, Is.EqualTo("Top"));
        });
    }

    [Test]
    public void AViewItOpensOnByReference_IsTheViewItLists()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Annots [4 0 R] >>");
        builder.Add("<< /Type /Annot /Subtype /3D /Rect [0 0 100 100] /3DD 5 0 R /3DV 7 0 R >>");
        builder.Add(PdfFileBuilder.Stream("/Type /3D /Subtype /U3D /VA [6 0 R 7 0 R]", Encoding.ASCII.GetBytes("x")));
        builder.Add("<< /XN (Front) >>");
        builder.Add("<< /XN (Side) >>");
        var artwork = PdfDocument.Open(builder.Build("/Root 1 0 R")).Pages[0].Annotations[0].Artwork3D;

        Assert.That(artwork.DefaultView, Is.SameAs(artwork.Views[1]));
    }

    [Test]
    public void AnotherKind_OrOneWithNoModel_HasNoArtwork()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Annots [4 0 R 5 0 R] >>");
        builder.Add("<< /Type /Annot /Subtype /Square /Rect [0 0 10 10] /3DD 6 0 R >>");
        builder.Add("<< /Type /Annot /Subtype /3D /Rect [0 0 10 10] >>");
        builder.Add(PdfFileBuilder.Stream("/Type /3D /Subtype /U3D", Encoding.ASCII.GetBytes("x")));
        var annotations = PdfDocument.Open(builder.Build("/Root 1 0 R")).Pages[0].Annotations;

        Assert.Multiple(() =>
        {
            Assert.That(annotations[0].Artwork3D, Is.Null, "a square is no 3D annotation, whatever it carries");
            Assert.That(annotations[1].Artwork3D, Is.Null, "no 3DD");
        });
    }
}
