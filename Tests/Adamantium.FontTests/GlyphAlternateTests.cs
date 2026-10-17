using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Shaping;
using NUnit.Framework;

namespace Adamantium.FontTests;

/// <summary>A font's 'GSUB' features as a panel of OpenType features and a glyph panel see them - names, values and
/// alternates - against HarfBuzz (Features/expected.txt, written by Adamantium.ShapingOracle).</summary>
public class GlyphAlternateTests
{
    private static readonly ConcurrentDictionary<string, IFont> Fonts = new();

    public static IEnumerable<TestCaseData> ReferenceFonts()
    {
        foreach (var line in File.ReadAllLines(Path.Combine(TestContext.CurrentContext.TestDirectory, "Features", "cases.txt")))
        {
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                yield return new TestCaseData(line).SetName(Path.GetFileName(line));
            }
        }
    }

    [TestCaseSource(nameof(ReferenceFonts))]
    public void FeatureNames_AreHarfBuzzs(string fontPath)
    {
        var font = Load(fontPath);
        var lines = Expected(fontPath, "F");
        var expected = lines.Where(fields => fields.Length > 3).Select(fields => string.Join("\t", fields.Skip(2)))
            .Distinct().OrderBy(text => text).ToArray();
        var actual = font.FeatureCatalog.GSUBFeatures.Where(feature => feature.FeatureParameters != null)
            .Select(Describe).OrderBy(text => text).ToArray();

        Assert.That(font.FeatureCatalog.GSUBFeatures.Select(feature => feature.Info.Tag),
            Is.SupersetOf(lines.Select(fields => fields[2]).Distinct()), "every feature HarfBuzz finds");
        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCaseSource(nameof(ReferenceFonts))]
    public void GlyphAlternates_AreHarfBuzzs(string fontPath)
    {
        var font = Load(fontPath);
        var expected = new HashSet<string>();
        foreach (var fields in Expected(fontPath, "A"))
        {
            var alternates = fields[5].Split(',');
            for (var i = 0; i < alternates.Length; i++)
            {
                expected.Add($"{fields[4]} {fields[2]}={i + 1} {alternates[i]}");
            }
        }

        var actual = new HashSet<string>();
        for (var glyph = 0u; glyph < font.Typeface.GlyphCount; glyph++)
        {
            foreach (var alternate in font.GetGlyphAlternates(glyph))
            {
                actual.Add($"{glyph} {alternate.Feature}={alternate.Value} {alternate.Glyph}");
            }
        }

        Assert.That(actual.Except(expected).Take(10), Is.Empty, "alternates HarfBuzz does not offer");
        Assert.That(expected.Except(actual).Take(10), Is.Empty, "alternates HarfBuzz offers and we do not");
    }

    [TestCaseSource(nameof(ReferenceFonts))]
    public void AFeaturesValues_AreItsMostAlternates(string fontPath)
    {
        var font = Load(fontPath);
        var most = Expected(fontPath, "A").GroupBy(fields => fields[2])
            .ToDictionary(group => group.Key, group => group.Max(fields => fields[5].Split(',').Length));

        foreach (var feature in font.FeatureCatalog.GSUBFeatures)
        {
            Assert.That(feature.ValueCount, Is.EqualTo(most.TryGetValue(feature.Info.Tag, out var count) ? count : 1),
                feature.Info.Tag);
        }
    }

    [Test]
    public void AStylisticSet_IsCalledWhatTheFontCallsIt_AndOtherFeaturesByTheirRegisteredName()
    {
        var font = Load("OTFFonts/Poppins-Medium.otf");
        var catalog = font.FeatureCatalog;

        Assert.That(catalog.GSUBFeatures.First(f => f.Info.Tag == "ss02").Name, Is.EqualTo("Double-storey a"));
        Assert.That(catalog.GSUBFeatures.First(f => f.Info.Tag == "nukt").Name, Is.EqualTo("Nukta Forms"));
        Assert.That(font.GetName(1), Is.EqualTo(font.FontFamily));
        Assert.That(font.GetName(60000), Is.Null);
    }

    [Test]
    public void ACharacterVariant_TakesItsTooltipSampleVariantNamesAndCharactersFromTheFont()
    {
        var names = new Dictionary<ushort, string>
        {
            [256] = "Alternate a", [257] = "Tooltip", [258] = "aaa", [259] = "Single-storey", [260] = "Hooked",
        };
        var parameters = new Adamantium.Fonts.Tables.Layout.FeatureParametersTable
        {
            FeatUiLabelNameId = 256, FeatUiTooltipTextNameId = 257, SampleTextNameId = 258, NumNamedParameters = 3,
            FirstParamUiLabelNameId = 259, Character = [0x61, 0x1F600],
        };

        var feature = new Feature(FeatureInfos.GetFeature("cv01"), FeatureKind.GSUB, parameters,
            id => names.TryGetValue(id, out var text) ? text : null);

        Assert.That(feature.Name, Is.EqualTo("Alternate a"));
        Assert.That(feature.Tooltip, Is.EqualTo("Tooltip"));
        Assert.That(feature.SampleText, Is.EqualTo("aaa"));
        Assert.That(feature.ParameterLabels, Is.EqualTo(new[] { "Single-storey", "Hooked", "" }), "a missing name is empty");
        Assert.That(feature.Characters, Is.EqualTo(new[] { 0x61, 0x1F600 }));
    }

    [Test]
    public void AGlyph_StandsForItsCharacter_AndAnAlternateForTheCharacterItReplaces()
    {
        var font = Load("OTFFonts/Poppins-Medium.otf");
        var a = font.GetGlyphByUnicode('a').Index;
        var doubleStorey = font.GetGlyphAlternates(a).First(alternate => alternate.Feature == "ss02");

        Assert.That(font.GetGlyphText(a), Is.EqualTo(new[] { "a" }));
        Assert.That(font.GetGlyphText(doubleStorey.Glyph), Does.Contain("a"));
    }

    [Test]
    public void ALigature_StandsForTheCharactersItJoins()
    {
        var font = Load("TTFFonts/PlayfairDisplay-Regular.ttf");
        var shaped = TextShaper.Shape(font, "fi", new ShapingOptions(features: FontFeature.ParseList("liga")));

        Assert.That(shaped, Has.Length.EqualTo(1), "Playfair joins fi");
        Assert.That(font.GetGlyphText(shaped[0].GlyphIndex), Does.Contain("fi"));
    }

    // Roboto Flex swaps its 'rvrn' lookups by weight: an instance gives what its own shaping substitutes.
    [Test]
    public void AnInstancesAlternates_FollowItsAxisValues()
    {
        var font = Load("Variations/RobotoFlex-Variable.ttf");
        var heavy = font.GetInstance([new FontVariation("wght", 1000)]);
        var dollar = font.GetGlyphByCharacter('$').Index;
        var shaped = TextShaper.Shape(heavy, "$")[0].GlyphIndex;
        Assume.That(shaped, Is.Not.EqualTo(dollar), "the heavy instance substitutes the dollar");

        Assert.That(heavy.GetGlyphAlternates(dollar, "rvrn", '$', null), Does.Contain(shaped));
        Assert.That(font.GetGlyphAlternates(dollar, "rvrn", '$', null), Does.Not.Contain(shaped));
    }

    private static IFont Load(string fontPath) => Fonts.GetOrAdd(fontPath, p => Typeface.LoadFont(p, 3).GetFont(0));

    private static List<string[]> Expected(string fontPath, string kind) =>
        File.ReadAllLines(Path.Combine(TestContext.CurrentContext.TestDirectory, "Features", "expected.txt"))
            .Select(line => line.Split('\t'))
            .Where(fields => fields.Length > 2 && fields[0] == fontPath && fields[1] == kind)
            .ToList();

    private static string Describe(Feature feature) =>
        string.Join("\t", feature.Info.Tag, Escape(feature.Label), Escape(feature.Tooltip), Escape(feature.SampleText),
            string.Join(";", feature.ParameterLabels.Select(Escape)),
            string.Join(",", feature.Characters.Select(c => c.ToString("X4"))));

    private static string Escape(string text) => (text ?? "").Replace("\\", "\\\\").Replace("\t", "\\t")
        .Replace("\n", "\\n").Replace("\r", "\\r").Replace(";", "\\;");
}
