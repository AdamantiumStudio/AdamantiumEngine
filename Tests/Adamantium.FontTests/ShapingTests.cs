using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Shaping;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class ShapingTests
{
    private static readonly ConcurrentDictionary<string, IFont> Fonts = new();

    public static IEnumerable<TestCaseData> ReferenceCases()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Shaping", "expected.txt");
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t');
            yield return new TestCaseData(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5])
                .SetName($"{Path.GetFileNameWithoutExtension(fields[0])} [{fields[1]} {fields[2]} {fields[3]}] {fields[4]}");
        }
    }

    [TestCaseSource(nameof(ReferenceCases))]
    public void ShapesLikeHarfBuzz(string fontPath, string script, string language, string features, string text,
        string expected)
    {
        var font = Fonts.GetOrAdd(fontPath, p => Typeface.LoadFont(p, 3).GetFont(0));
        var vertical = script.EndsWith(":TTB", StringComparison.Ordinal);
        var options = new ShapingOptions(vertical ? script[..^4] : script, language.Length > 0 ? language : null,
            FontFeature.ParseList(features), vertical: vertical);

        var glyphs = TextShaper.Shape(font, Unescape(text), options);

        Assert.That(string.Join(" ", glyphs.Select(g => g.ToString())), Is.EqualTo(expected));
    }

    // A web font shapes as its TrueType original: the WOFF parsers gave their tables no length, and a table read by its
    // length - 'kern', COLR, JSTF - read as empty.
    [TestCase("woff")]
    [TestCase("woff2")]
    public void AWebFont_ShapesAsItsOriginal(string flavor)
    {
        var folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "ScriptFonts");
        var original = Typeface.LoadFont(Path.Combine(folder, "AdamantiumMarksTest-Kern2.ttf"), 3).GetFont(0);
        var web = Typeface.LoadFont(Path.Combine(folder, "AdamantiumMarksTest-Kern2." + flavor), 3).GetFont(0);
        var options = new ShapingOptions("Latn", null, FontFeature.ParseList(""));

        string Shaped(IFont font) => string.Join(" ", TextShaper.Shape(font, "AVATAR To Ao", options).Select(g => g.ToString()));

        Assert.That(Shaped(original), Does.Contain("@-40,0"), "the original is kerned");
        Assert.That(Shaped(web), Is.EqualTo(Shaped(original)));
    }

    public static IEnumerable<TestCaseData> VariedCases()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Shaping", "variations-expected.txt");
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t');
            yield return new TestCaseData(fields[0], fields[4], fields[5], fields[6])
                .SetName($"{Path.GetFileNameWithoutExtension(fields[0])} at {fields[4]}: {fields[5]}");
        }
    }

    // Shaped at axis values: 'FeatureVariations' swaps the lookups a feature applies, and positioning takes its deltas.
    [TestCaseSource(nameof(VariedCases))]
    public void ShapesLikeHarfBuzz_AtAxisValues(string fontPath, string variations, string text, string expected)
    {
        var font = Fonts.GetOrAdd(fontPath, p => Typeface.LoadFont(p, 3).GetFont(0)).GetInstance(variations.Split(',')
            .Select(v => v.Split('='))
            .Select(v => new FontVariation(v[0], float.Parse(v[1], CultureInfo.InvariantCulture)))
            .ToArray());

        var glyphs = TextShaper.Shape(font, Unescape(text), new ShapingOptions("Latn"));

        Assert.That(string.Join(" ", glyphs.Select(g => g.ToString())), Is.EqualTo(expected));
    }

    [TestCase("liga", "liga", 1u, FontFeature.GlobalStart, FontFeature.GlobalEnd)]
    [TestCase("-kern", "kern", 0u, FontFeature.GlobalStart, FontFeature.GlobalEnd)]
    [TestCase("salt=2", "salt", 2u, FontFeature.GlobalStart, FontFeature.GlobalEnd)]
    [TestCase("kern[3:5]=0", "kern", 0u, 3, 5)]
    [TestCase("smcp[2]", "smcp", 1u, 2, 3)]
    [TestCase("ss01[4:]", "ss01", 1u, 4, FontFeature.GlobalEnd)]
    public void ParsesFeatureSyntax(string text, string tag, uint value, int start, int end)
    {
        var feature = FontFeature.Parse(text);

        Assert.That(feature.Tag, Is.EqualTo(tag));
        Assert.That(feature.Value, Is.EqualTo(value));
        Assert.That(feature.Start, Is.EqualTo(start));
        Assert.That(feature.End, Is.EqualTo(end));
    }

    [TestCase("TTFFonts/CascadiaCode-Regular.ttf", "=>", "calt")]
    [TestCase("TTFFonts/PlayfairDisplay-Regular.ttf", "fi", "liga")]
    [TestCase("TTFFonts/CascadiaCode-Regular.ttf", "0", "zero")]
    [TestCase("TTFFonts/SourceSans3-Regular.ttf", "1/2", "frac")]
    public void AFeatureAppliesUnasked_ExactlyWhenItIsADefaultFeature(string fontPath, string text, string tag)
    {
        var font = Fonts.GetOrAdd(fontPath, p => Typeface.LoadFont(p, 3).GetFont(0));
        string Glyphs(string features) => string.Join(" ", TextShaper.Shape(font, text,
            new ShapingOptions(features: FontFeature.ParseList(features))).Select(g => g.ToString()));

        var on = Glyphs(tag);
        var off = Glyphs($"{tag}=0");
        Assert.That(on, Is.Not.EqualTo(off), $"\"{text}\" does not show {tag} in {fontPath}");

        var appliedUnasked = Glyphs("") == on;
        Assert.That(TextShaper.DefaultFeatures.Contains(tag), Is.EqualTo(appliedUnasked));
        Assert.That(FeatureInfos.GetFeature(tag).DefaultFeatureState,
            Is.EqualTo(appliedUnasked ? FeatureState.On : FeatureState.Off), "the registry says what shaping does");
    }

    [TestCase("kern", FeatureState.On)]
    [TestCase("liga", FeatureState.On)]
    [TestCase("locl", FeatureState.On)]
    [TestCase("mark", FeatureState.On)]
    [TestCase("ccmp", FeatureState.On)]
    [TestCase("size", FeatureState.Off)]
    [TestCase("smcp", FeatureState.Off)]
    [TestCase("dlig", FeatureState.Off)]
    public void TheRegistry_SaysWhetherShapingAppliesAFeatureUnasked(string tag, FeatureState expected)
    {
        Assert.That(FeatureInfos.GetFeature(tag).DefaultFeatureState, Is.EqualTo(expected));
    }

    private static string Unescape(string text)
    {
        var result = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '{' && text.AsSpan(i).StartsWith("{U+"))
            {
                var close = text.IndexOf('}', i);
                var code = int.Parse(text.AsSpan(i + 3, close - i - 3), NumberStyles.HexNumber);
                result.Append(char.ConvertFromUtf32(code));
                i = close;
                continue;
            }

            result.Append(text[i]);
        }

        return result.ToString();
    }
}
