using System.Linq;
using System.Text;
using Adamantium.Pdf.Annotations;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.ThreeD;
using Adamantium.Pdf.Writing;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>Comments on a view of 3D artwork (ISO 32000-1, 13.6.6): the 3D annotation, the view and the checksum a
/// markup's <c>ExData</c> names, read from a file and written after it - the view one object, so a markup read back is
/// shown in the very view of the artwork it names.</summary>
public class PdfMarkup3DTests
{
    private const string Markup =
        "/ExData << /Type /ExData /Subtype /Markup3D /3DA 4 0 R /3DV 6 0 R /MD5 <00112233445566778899AABBCCDDEEFF> >>";

    private static byte[] Build()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Annots [4 0 R 7 0 R 8 0 R 9 0 R] >>");
        builder.Add("<< /Type /Annot /Subtype /3D /NM (plate) /Rect [100 100 400 400] /3DD 5 0 R >>");
        builder.Add(PdfFileBuilder.Stream("/Type /3D /Subtype /U3D /VA [6 0 R]", Encoding.ASCII.GetBytes("U3D model")));
        builder.Add("<< /Type /3DView /XN (Comment view 1) /MS /M /C2W [1 0 0 0 0 -1 0 1 0 0 -300 0] /CO 300 >>");
        builder.Add("<< /Type /Annot /Subtype /Square /Rect [150 150 200 200] " + Markup + " >>");
        builder.Add("<< /Type /Annot /Subtype /Square /Rect [150 150 200 200] " +
                    "/ExData << /Subtype /Markup3D /3DA (plate) /3DV << /XN (Inline) >> >> >>");
        builder.Add("<< /Type /Annot /Subtype /Square /Rect [150 150 200 200] >>");
        return builder.Build("/Root 1 0 R");
    }

    [Test]
    public void AMarkup_NamesItsAnnotationItsViewAndTheChecksum()
    {
        var document = PdfDocument.Open(Build());
        var annotations = document.Pages[0].Annotations;
        var artwork = annotations[0].Artwork3D;
        var markup = annotations[1].Markup3D;
        var byName = annotations[2].Markup3D;

        Assert.Multiple(() =>
        {
            Assert.That(markup.Annotation, Is.EqualTo(new PdfObjectId(4, 0)));
            Assert.That(markup.View.Name, Is.EqualTo("Comment view 1"));
            Assert.That(markup.View.IsSameAs(artwork.Views[0]), Is.True, "one object, listed by the stream as well");
            Assert.That(markup.Checksum, Is.EqualTo(new byte[]
            {
                0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
            }));
            Assert.That(byName.Annotation, Is.Null);
            Assert.That(byName.AnnotationName, Is.EqualTo("plate"));
            Assert.That(byName.View.Id, Is.Null, "a view written in place is no object");
            Assert.That(byName.View.IsSameAs(artwork.Views[0]), Is.False);
            Assert.That(annotations[3].Markup3D, Is.Null);
        });
    }

    [Test]
    public void AMarkupWritten_ComesBackOnTheViewItWasMadeOn()
    {
        var document = PdfDocument.Open(Build());
        var page = document.Pages[0];
        var model = page.Annotations[0];
        var artwork = model.Artwork3D;
        var view = new Pdf3DView
        {
            Name = "Comment view 2",
            UsesCamera = true,
            Camera = new Pdf3DCamera(new Pdf3DVector(0, 1, 0), new Pdf3DVector(0, 0, 1), new Pdf3DVector(1, 0, 0),
                new Pdf3DVector(-250, 0, 0)),
            CenterOfOrbit = 250,
            Projection = new Pdf3DProjection { Kind = Pdf3DProjectionKind.Orthographic, Scale = 2, Binding = "W" },
            Background = new Pdf3DColor(0.25, 0.5, 0.75),
            RenderMode = new Pdf3DRenderMode { Name = "Wireframe" },
            Lighting = "CAD",
        };
        var update = new PdfIncrementalUpdate(document);
        var viewObject = update.Add(view.ToDictionary());
        var data = PdfMarkup3D.ToDictionary(new PdfReference(model.Id!.Value), viewObject, artwork.ComputeChecksum());
        foreach (var unused in Enumerable.Range(0, 2))
        {
            update.AddAnnotation(page, new PdfNewAnnotation(PdfAnnotationTypes.Square, new PdfRectangle(10, 10, 50, 50))
            {
                ExternalData = data,
            });
        }

        var reopened = PdfDocument.Open(update.Write());
        var written = reopened.Pages[0].Annotations.Skip(4).Select(annotation => annotation.Markup3D).ToList();
        var first = written[0].View;

        Assert.Multiple(() =>
        {
            Assert.That(written[0].Annotation, Is.EqualTo(model.Id));
            Assert.That(written[0].Checksum, Is.EqualTo(reopened.Pages[0].Annotations[0].Artwork3D.ComputeChecksum()));
            Assert.That(first.IsSameAs(written[1].View), Is.True, "two markups on one view object");
            Assert.That(first.Name, Is.EqualTo("Comment view 2"));
            Assert.That(first.UsesCamera, Is.True);
            Assert.That(first.Camera.ZAxis, Is.EqualTo(new Pdf3DVector(1, 0, 0)));
            Assert.That(first.Camera.Position, Is.EqualTo(new Pdf3DVector(-250, 0, 0)));
            Assert.That(first.CenterOfOrbit, Is.EqualTo(250));
            Assert.That(first.Projection.Kind, Is.EqualTo(Pdf3DProjectionKind.Orthographic));
            Assert.That(first.Projection.Scale, Is.EqualTo(2));
            Assert.That(first.Projection.Binding, Is.EqualTo("W"));
            Assert.That(first.Background, Is.EqualTo(new Pdf3DColor(0.25, 0.5, 0.75)));
            Assert.That(first.RenderMode.Name, Is.EqualTo("Wireframe"));
            Assert.That(first.Lighting, Is.EqualTo("CAD"));
        });
    }
}
