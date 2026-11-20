using System.Text;
using Adamantium.Pdf;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Security;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfSecurityTests
{
    private static byte[] EncryptedFile(PdfTestEncryption encryption)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>");
        builder.Add(PdfFileBuilder.Stream(string.Empty, encryption.Encrypt(4, Encoding.ASCII.GetBytes("BT (secret) Tj ET"))));
        builder.Add("<< /Title " + encryption.EncryptedString(5, "Hidden title") + " >>");
        builder.Add(encryption.Dictionary);
        return builder.Build("/Root 1 0 R /Info 5 0 R /Encrypt 6 0 R /ID " + encryption.IdArray);
    }

    [TestCase("Key", "Plaintext", "BBF316E8D940AF0AD3")]
    [TestCase("Wiki", "pedia", "1021BF0420")]
    [TestCase("Secret", "Attack at dawn", "45A01F645FC35B383552544B9BF5")]
    public void Rc4MatchesItsKnownVectors(string key, string text, string expected)
    {
        var encrypted = PdfRc4.Transform(Encoding.ASCII.GetBytes(key), Encoding.ASCII.GetBytes(text));
        Assert.That(System.Convert.ToHexString(encrypted), Is.EqualTo(expected));
    }

    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    public void TheEmptyUserPasswordOpensAFileOnItsOwn(int revision)
    {
        var document = PdfDocument.Open(EncryptedFile(new PdfTestEncryption(revision, string.Empty, "owner")));
        Assert.That(document.IsEncrypted, Is.True);
        Assert.That(document.IsAuthenticated, Is.True);
        Assert.That(document.Security.IsOwner, Is.False);
        Assert.That(document.GetInfoText("Title"), Is.EqualTo("Hidden title"));
        Assert.That(Encoding.ASCII.GetString(document.Pages[0].GetContents().Span), Is.EqualTo("BT (secret) Tj ET"));
    }

    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    public void AUserPasswordIsAskedForAndChecked(int revision)
    {
        var document = PdfDocument.Open(EncryptedFile(new PdfTestEncryption(revision, "user", "owner", -3904)));
        Assert.That(document.IsAuthenticated, Is.False);
        Assert.That(document.Info, Is.Null);
        Assert.Throws<PdfException>(() => _ = document.Pages);
        Assert.That(document.Authenticate("wrong"), Is.False);
        Assert.That(document.Authenticate("user"), Is.True);
        Assert.That(document.Security.IsOwner, Is.False);
        Assert.That(document.Permissions, Is.EqualTo((PdfPermissions)(-3904) & PdfPermissions.All));
        Assert.That(document.GetInfoText("Title"), Is.EqualTo("Hidden title"));
        Assert.That(Encoding.ASCII.GetString(document.Pages[0].GetContents().Span), Is.EqualTo("BT (secret) Tj ET"));
    }

    [TestCase(3)]
    [TestCase(6)]
    public void TheSameFileSavedAgain_TakesThePasswordItWasGiven_AnotherFileDoesNot(int revision)
    {
        var bytes = EncryptedFile(new PdfTestEncryption(revision, "user", "owner", -3904));
        byte[] grown = [.. bytes, .. "\n% saved again\n"u8];
        var given = PdfDocument.Open(bytes);
        var ownerGiven = given.Authenticate("owner");
        var byOptions = PdfDocument.Open(bytes, new PdfReadOptions { Password = "user" });
        var wrongOptions = PdfDocument.Open(bytes, new PdfReadOptions { Password = "typo" });
        var savedAgain = PdfDocument.Open(grown);
        var savedAgainToo = PdfDocument.Open(grown);
        var ownerAlready = PdfDocument.Open(grown, new PdfReadOptions { Password = "owner" });
        var another = PdfDocument.Open(EncryptedFile(new PdfTestEncryption(revision, "user", "owner", -4)));
        var locked = PdfDocument.Open(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(ownerGiven, Is.True);
            Assert.That(savedAgain.TakePasswordFrom(given), Is.True);
            Assert.That(savedAgain.Security.IsOwner, Is.True, "the owner's, as given");
            Assert.That(savedAgainToo.TakePasswordFrom(byOptions), Is.True, "a password given when opening too");
            Assert.That(savedAgainToo.Security.IsOwner, Is.False, "the user's, as given");
            Assert.That(ownerAlready.TakePasswordFrom(byOptions), Is.True);
            Assert.That(ownerAlready.Security.IsOwner, Is.True, "the user's password does not lower the owner's rights");
            Assert.That(PdfDocument.Open(grown).TakePasswordFrom(wrongOptions), Is.False, "a password that opened nothing");
            Assert.That(another.TakePasswordFrom(given), Is.False, "another file, though its password is the same");
            Assert.That(another.IsAuthenticated, Is.False);
            Assert.That(PdfDocument.Open(bytes).TakePasswordFrom(locked), Is.False, "nothing was given");
            Assert.That(PdfDocument.Open(bytes).TakePasswordFrom(null), Is.False);
        });
    }

    // A password typed, carried or given when opening is told; the empty one a restricted file opens with, or a wrong
    // one, is not.
    [TestCase(3)]
    [TestCase(6)]
    public void APasswordThatOpenedTheFile_IsTold(int revision)
    {
        var bytes = EncryptedFile(new PdfTestEncryption(revision, "user", "owner", -3904));
        var restricted = PdfDocument.Open(EncryptedFile(new PdfTestEncryption(revision, string.Empty, "owner", -3904)));
        var emptyTyped = PdfDocument.Open(EncryptedFile(new PdfTestEncryption(revision, string.Empty, "owner", -3904)));
        emptyTyped.Authenticate(string.Empty);
        var typed = PdfDocument.Open(bytes);
        typed.Authenticate("user");
        var wrong = PdfDocument.Open(bytes);
        wrong.Authenticate("typo");
        var carried = PdfDocument.Open([.. bytes, .. "\n% saved again\n"u8]);
        carried.TakePasswordFrom(typed);

        Assert.Multiple(() =>
        {
            Assert.That(restricted.IsAuthenticated, Is.True);
            Assert.That(restricted.IsOpenedWithPassword, Is.False, "the empty password is told as one typed");
            Assert.That(emptyTyped.IsOpenedWithPassword, Is.False, "the empty password, given, is told as one typed");
            Assert.That(typed.IsOpenedWithPassword, Is.True);
            Assert.That(wrong.IsOpenedWithPassword, Is.False, "a wrong password is told as opening the file");
            Assert.That(carried.IsOpenedWithPassword, Is.True, "the password carried to the file saved again");
            Assert.That(PdfDocument.Open(bytes, new PdfReadOptions { Password = "user" }).IsOpenedWithPassword, Is.True);
        });
    }

    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    public void AWrongPasswordAfterTheOwnersKeepsTheOwnersAccess(int revision)
    {
        var options = new PdfReadOptions { Password = "owner" };
        var document = PdfDocument.Open(EncryptedFile(new PdfTestEncryption(revision, "user", "owner", -3904)), options);
        Assert.That(document.Authenticate("wrong"), Is.False);
        Assert.That(document.Security.IsOwner, Is.True);
        Assert.That(document.Permissions, Is.EqualTo(PdfPermissions.All));
    }

    // How long the key is, as a viewer tells it: 128 bits for RC4 here, 128 for AES-128, 256 for AES-256.
    [TestCase(3, 128)]
    [TestCase(4, 128)]
    [TestCase(6, 256)]
    public void TheKeysLength_IsToldInBits(int revision, int bits)
    {
        var document = PdfDocument.Open(EncryptedFile(new PdfTestEncryption(revision, string.Empty, "owner")));
        Assert.That(document.Security.KeyBits, Is.EqualTo(bits));
    }

    [Test]
    public void AesV2TakesA128BitKeyWhateverTheLengthSays()
    {
        var encryption = new PdfTestEncryption(4, string.Empty, "owner");
        var file = Encoding.Latin1.GetString(EncryptedFile(encryption));
        var shortLength = file.Replace("/V 4 /R 4 /Length 128", "/V 4 /R 4 /Length 40 ");
        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(shortLength));
        Assert.That(document.IsRepaired, Is.False);
        Assert.That(document.IsAuthenticated, Is.True);
        Assert.That(document.Security.KeyBits, Is.EqualTo(128));
        Assert.That(Encoding.ASCII.GetString(document.Pages[0].GetContents().Span), Is.EqualTo("BT (secret) Tj ET"));
    }

    // A file that encrypts only its embedded files (EFF; strings and streams Identity), its one attachment's stream
    // written with or without its optional /Type.
    private static byte[] OnlyAttachmentsEncrypted(PdfTestEncryption encryption, bool typed, bool asksOnOpen = true)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles << /Names [(data.txt) 5 0 R] >> >> >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>");
        builder.Add(PdfFileBuilder.Stream(string.Empty, Encoding.ASCII.GetBytes("BT (plain) Tj ET")));
        builder.Add("<< /Type /Filespec /F (data.txt) /EF << /F 6 0 R >> >>");
        builder.Add(PdfFileBuilder.Stream(typed ? "/Type /EmbeddedFile" : string.Empty,
            encryption.Encrypt(6, Encoding.ASCII.GetBytes("attached secret"))));
        var dictionary = encryption.Dictionary.Replace("/StmF /StdCF /StrF /StdCF", "/StmF /Identity /StrF /Identity /EFF /StdCF");
        if (asksOnOpen)
        {
            dictionary = dictionary.Replace("/StdCF << ", "/StdCF << /AuthEvent /EFOpen ");
        }

        builder.Add(dictionary);
        return builder.Build("/Root 1 0 R /Encrypt 7 0 R /ID " + encryption.IdArray);
    }

    // Strings and streams are plain there, so a mark is written in without the password - and reads back.
    [Test]
    public void AFileEncryptingOnlyItsAttachments_TakesAMark_WithoutThePassword()
    {
        var document = PdfDocument.Open(OnlyAttachmentsEncrypted(new PdfTestEncryption(4, "user", "owner"), true));
        var update = new Adamantium.Pdf.Writing.PdfIncrementalUpdate(document);
        update.AddAnnotation(document.Pages[0],
            new Adamantium.Pdf.Annotations.PdfNewAnnotation(Adamantium.Pdf.Annotations.PdfAnnotationTypes.Square,
                new PdfRectangle(0, 0, 50, 50)) { Contents = "plain note" });

        var again = PdfDocument.Open(update.Write());

        Assert.Multiple(() =>
        {
            Assert.That(again.IsAuthenticated, Is.True);
            Assert.That(again.Pages[0].Annotations, Has.Count.EqualTo(1));
            Assert.That(again.Pages[0].Annotations[0].Contents, Is.EqualTo("plain note"));
        });
    }

    // The document opens without a password, so it is the attachments' password that is carried to the same file saved
    // again - and only to it.
    [Test]
    public void AFileEncryptingOnlyItsAttachments_SavedAgain_TakesTheirPassword()
    {
        var bytes = OnlyAttachmentsEncrypted(new PdfTestEncryption(4, "user", "owner"), true);
        byte[] grown = [.. bytes, .. "\n% saved again\n"u8];
        var given = PdfDocument.Open(bytes);
        var unlocked = given.Authenticate("user");
        var savedAgain = PdfDocument.Open(grown);
        var notGiven = PdfDocument.Open(grown);

        Assert.Multiple(() =>
        {
            Assert.That(unlocked, Is.True);
            Assert.That(savedAgain.TakePasswordFrom(given), Is.True);
            Assert.That(savedAgain.Attachments[0].NeedsPassword, Is.False, "the attachments' password was not carried");
            Assert.That(Encoding.ASCII.GetString(savedAgain.Attachments[0].GetData().Span), Is.EqualTo("attached secret"));
            Assert.That(notGiven.TakePasswordFrom(PdfDocument.Open(bytes)), Is.True, "the document itself is open");
            Assert.That(notGiven.Attachments[0].NeedsPassword, Is.True, "a password was carried that was never given");
        });
    }

    // The document opens without a password: the one its attachments asked for is not told as opening it - unless the
    // filter asks for it at the document's opening.
    [Test]
    public void TheAttachmentsPassword_IsNotToldAsOpeningTheDocument()
    {
        var encryption = new PdfTestEncryption(4, "user", "owner");
        var asksForAttachments = PdfDocument.Open(OnlyAttachmentsEncrypted(encryption, true));
        asksForAttachments.Authenticate("user");
        var asksAtOpening = PdfDocument.Open(OnlyAttachmentsEncrypted(encryption, true, false));
        asksAtOpening.Authenticate("user");

        Assert.Multiple(() =>
        {
            Assert.That(asksForAttachments.IsOpenedWithPassword, Is.False);
            Assert.That(asksAtOpening.IsOpenedWithPassword, Is.True);
        });
    }

    // The owner's password after the user's opens with the same key: what was read stands, only the rights change.
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    public void TheOwnersPasswordAfterTheUsers_KeepsWhatWasRead(int revision)
    {
        var document = PdfDocument.Open(EncryptedFile(new PdfTestEncryption(revision, "user", "owner", -3904)));
        document.Authenticate("user");
        var page = document.Pages[0];

        var lifted = document.Authenticate("owner");

        Assert.Multiple(() =>
        {
            Assert.That(lifted, Is.True);
            Assert.That(document.Security.IsOwner, Is.True);
            Assert.That(document.Permissions, Is.EqualTo(PdfPermissions.All));
            Assert.That(document.Pages[0], Is.SameAs(page), "what was read was let go though the key stayed");
            Assert.That(Encoding.ASCII.GetString(document.Pages[0].GetContents().Span), Is.EqualTo("BT (secret) Tj ET"));
        });
    }

    // Without AuthEvent EFOpen the filter asks at the document's opening, as by default.
    [Test]
    public void AFileEncryptingOnlyItsAttachments_ButAskingAtOpening_NeedsThePasswordFirst()
    {
        var document = PdfDocument.Open(OnlyAttachmentsEncrypted(new PdfTestEncryption(4, "user", "owner"), true, false));

        Assert.That(document.IsAuthenticated, Is.False);
        Assert.That(document.Authenticate("user"), Is.True);
        Assert.That(Encoding.ASCII.GetString(document.Attachments[0].GetData().Span), Is.EqualTo("attached secret"));
    }

    [TestCase(4, true)]
    [TestCase(4, false)]
    [TestCase(6, true)]
    public void AFileEncryptingOnlyItsAttachments_ReadsWithoutAPassword_AndAsksOneForThem(int revision, bool typed)
    {
        var document = PdfDocument.Open(OnlyAttachmentsEncrypted(new PdfTestEncryption(revision, "user", "owner"), typed));
        var attachment = document.Attachments[0];

        Assert.Multiple(() =>
        {
            Assert.That(document.IsEncrypted, Is.True);
            Assert.That(document.IsAuthenticated, Is.True, "the document itself is plain");
            Assert.That(document.Security.EncryptsOnlyEmbeddedFiles, Is.True);
            Assert.That(Encoding.ASCII.GetString(document.Pages[0].GetContents().Span), Is.EqualTo("BT (plain) Tj ET"));
            Assert.That(attachment.NeedsPassword, Is.True);
            Assert.Throws<PdfException>(() => attachment.GetData(), "read without the password");
        });

        Assert.That(document.Authenticate("wrong"), Is.False);
        Assert.That(document.Authenticate("user"), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(attachment.NeedsPassword, Is.False);
            Assert.That(Encoding.ASCII.GetString(attachment.GetData().Span), Is.EqualTo("attached secret"));
        });
    }

    [Test]
    public void AnAttachmentOfAFileEncryptingEverything_IsDecryptedAsItsOtherStreams()
    {
        var encryption = new PdfTestEncryption(4, string.Empty, "owner");
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles << /Names [(a.txt) 4 0 R] >> >> >>");
        builder.Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        builder.Add("<< /Type /Page /Parent 2 0 R >>");
        builder.Add("<< /Type /Filespec /F (a.txt) /EF << /F 5 0 R >> >>");
        builder.Add(PdfFileBuilder.Stream(string.Empty, encryption.Encrypt(5, Encoding.ASCII.GetBytes("everything"))));
        builder.Add(encryption.Dictionary);
        var document = PdfDocument.Open(builder.Build("/Root 1 0 R /Encrypt 6 0 R /ID " + encryption.IdArray));

        Assert.Multiple(() =>
        {
            Assert.That(document.Security.EncryptsOnlyEmbeddedFiles, Is.False);
            Assert.That(document.Attachments[0].NeedsPassword, Is.False);
            Assert.That(Encoding.ASCII.GetString(document.Attachments[0].GetData().Span), Is.EqualTo("everything"));
        });
    }

    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    public void TheOwnerPasswordOpensWithEveryPermission(int revision)
    {
        var options = new PdfReadOptions { Password = "owner" };
        var document = PdfDocument.Open(EncryptedFile(new PdfTestEncryption(revision, "user", "owner", -3904)), options);
        Assert.That(document.IsAuthenticated, Is.True);
        Assert.That(document.Security.IsOwner, Is.True);
        Assert.That(document.Permissions, Is.EqualTo(PdfPermissions.All));
        Assert.That(document.GetInfoText("Title"), Is.EqualTo("Hidden title"));
    }
}
