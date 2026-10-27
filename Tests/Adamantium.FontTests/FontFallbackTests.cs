using System.IO;
using Adamantium.Fonts;
using NUnit.Framework;

namespace Adamantium.FontTests;

/// <summary>A character none of the listed families has is drawn by another family of the collection that has it, the
/// one sharing most words of the text's family name first.</summary>
[TestFixture]
public class FontFallbackTests
{
    private const int Alef = 0x05D0;
    private const int ArabicBeh = 0x0628;

    private FontCollection fonts;

    [OneTimeSetUp]
    public void IndexTheScriptFonts()
    {
        fonts = new FontCollection();
        fonts.AddFolder(Path.Combine(TestContext.CurrentContext.TestDirectory, "ScriptFonts"));
    }

    [Test]
    public void ACharacterNoListedFamilyHas_IsDrawnByAnotherFamilyThatHasIt()
    {
        var fallback = new FontFallback(fonts, []);
        var like = FontCollection.Load(fonts.Match("Noto Sans Arabic"));

        var font = fallback.FontFor(Alef, like);

        Assert.That(FamilyOf(font), Is.EqualTo("Noto Sans Hebrew"));
    }

    [Test]
    public void OfTheFamiliesThatHaveIt_TheOneSharingMostWordsWithTheTextsFamily_IsTaken()
    {
        var fallback = new FontFallback(fonts, []);
        var like = FontCollection.Load(fonts.Match("Noto Sans Hebrew"));

        var font = fallback.FontFor(ArabicBeh, like);

        Assert.That(FamilyOf(font), Is.EqualTo("Noto Sans Arabic"),
            "Noto Naskh Arabic has it too, but shares one word where Noto Sans Arabic shares two");
    }

    private static string FamilyOf(IFont font) =>
        string.IsNullOrEmpty(font?.TypographicFamilyName) ? font?.FontFamily : font.TypographicFamilyName;
}
