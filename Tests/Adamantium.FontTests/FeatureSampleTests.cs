using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Shaping;
using Adamantium.Fonts.Tables.Layout;
using NUnit.Framework;

namespace Adamantium.FontTests;

/// <summary>The characters a feature is shown by: the ones it turns into other glyphs, a few, of one script.</summary>
public class FeatureSampleTests
{
    // Poppins' sets: ss02 changes a and its accented forms, ss01 and ss03 only signs, ss04 four digits.
    [Test]
    public void ASample_IsOneFormOfALetter_AndSignsOnlyWhereNoLetterChanges()
    {
        var font = Typeface.LoadFont("OTFFonts/Poppins-Medium.otf", 3).GetFont(0);

        var samples = FeatureSamples.Of(font, font.FeatureCatalog.GSUBFeatures, 6);

        Assert.Multiple(() =>
        {
            Assert.That(samples["ss01"], Is.EqualTo("!,.:;?"));
            Assert.That(samples["ss02"], Is.EqualTo("a"));
            Assert.That(samples["ss03"], Is.EqualTo("&"));
            Assert.That(samples["ss04"], Is.EqualTo("2369"));
        });
    }

    // Naskh's aalt changes Arabic letters and signs of no script that come before them by code point - guillemets,
    // the Arabic comma: the letters show it.
    [Test]
    public void LettersOfAScript_ShowAFeature_BeforeSignsThatComeFirst()
    {
        var font = Typeface.LoadFont("ScriptFonts/NotoNaskhArabic-Regular.ttf", 3).GetFont(0);

        var sample = FeatureSamples.Of(font, font.FeatureCatalog.GSUBFeatures)["aalt"];

        Assert.That(Characters(sample), Is.EqualTo(new[] { 0x0620, 0x0627, 0x062D, 0x062F }));
    }

    // Source Sans' cv16 changes Ќ, К and к: Ќ comes first by code point, К shows the letter.
    [Test]
    public void ALetter_ComesBeforeItsComposedForms()
    {
        var font = Typeface.LoadFont("OTFFonts/SourceSans3-Regular.otf", 3).GetFont(0);

        var sample = FeatureSamples.Of(font, font.FeatureCatalog.GSUBFeatures)["cv16"];

        Assert.That(Characters(sample), Is.EqualTo(new[] { 0x0416, 0x041A, 0x0436, 0x043A }));
    }

    [TestCase("OTFFonts/SourceSans3-Regular.otf")]
    [TestCase("ScriptFonts/NotoNaskhArabic-Regular.ttf")]
    public void ASample_IsOfOneScript_EachCharacterChangedByTheFeature_EachGlyphOnce(string path)
    {
        var font = Typeface.LoadFont(path, 3).GetFont(0);

        var samples = FeatureSamples.Of(font, font.FeatureCatalog.GSUBFeatures, 6);

        Assert.That(samples, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var (tag, sample) in samples)
            {
                var characters = Characters(sample).ToList();
                Assert.That(characters, Has.Count.InRange(1, 6), tag);
                Assert.That(characters.Select(UnicodeData.GetScript).Where(s => s is not ("Zyyy" or "Zinh")).Distinct()
                    .Count(), Is.LessThanOrEqualTo(1), $"{tag}: {sample}");

                var glyphs = new HashSet<uint>();
                foreach (var c in characters)
                {
                    Assert.That(font.TryGetGlyphIndex(c, out var glyph), Is.True);
                    Assert.That(glyphs.Add(glyph), Is.True, $"{tag}: one glyph once");
                    Assert.That(font.GetGlyphAlternates(glyph).Any(a => a.Feature == tag && a.Value == 1), Is.True,
                        $"{tag} changes U+{c:X4}");
                }
            }
        });
    }

    // The font's own sample text of a character variant is its sample, as the font means it, wherever the feature comes.
    [Test]
    public void AFontsOwnSampleText_IsTheSample()
    {
        var font = Typeface.LoadFont("OTFFonts/Poppins-Medium.otf", 3).GetFont(0);
        var names = new Dictionary<ushort, string> { [256] = "Alternate a", [257] = "aaa" };
        var parameters = new FeatureParametersTable { FeatUiLabelNameId = 256, SampleTextNameId = 257 };
        var variant = new Feature(FeatureInfos.GetFeature("cv01"), FeatureKind.GSUB, parameters,
            id => names.TryGetValue(id, out var text) ? text : null);
        var bare = new Feature(FeatureInfos.GetFeature("cv01"), FeatureKind.GSUB, null, _ => null);

        Assert.Multiple(() =>
        {
            Assert.That(FeatureSamples.Of(font, [variant])["cv01"], Is.EqualTo("aaa"));
            Assert.That(FeatureSamples.Of(font, [bare, variant])["cv01"], Is.EqualTo("aaa"));
            Assert.That(FeatureSamples.Of(font, [variant, bare])["cv01"], Is.EqualTo("aaa"));
        });
    }

    // The characters a variant names come first: à, and a after it is the same letter.
    [Test]
    public void TheCharactersAVariantNames_ComeFirst()
    {
        var font = Typeface.LoadFont("OTFFonts/Poppins-Medium.otf", 3).GetFont(0);
        var parameters = new FeatureParametersTable { Character = [0x00E0] };
        var named = new Feature(FeatureInfos.GetFeature("ss02"), FeatureKind.GSUB, parameters, _ => null);

        Assert.That(FeatureSamples.Of(font, [named])["ss02"], Is.EqualTo("à"));
    }

    [Test]
    public void ASampleOfNoCharacter_IsRefused()
    {
        var font = Typeface.LoadFont("OTFFonts/Poppins-Medium.otf", 3).GetFont(0);

        Assert.Throws<ArgumentOutOfRangeException>(() => FeatureSamples.Of(font, font.FeatureCatalog.GSUBFeatures, 0));
    }

    private static IEnumerable<int> Characters(string text)
    {
        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
        {
            yield return char.ConvertToUtf32(text, i);
        }
    }
}
