using System.IO;
using System.Linq;
using Adamantium.Core.Fonts;
using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class FontTableTests
{
    [Test]
    public void OnlyUnicodeCharacterMapsGiveCharactersTheirGlyphs()
    {
        var font = Typeface.LoadFont("OTFFonts/Quicksand-Bold.otf", 3).GetFont(0);

        Assert.That(font.Unicodes, Does.Not.Contain(0xDEu));
        Assert.That(font.GetGlyphByCharacter('A').Name, Is.EqualTo("A"));
    }

    [Test]
    public void TrueTypeFontHasItsOpenTypeFeatures()
    {
        var font = Typeface.LoadFont("TTFFonts/SourceSans3-Regular.ttf", 3).GetFont(0);

        Assert.That(font.FeatureCatalog.GPOSFeatures.Select(x => x.Info.Tag), Does.Contain("kern"));
    }

    [Test]
    public void FeaturesAreListedPerScriptAndLanguageSystem()
    {
        var catalog = Typeface.LoadFont("TTFFonts/SourceSans3-Regular.ttf", 3).GetFont(0).FeatureCatalog;

        Assert.That(catalog.Scripts.Select(s => s.Tag), Is.SupersetOf(new[] { "latn", "cyrl", "grek" }));
        var cyrillic = catalog.GetScript("cyrl");
        Assert.That(cyrillic.DefaultLanguage, Is.Not.Null);
        Assert.That(cyrillic.DefaultLanguage.Tag, Is.EqualTo("DFLT"));
        Assert.That(cyrillic.GetLanguage("BGR ").HasFeature("locl"), Is.True, "Bulgarian forms");
        Assert.That(cyrillic.GetLanguage("SRB "), Is.Not.Null.And.Not.SameAs(cyrillic.GetLanguage("BGR ")));
        Assert.That(catalog.Features.Select(f => (f.Info.Tag, f.Kind)), Is.Unique);
    }

    [TestCase("TTFFonts/SourceSans3-Regular.ttf")]
    [TestCase("OTFFonts/SourceSans3-Regular.otf")]
    [TestCase("WoffFonts/Sarabun-Regular.woff2")]
    public void FontLoadsFromBytesAsFromAFile(string path)
    {
        var fromFile = Typeface.LoadFont(path, 3);
        var fromBytes = Typeface.LoadFont(File.ReadAllBytes(path), 3);

        Assert.That(fromBytes.Fonts.Count, Is.EqualTo(fromFile.Fonts.Count));
        Assert.That(fromBytes.GlyphCount, Is.EqualTo(fromFile.GlyphCount));
    }

    [Test]
    public void FeaturesOfTheDefaultLanguageSystemAreAvailable()
    {
        var font = Typeface.LoadFont("OTFFonts/CFF/Quicksand-Regular.otf", 3).GetFont(0);

        Assert.That(font.FeatureCatalog.GPOSFeatures.Select(x => x.Info.Tag), Does.Contain("kern"));
    }

    [Test]
    public void FeatureParametersAreReadInTheFormatOfTheirFeature()
    {
        var font = Typeface.LoadFont("OTFFonts/SourceSans3-Regular.otf", 3).GetFont(0);
        var withParameters = font.FeatureCatalog.GSUBFeatures.Where(x => x.FeatureParameters != null).ToArray();
        var characterVariants = withParameters.Where(x => x.Info.Tag.StartsWith("cv")).ToArray();
        var stylisticSets = withParameters.Where(x => x.Info.Tag.StartsWith("ss")).ToArray();

        Assert.That(characterVariants, Is.Not.Empty);
        Assert.That(stylisticSets, Is.Not.Empty);
        foreach (var feature in characterVariants)
        {
            Assert.That(feature.FeatureParameters.Character, Has.Length.EqualTo(feature.FeatureParameters.CharCount));
            Assert.That(feature.FeatureParameters.Character, Has.All.LessThan(0x110000u), feature.Info.Tag);
        }

        foreach (var feature in stylisticSets)
        {
            Assert.That(feature.FeatureParameters.CharCount, Is.Zero, feature.Info.Tag);
            Assert.That(feature.FeatureParameters.FeatUiLabelNameId, Is.Not.Zero, feature.Info.Tag);
        }
    }

    [Test]
    public void UnregisteredLanguageTagIsKeptUnderItsOwnName()
    {
        var language = LanguageTags.GetMsdnLanguage("CHN ");

        Assert.That(language.Tag, Is.EqualTo("CHN "));
        Assert.That(language.FriendlyName, Is.EqualTo("CHN"));
        Assert.That(LanguageTags.GetMsdnLanguage("CHN "), Is.EqualTo(language));
    }

    [Test]
    public void UnregisteredFeatureTagIsKeptUnderItsOwnName()
    {
        var feature = FeatureInfos.GetFeature("zz99");

        Assert.That(feature.Tag, Is.EqualTo("zz99"));
        Assert.That(feature.RegisteredBy, Is.EqualTo(FeatureRegistration.Unregistered));
        Assert.That(FeatureInfos.GetFeature("zz99"), Is.SameAs(feature));
    }
}
