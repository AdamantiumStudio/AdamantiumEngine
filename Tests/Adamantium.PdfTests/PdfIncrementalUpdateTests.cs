using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Adamantium.Pdf;
using Adamantium.Pdf.Annotations;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Measurement;
using Adamantium.Pdf.Navigation;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Writing;
using NUnit.Framework;

namespace Adamantium.PdfTests;

/// <summary>Changes written after a file's bytes: annotations added, replaced and taken off, the catalog's developer
/// extensions - each read back by opening what was written.</summary>
public class PdfIncrementalUpdateTests
{
    private const string Key = "AUUI_Mark";

    private static PdfFileBuilder Pages(string annots = "")
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Contents 4 0 R " + annots + " >>");
        builder.Add(PdfFileBuilder.Stream(string.Empty, Encoding.ASCII.GetBytes("0 0 m 100 100 l S")));
        return builder;
    }

    private static PdfNewAnnotation Square(string name, string text = "a note")
    {
        var data = new PdfDictionary();
        data.Set("V", new PdfInteger(1));
        data.Set("Arrow head", new PdfName("Open#Arrow (big)"));
        data.Set("Note", PdfString.FromText("скобки ( ) и \\ обратная"));
        var appearance = new PdfContentBuilder().Opacity(0.5, 0.25).StrokeColor(1, 0, 0).LineWidth(2)
            .Dash([3, 1]).Rectangle(1, 1, 98, 48).Stroke();
        return new PdfNewAnnotation(PdfAnnotationTypes.Square, new PdfRectangle(100, 200, 200, 250))
        {
            Name = name,
            Contents = text,
            Author = "Тестер",
            Modified = new DateTimeOffset(2026, 11, 9, 20, 5, 0, TimeSpan.FromHours(3)),
            Color = [1, 0, 0],
            Opacity = 0.5,
            BorderWidth = 2,
            Dash = [3, 1],
            Measure = PdfMeasure.Scaled("1 pt = 0.5 m", "m", 0.5),
            Appearance = appearance.ToFormXObject(new PdfRectangle(0, 0, 100, 50)),
            ExtensionKey = Key,
            ExtensionData = data,
        };
    }

    private static void AssertSquare(PdfAnnotation annotation, string name, string text = "a note")
    {
        Assert.That(annotation.Subtype, Is.EqualTo(PdfAnnotationTypes.Square));
        Assert.That(annotation.Rect, Is.EqualTo(new PdfRectangle(100, 200, 200, 250)));
        Assert.That(annotation.Name, Is.EqualTo(name));
        Assert.That(annotation.Contents, Is.EqualTo(text));
        Assert.That(annotation.Author, Is.EqualTo("Тестер"));
        Assert.That(annotation.Flags, Is.EqualTo(4));
        Assert.That(annotation.Measure.FormatDistance(10, 0), Is.EqualTo("5.00 m"));
        var data = annotation.ExtensionData(Key);
        Assert.That(data["V"], Is.TypeOf<PdfInteger>());
        Assert.That(((PdfName)data["Arrow head"]).Value, Is.EqualTo("Open#Arrow (big)"));
        Assert.That(((PdfString)data["Note"]).ToText(), Is.EqualTo("скобки ( ) и \\ обратная"));
    }

    private static string Appearance(PdfDocument document, PdfAnnotation annotation)
    {
        var appearances = (PdfDictionary)document.Resolve(annotation.Dictionary["AP"]);
        var stream = (PdfStream)document.Resolve(appearances["N"]);
        return Encoding.ASCII.GetString(document.Decode(stream).Data.Span);
    }

    [Test]
    public void AnAnnotationAddedAfterATableIsReadBack()
    {
        var original = Pages().Build("/Root 1 0 R");
        var document = PdfDocument.Open(original);
        var update = new PdfIncrementalUpdate(document);

        update.AddAnnotation(document.Pages[0], Square("mark-1"));
        update.DeclareExtension("AUUI", "2.0", 1);
        var written = update.Write();

        Assert.That(written.AsSpan(0, original.Length).SequenceEqual(original), Is.True, "the file is kept byte for byte");
        var tail = Encoding.Latin1.GetString(written, original.Length, written.Length - original.Length);
        Assert.That(tail, Does.Contain("\nxref\n").Or.StartWith("xref\n"), "a table, as the file had");
        var reopened = PdfDocument.Open(written);
        Assert.That(reopened.IsRepaired, Is.False);
        var annotation = reopened.Pages[0].Annotations.Single();
        AssertSquare(annotation, "mark-1");
        Assert.That(Appearance(reopened, annotation), Does.Contain("/GS0 gs").And.Contain("1 0 0 RG").And.Contain("re"));
        var extension = (PdfDictionary)reopened.Resolve(
            ((PdfDictionary)reopened.Resolve(reopened.Catalog["Extensions"]))["AUUI"]);
        Assert.That(((PdfName)extension["BaseVersion"]).Value, Is.EqualTo("2.0"));
        Assert.That(((PdfInteger)extension["ExtensionLevel"]).Value, Is.EqualTo(1));
        Assert.That(reopened.Pages[0].GetContents().ToArray(), Is.EqualTo(Encoding.ASCII.GetBytes("0 0 m 100 100 l S")));
    }

    [Test]
    public void AFileOfCrossReferenceStreamsGetsOneAndItsPackedObjectsStayReadable()
    {
        var original = Pages().BuildWithStream("/Root 1 0 R", new HashSet<int> { 2, 3 });
        var document = PdfDocument.Open(original);
        var update = new PdfIncrementalUpdate(document);

        update.AddAnnotation(document.Pages[0], Square("mark-1"));
        var written = update.Write();

        var tail = Encoding.Latin1.GetString(written, original.Length, written.Length - original.Length);
        Assert.That(tail, Does.Contain("/Type /XRef").And.Not.Contain("\nxref\n"));
        var reopened = PdfDocument.Open(written);
        Assert.That(reopened.IsRepaired, Is.False);
        Assert.That(reopened.PageCount, Is.EqualTo(1));
        AssertSquare(reopened.Pages[0].Annotations.Single(), "mark-1");
    }

    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    public void AnEncryptedFilesNewObjectsAreEncryptedAsItsOthers(int revision)
    {
        var encryption = new PdfTestEncryption(revision, "user", "owner");
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Contents 4 0 R >>");
        builder.Add(PdfFileBuilder.Stream(string.Empty, encryption.Encrypt(4, Encoding.ASCII.GetBytes("0 0 m S"))));
        builder.Add(encryption.Dictionary);
        var original = builder.Build("/Root 1 0 R /Encrypt 5 0 R /ID " + encryption.IdArray);
        var document = PdfDocument.Open(original);
        Assert.That(document.Authenticate("user"), Is.True);
        var update = new PdfIncrementalUpdate(document);

        update.AddAnnotation(document.Pages[0], Square("mark-1", "тайна"));
        var written = update.Write();

        var tail = Encoding.Latin1.GetString(written, original.Length, written.Length - original.Length);
        Assert.That(tail, Does.Not.Contain("a note").And.Not.Contain("re\nS"), "strings and streams are encrypted");
        var reopened = PdfDocument.Open(written);
        Assert.That(reopened.Authenticate("user"), Is.True);
        var annotation = reopened.Pages[0].Annotations.Single();
        AssertSquare(annotation, "mark-1", "тайна");
        Assert.That(Appearance(reopened, annotation), Does.Contain("1 0 0 RG"));
    }

    [Test]
    public void AnUpdateOfAnUpdateReplacesAndTakesOffWhatTheFirstWrote()
    {
        var first = PdfDocument.Open(Pages().Build("/Root 1 0 R"));
        var adding = new PdfIncrementalUpdate(first);
        adding.AddAnnotation(first.Pages[0], Square("mark-1"));
        adding.AddAnnotation(first.Pages[0], Square("mark-2"));
        var second = PdfDocument.Open(adding.Write());

        var changing = new PdfIncrementalUpdate(second);
        var page = second.Pages[0];
        changing.ReplaceAnnotation(page, page.Annotations[0], Square("mark-1", "changed"));
        changing.RemoveAnnotation(page, page.Annotations[1]);
        var removedAppearance = ((PdfReference)((PdfDictionary)second.Resolve(page.Annotations[1].Dictionary["AP"]))["N"]).Id;
        var third = PdfDocument.Open(changing.Write());

        var annotation = third.Pages[0].Annotations.Single();
        AssertSquare(annotation, "mark-1", "changed");
        Assert.That(annotation.Id, Is.EqualTo(page.Annotations[0].Id), "replaced in place");
        Assert.That(third.Resolve(new PdfReference(page.Annotations[1].Id!.Value)), Is.SameAs(PdfNull.Value));
        Assert.That(third.Resolve(new PdfReference(removedAppearance)), Is.SameAs(PdfNull.Value));
        Assert.That(third.IsRepaired, Is.False);
    }

    [Test]
    public void APageWhoseAnnotationsAreAnObjectOfTheirOwnKeepsItAndTheOthers()
    {
        var builder = Pages("/Annots 5 0 R");
        builder.Add("[6 0 R]");
        builder.Add("<< /Type /Annot /Subtype /Text /Rect [10 10 30 30] /Contents (theirs) >>");
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        var update = new PdfIncrementalUpdate(document);

        update.AddAnnotation(document.Pages[0], Square("mark-1"));
        var written = update.Write();

        var reopened = PdfDocument.Open(written);
        var annotations = reopened.Pages[0].Annotations;
        Assert.That(annotations.Select(a => a.Subtype), Is.EqualTo(new[] { "Text", "Square" }));
        Assert.That(annotations[0].Contents, Is.EqualTo("theirs"));
        Assert.That(reopened.Pages[0].Dictionary["Annots"], Is.TypeOf<PdfReference>(), "the page itself is not rewritten");
    }

    [Test]
    public void ARepairedFileGetsAWholeCrossReference()
    {
        var original = Pages().Build("/Root 1 0 R");
        var text = Encoding.Latin1.GetString(original);
        var broken = Encoding.Latin1.GetBytes(text.Replace("startxref\n" + PdfFileBuilder.StartXref(original),
            "startxref\n999999"));
        var document = PdfDocument.Open(broken);
        Assert.That(document.IsRepaired, Is.True);
        var update = new PdfIncrementalUpdate(document);

        update.AddAnnotation(document.Pages[0], Square("mark-1"));
        var reopened = PdfDocument.Open(update.Write());

        Assert.That(reopened.IsRepaired, Is.False, "the new cross-reference holds every object");
        Assert.That(reopened.Pages[0].GetContents().ToArray(), Is.EqualTo(Encoding.ASCII.GetBytes("0 0 m 100 100 l S")));
        AssertSquare(reopened.Pages[0].Annotations.Single(), "mark-1");
    }

    [Test]
    public void PagesSharingOneAnnotationArrayKeepEveryAddition()
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Annots 5 0 R >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Annots 5 0 R >>");
        builder.Add("[]");
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        var update = new PdfIncrementalUpdate(document);

        update.AddAnnotation(document.Pages[0], Square("a1"));
        update.AddAnnotation(document.Pages[1], Square("b1"));
        update.AddAnnotation(document.Pages[0], Square("a2"));
        var reopened = PdfDocument.Open(update.Write());

        Assert.That(reopened.Pages[0].Annotations.Select(a => a.Name), Is.EqualTo(new[] { "a1", "b1", "a2" }));
        Assert.That(reopened.Pages[1].Annotations.Select(a => a.Name), Is.EqualTo(new[] { "a1", "b1", "a2" }));
    }

    [TestCase(3)]
    [TestCase(6)]
    public void AStreamOfAnEncryptedFileWrittenAgainIsNotEncryptedTwice(int revision)
    {
        var encryption = new PdfTestEncryption(revision, "user", "owner");
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Contents 4 0 R >>");
        builder.Add(PdfFileBuilder.Stream(string.Empty, encryption.Encrypt(4, Encoding.ASCII.GetBytes("0 0 m S"))));
        builder.Add(encryption.Dictionary);
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R /Encrypt 5 0 R /ID " + encryption.IdArray));
        document.Authenticate("user");
        var update = new PdfIncrementalUpdate(document);
        var contents = (PdfReference)document.Pages[0].Dictionary["Contents"];

        update.Replace(contents.Id, document.Resolve(contents));
        update.Replace(new PdfObjectId(9, 0), document.Resolve(contents));
        var reopened = PdfDocument.Open(update.Write());
        reopened.Authenticate("user");

        Assert.That(reopened.Pages[0].GetContents().ToArray(), Is.EqualTo(Encoding.ASCII.GetBytes("0 0 m S")));
        var moved = (PdfStream)reopened.Resolve(new PdfReference(9, 0));
        Assert.That(reopened.Decode(moved).Data.ToArray(), Is.EqualTo(Encoding.ASCII.GetBytes("0 0 m S")),
            "re-encrypted with the key of its new number");
    }

    [Test]
    public void AHugeDeclaredSizeDoesNotLeadNumbersPastTheLimit()
    {
        var document = PdfDocument.Open(Pages().Build("/Root 1 0 R /Size 2147483647"));
        var update = new PdfIncrementalUpdate(document);

        var reference = update.AddAnnotation(document.Pages[0], Square("mark-1"));

        Assert.That(reference.Id.Number, Is.LessThan(100));
        var reopened = PdfDocument.Open(update.Write());
        Assert.That(reopened.IsRepaired, Is.False);
        AssertSquare(reopened.Pages[0].Annotations.Single(), "mark-1");
    }

    [Test]
    public void AnObjectReplacedPastTheEndIsNotTakenByTheCrossReferenceStream()
    {
        var original = Pages().BuildWithStream("/Root 1 0 R");
        var document = PdfDocument.Open(original);
        var update = new PdfIncrementalUpdate(document);
        var beyond = new PdfObjectId(40, 0);
        var value = new PdfDictionary();
        value.Set("Kept", PdfBoolean.True);

        update.Replace(beyond, value);
        var reopened = PdfDocument.Open(update.Write());

        Assert.That(((PdfDictionary)reopened.Resolve(new PdfReference(beyond)))["Kept"], Is.SameAs(PdfBoolean.True));
        Assert.That(PdfValues.Integer(reopened.Trailer["Size"], reopened), Is.GreaterThan(40));
        Assert.Throws<ArgumentOutOfRangeException>(() => update.Replace(new PdfObjectId(3, 100000), value));
        Assert.Throws<ArgumentOutOfRangeException>(() => update.Remove(new PdfObjectId(0, 0)));
    }

    [Test]
    public void AFileWithBytesBeforeItsHeaderKeepsCountingFromTheHeader()
    {
        var builder = Pages();
        builder.Prefix = Encoding.ASCII.GetBytes("junk before the header\n");
        var original = builder.Build("/Root 1 0 R");
        var document = PdfDocument.Open(original);
        var update = new PdfIncrementalUpdate(document);

        update.AddAnnotation(document.Pages[0], Square("mark-1"));
        var written = update.Write();

        var tail = Encoding.Latin1.GetString(written, original.Length, written.Length - original.Length);
        Assert.That(tail, Does.Contain("xref\n").And.Not.Contain("/Type /XRef"), "still a table");
        var startXref = PdfFileBuilder.StartXref(written);
        Assert.That(Encoding.Latin1.GetString(written, (int)startXref + builder.Prefix.Length, 4), Is.EqualTo("xref"));
        AssertSquare(PdfDocument.Open(written).Pages[0].Annotations.Single(), "mark-1");
    }

    [Test]
    public void ARepairedOldFileGetsTheVersionItsCrossReferenceStreamNeeds()
    {
        var builder = Pages();
        builder.Header = "%PDF-1.4\n";
        var original = builder.Build("/Root 1 0 R");
        var broken = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(original)
            .Replace("startxref\n" + PdfFileBuilder.StartXref(original), "startxref\n999999"));
        var document = PdfDocument.Open(broken);
        var update = new PdfIncrementalUpdate(document);

        update.AddAnnotation(document.Pages[0], Square("mark-1"));
        var reopened = PdfDocument.Open(update.Write());

        Assert.That(reopened.Version, Is.EqualTo("1.5"));
    }

    [Test]
    public void AnAppearanceAnotherAnnotationShowsTooStays()
    {
        var builder = Pages("/Annots [5 0 R 6 0 R]");
        builder.Add("<< /Type /Annot /Subtype /Square /Rect [0 0 10 10] /AP << /N 7 0 R >> >>");
        builder.Add("<< /Type /Annot /Subtype /Square /Rect [20 0 30 10] /AP << /N 7 0 R >> >>");
        builder.Add(PdfFileBuilder.Stream("/Type /XObject /Subtype /Form /BBox [0 0 10 10]",
            Encoding.ASCII.GetBytes("0 0 10 10 re f")));
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R"));
        var update = new PdfIncrementalUpdate(document);
        var page = document.Pages[0];

        update.RemoveAnnotation(page, page.Annotations[0]);
        var reopened = PdfDocument.Open(update.Write());

        var left = reopened.Pages[0].Annotations.Single();
        Assert.That(reopened.Resolve(((PdfDictionary)left.Dictionary["AP"])["N"]), Is.TypeOf<PdfStream>());
    }

    [Test]
    public void ATableWhoseFirstLineIsMisplacedStillReadsItsObjects()
    {
        var builder = Pages();
        var original = Encoding.Latin1.GetString(builder.Build("/Root 1 0 R"));
        var damaged = original.Replace("xref\n0 5\n0000000000 65535 f\r\n", "xref\n1 5\n0000000000 65535 f\r\n");
        Assert.That(damaged, Is.Not.EqualTo(original));

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(damaged));

        Assert.That(document.PageCount, Is.EqualTo(1));
    }

    [Test]
    public void ALockedFileIsNotUpdated()
    {
        var encryption = new PdfTestEncryption(4, "user", "owner");
        var builder = Pages();
        builder.Add(encryption.Dictionary);
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R /Encrypt 5 0 R /ID " + encryption.IdArray));

        Assert.Throws<PdfException>(() => new PdfIncrementalUpdate(document));
    }

    [Test]
    public void TextStringsAreDocEncodedWhenTheyCanBeAndUnicodeOtherwise()
    {
        Assert.That(PdfTextEncoding.Encode("Café • €"), Is.EqualTo(new byte[] { 0x43, 0x61, 0x66, 0xE9, 0x20, 0x80, 0x20, 0xA0 }));
        Assert.That(PdfTextEncoding.Encode("Дом")[..2], Is.EqualTo(new byte[] { 0xFE, 0xFF }));
        foreach (var text in new[] { "Café • € ﬁ", "Дом и сад", "tab\tline\n", "", "þÿAB", "ï»¿x" })
        {
            Assert.That(PdfTextEncoding.Decode(PdfTextEncoding.Encode(text)), Is.EqualTo(text));
        }
    }

    [Test]
    public void ADateCarriesItsOffsetAndReadsBack()
    {
        var times = new[]
        {
            new DateTimeOffset(2026, 11, 9, 20, 5, 7, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromMinutes(-150)),
        };
        Assert.That(times.Select(PdfDate.Format),
            Is.EqualTo(new[] { "D:20261109200507+03'00", "D:20260102030405Z", "D:20260102030405-02'30" }));
        foreach (var time in times)
        {
            Assert.That(PdfDate.Parse(PdfDate.Format(time)), Is.EqualTo(time));
        }

        Assert.That(PdfDate.Parse("D:2026"), Is.EqualTo(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.That(PdfDate.Parse("D:20260230120000+05'30'"),
            Is.EqualTo(new DateTimeOffset(2026, 2, 28, 12, 0, 0, TimeSpan.FromMinutes(330))), "a day past the month's end");
        Assert.That(PdfDate.Parse("not a date"), Is.Null);
        Assert.That(PdfDate.Parse(""), Is.Null);
    }
}
