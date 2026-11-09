using System;
using System.Linq;
using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Navigation;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>Outlines, destinations, actions, links, attachments, the document's properties and annotation
/// appearances.</summary>
public class PdfNavigationTests
{
    private static PdfDocument Document(string catalog, string page2 = "", string info = "", params string[] more)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R " + catalog + " >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] " + page2 + " >>");
        builder.Add("<< " + info + " >>");
        foreach (var body in more)
        {
            builder.Add(body);
        }

        return PdfDocument.Open(builder.Build("/Root 1 0 R /Info 5 0 R"));
    }

    [Test]
    public void TheOutlineIsATreeOfTitlesAndWhereTheyGo()
    {
        var document = Document("/Outlines 6 0 R /Dests << /chapter [4 0 R /FitH 500] >> /Names << /Dests 13 0 R >> " +
                                "/URI << /Base (https://example.com/docs/) >>", "", "",
            "<< /Type /Outlines /First 7 0 R /Last 9 0 R /Count 3 >>",
            "<< /Title (One) /Parent 6 0 R /Next 8 0 R /Dest [3 0 R /XYZ 72 720 1.5] /First 10 0 R /Count 1 /F 2 /C [1 0 0] >>",
            "<< /Title (Two) /Parent 6 0 R /Next 9 0 R /Dest /chapter >>",
            "<< /Title (Three) /Parent 6 0 R /Next 7 0 R /A << /S /URI /URI (page.html) >> /F 1 >>",
            "<< /Title (One.1) /Parent 7 0 R /Dest (named) /Count -1 >>",
            "<< /Title (unused) >>",
            "<< /Title (unused) >>",
            "<< /Kids [14 0 R] >>",
            "<< /Limits [(a) (z)] /Names [(named) << /D [4 0 R /FitR 1 2 3 4] >>] >>");

        var outline = document.Outline;

        Assert.That(outline.Select(item => item.Title), Is.EqualTo(new[] { "One", "Two", "Three" }), "the loop back stops");
        var one = outline[0];
        Assert.That((one.IsOpen, one.IsBold, one.IsItalic), Is.EqualTo((true, true, false)));
        Assert.That(one.Color, Is.EqualTo(new double[] { 1, 0, 0 }));
        var xyz = one.Action.Destination;
        Assert.That((xyz.PageIndex, xyz.Kind, xyz.Left, xyz.Top, xyz.Zoom),
            Is.EqualTo((0, PdfDestinationKind.Xyz, (double?)72, (double?)720, (double?)1.5)));
        Assert.That((outline[1].Action.Destination.PageIndex, outline[1].Action.Destination.Kind,
            outline[1].Action.Destination.Top), Is.EqualTo((1, PdfDestinationKind.FitWidth, (double?)500)));
        Assert.That(outline[2].Action.Kind, Is.EqualTo(PdfActionKind.Uri));
        Assert.That(outline[2].Action.Uri, Is.EqualTo("https://example.com/docs/page.html"));
        Assert.That(outline[2].IsItalic, Is.True);
        var child = one.Children.Single();
        Assert.That(child.IsOpen, Is.False);
        var fitR = child.Action.Destination;
        Assert.That((fitR.PageIndex, fitR.Kind, fitR.Left, fitR.Bottom, fitR.Right, fitR.Top),
            Is.EqualTo((1, PdfDestinationKind.FitRectangle, (double?)1, (double?)2, (double?)3, (double?)4)));
    }

    [Test]
    public void LinksFollowDestinationsAndActionsWithinTheirArea()
    {
        var document = Document("/Dests << /end [4 0 R /Fit] >>",
            "/Annots [<< /Type /Annot /Subtype /Link /Rect [10 10 110 30] /Dest /end >> " +
            "<< /Type /Annot /Subtype /Link /Rect [0 100 300 140] /QuadPoints [200 140 300 140 200 120 300 120 0 120 100 120 0 100 100 100] " +
            "/A << /S /GoToR /F (other.pdf) /D [2 /Fit] >> >> " +
            "<< /Type /Annot /Subtype /Link /Rect [0 200 50 220] /A << /S /Named /N /NextPage >> >> " +
            "<< /Type /Annot /Subtype /Link /Rect [0 300 50 320] /F 2 /A << /S /URI /URI (https://hidden) >> >> " +
            "<< /Type /Annot /Subtype /Text /Rect [0 0 1 1] >>]");

        var links = document.Pages[1].Links;

        Assert.That(links, Has.Count.EqualTo(3));
        Assert.That((links[0].Action.Kind, links[0].Action.Destination.PageIndex, links[0].Action.Destination.Kind),
            Is.EqualTo((PdfActionKind.GoTo, 1, PdfDestinationKind.Fit)));
        Assert.That(links[0].Contains(new PdfPoint(50, 20)), Is.True);
        Assert.That((links[1].Action.Kind, links[1].Action.File, links[1].Action.Destination.PageIndex),
            Is.EqualTo((PdfActionKind.GoToRemote, "other.pdf", 2)));
        Assert.That(links[1].Contains(new PdfPoint(250, 130)), Is.True);
        Assert.That(links[1].Contains(new PdfPoint(150, 130)), Is.False, "inside the rectangle, outside its quads");
        Assert.That((links[2].Action.Kind, links[2].Action.Name), Is.EqualTo((PdfActionKind.Named, "NextPage")));
    }

    [Test]
    public void ALinksQuadrilateralsHitInEitherOrderAndAFlatOneHitsNothing()
    {
        var document = Document("",
            "/Annots [<< /Type /Annot /Subtype /Link /Rect [0 0 100 20] /QuadPoints [0 0 100 0 100 20 0 20] /Dest [3 0 R /Fit] >> " +
            "<< /Type /Annot /Subtype /Link /Rect [0 50 100 70] /QuadPoints [5 60 5 60 5 60 5 60] /Dest [3 0 R /Fit] >>]");

        var links = document.Pages[1].Links;

        Assert.That(links[0].Contains(new PdfPoint(50, 10)), Is.True, "ISO's counterclockwise order");
        Assert.That(links[1].Contains(new PdfPoint(50, 60)), Is.False);
        Assert.That(new Adamantium.Pdf.Text.PdfQuad(new PdfPoint(5, 5), new PdfPoint(5, 5), new PdfPoint(5, 5),
            new PdfPoint(5, 5)).Contains(new PdfPoint(500, 500)), Is.False);
    }

    [Test]
    public void ALocalDestinationsPageNumberIsOneOfTheDocumentsOrNone()
    {
        var document = Document("/OpenAction 6 0 R", "", "", "[999 /Fit]", "[1 /Fit]");

        Assert.That(document.ResolveDestination(new Adamantium.Pdf.Objects.PdfReference(6, 0)).PageIndex, Is.EqualTo(-1));
        Assert.That(document.ResolveDestination(new Adamantium.Pdf.Objects.PdfReference(7, 0)).PageIndex, Is.EqualTo(1));
    }

    [Test]
    public void ARootRelativeAddressTakesTheBase()
    {
        var document = Document("/URI << /Base (https://example.com/docs/) >>",
            "/Annots [<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /A << /S /URI /URI (/top.html) >> >>]");

        Assert.That(document.Pages[1].Links.Single().Action.Uri, Is.EqualTo("https://example.com/top.html"));
    }

    [Test]
    public void DatesStopAtTheirFirstMissingPart()
    {
        Assert.That(PdfDate.Parse("2024-01-01"), Is.EqualTo(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.That(PdfDate.Parse("2019-05-14T10:00Z"), Is.EqualTo(new DateTimeOffset(2019, 5, 14, 10, 0, 0, TimeSpan.Zero)));
        Assert.That(PdfDate.Parse("D:2024-07"), Is.EqualTo(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.That(PdfDate.Parse("D:00010101000000+05'00"), Is.Not.Null);
        Assert.That(PdfDate.Parse("D:99991231235959-05'00"), Is.Not.Null);
    }

    [Test, Timeout(20000)]
    public void ANameTreeWhoseNodesShareTheirKidsIsWalkedOnce()
    {
        var kids = string.Join(" ", Enumerable.Range(0, 20000).Select(_ => "<< /Kids 7 0 R >>"));
        var document = Document("/Names << /Dests 6 0 R >>", "", "",
            "<< /Kids 7 0 R >>", "[" + kids + " << /Names [(here) [3 0 R /Fit]] >>]");

        Assert.That(document.ResolveDestination(new Adamantium.Pdf.Objects.PdfString(Encoding.ASCII.GetBytes("here")))
            .PageIndex, Is.EqualTo(0));
    }

    [Test]
    public void AttachmentsAreListedWithWhatTheFileSaysOfThem()
    {
        var data = Encoding.ASCII.GetBytes("hello, attachment");
        var document = Document("/Names << /EmbeddedFiles 6 0 R >>",
            "/Annots [<< /Type /Annot /Subtype /FileAttachment /Rect [0 0 10 10] /FS 9 0 R >>]", "",
            "<< /Kids [7 0 R] >>",
            "<< /Names [(notes) 8 0 R (broken) << /Type /Filespec >>] >>",
            "<< /Type /Filespec /F (notes.txt) /UF <FEFF04370430043C04350442043A0438002E007400780074> " +
            "/Desc (Field notes) /EF << /F 10 0 R >> >>",
            "<< /Type /Filespec /F (pinned.bin) /EF << /F 11 0 R >> >>",
            Encoding.Latin1.GetString(PdfFileBuilder.Stream("/Type /EmbeddedFile /Subtype /text#2Fplain /Filter /FlateDecode " +
                "/Params << /Size 17 /CreationDate (D:20250102030405Z) /ModDate (D:20260304050607+02'00) >>",
                PdfFileBuilder.Flate(data))),
            Encoding.Latin1.GetString(PdfFileBuilder.Stream("/Type /EmbeddedFile", [1, 2, 3])));

        var attachment = document.Attachments.Single();

        Assert.That((attachment.Key, attachment.FileName, attachment.Description, attachment.MimeType, attachment.Size),
            Is.EqualTo(("notes", "заметки.txt", "Field notes", "text/plain", (long?)17)));
        Assert.That(attachment.Created, Is.EqualTo(new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero)));
        Assert.That(attachment.Modified, Is.EqualTo(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2))));
        Assert.That(attachment.GetData().ToArray(), Is.EqualTo(data));
        var pinned = document.Pages[1].Annotations.Single().Attachment;
        Assert.That((pinned.Key, pinned.FileName, pinned.GetData().ToArray()), Is.EqualTo(((string)null, "pinned.bin",
            new byte[] { 1, 2, 3 })));
    }

    [Test]
    public void ThePropertiesAreTheInformationDictionarys()
    {
        var document = Document("", "", "/Title (Report) /Author <FEFF0410043D043D0430> /Producer (Adamantium) " +
                                        "/CreationDate (D:20261110090000+03'00) /Subject ()");

        var properties = document.Properties;

        Assert.That((properties.Title, properties.Author, properties.Producer, properties.Subject, properties.Creator),
            Is.EqualTo(("Report", "Анна", "Adamantium", (string)null, (string)null)));
        Assert.That(properties.Created, Is.EqualTo(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(3))));
    }

    [Test]
    public void AnAppearanceOfTheAnnotationsStateIsSetIntoItsRectangle()
    {
        var document = Document("",
            "/Annots [<< /Type /Annot /Subtype /Widget /Rect [100 200 140 220] /AS /On /AP << /N << /On 6 0 R /Off 7 0 R >> >> >>]",
            "",
            Encoding.Latin1.GetString(PdfFileBuilder.Stream("/Type /XObject /Subtype /Form /BBox [0 0 20 40] /Matrix [0 1 -1 0 0 0]",
                Encoding.ASCII.GetBytes("0 0 m"))),
            Encoding.Latin1.GetString(PdfFileBuilder.Stream("/Type /XObject /Subtype /Form /BBox [0 0 1 1]",
                Encoding.ASCII.GetBytes("0 0 m"))));
        var annotation = document.Pages[1].Annotations.Single();

        var appearance = annotation.Appearance;
        var transform = annotation.AppearanceTransform(appearance);

        Assert.That(PdfRectangle.From(appearance.Dictionary["BBox"], document), Is.EqualTo(new PdfRectangle(0, 0, 20, 40)),
            "the On state's");
        var corners = new[] { transform.Transform(0, 0), transform.Transform(20, 0), transform.Transform(20, 40),
            transform.Transform(0, 40) };
        Assert.That(corners.Min(c => c.X), Is.EqualTo(100).Within(1e-9));
        Assert.That(corners.Max(c => c.X), Is.EqualTo(140).Within(1e-9));
        Assert.That(corners.Min(c => c.Y), Is.EqualTo(200).Within(1e-9));
        Assert.That(corners.Max(c => c.Y), Is.EqualTo(220).Within(1e-9));
        Assert.That(annotation.IsHidden, Is.False);
    }
}
