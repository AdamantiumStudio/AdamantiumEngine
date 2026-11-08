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

    [Test]
    public void AesV2TakesA128BitKeyWhateverTheLengthSays()
    {
        var encryption = new PdfTestEncryption(4, string.Empty, "owner");
        var file = Encoding.Latin1.GetString(EncryptedFile(encryption));
        var shortLength = file.Replace("/V 4 /R 4 /Length 128", "/V 4 /R 4 /Length 40 ");
        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(shortLength));
        Assert.That(document.IsRepaired, Is.False);
        Assert.That(document.IsAuthenticated, Is.True);
        Assert.That(Encoding.ASCII.GetString(document.Pages[0].GetContents().Span), Is.EqualTo("BT (secret) Tj ET"));
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
