using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.Core.Fonts;
using Adamantium.Fonts;
using NUnit.Framework;

namespace Adamantium.FontTests;

[TestFixture]
public class FontCollectionTests
{
    private static string Folder(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, name);

    private static FontFace Face(int weight, FontStyle style = FontStyle.Normal, double stretch = 100,
        int minWeight = 0, int maxWeight = 0)
    {
        return new FontFace("Test", "Test", $"{weight} {style} {stretch}", null, new FontWeight(weight), style,
            new FontStretch(stretch), $"{weight}-{style}-{stretch}.ttf", 0,
            minWeight == 0 ? default : new FontWeight(minWeight), maxWeight == 0 ? default : new FontWeight(maxWeight));
    }

    // CSS Fonts 4, 5.2: 400-500 look up to 500, then down, then up; below 400 down then up; above 500 up then down.
    [TestCase(500, new[] { 300, 400, 600, 700 }, 400)]
    [TestCase(450, new[] { 300, 500, 600 }, 500)]
    [TestCase(400, new[] { 300, 600 }, 300)]
    [TestCase(300, new[] { 400, 700 }, 400)]
    [TestCase(300, new[] { 200, 400 }, 200)]
    [TestCase(600, new[] { 400, 700 }, 700)]
    [TestCase(600, new[] { 300, 400 }, 400)]
    [TestCase(700, new[] { 400, 700, 900 }, 700)]
    public void TheWeightIsPickedAsCssPicksIt(int desired, int[] available, int expected)
    {
        var faces = available.Select(w => Face(w)).ToList();

        var match = FontMatcher.Match(faces, new FontWeight(desired), FontStyle.Normal, FontStretch.Normal);

        Assert.That(match.Weight.Value, Is.EqualTo(expected));
    }

    [Test]
    public void AnItalicFallsBackToOblique_ThenToUpright()
    {
        var faces = new[] { Face(400), Face(400, FontStyle.Oblique) };

        Assert.That(FontMatcher.Match(faces, FontWeight.Normal, FontStyle.Italic, FontStretch.Normal).Style,
            Is.EqualTo(FontStyle.Oblique));
        Assert.That(FontMatcher.Match([Face(400)], FontWeight.Normal, FontStyle.Italic, FontStretch.Normal).Style,
            Is.EqualTo(FontStyle.Normal));
    }

    [Test]
    public void TheWidthComesBeforeTheWeight()
    {
        var faces = new[] { Face(700, stretch: 75), Face(400, stretch: 125), Face(400, stretch: 100) };

        var match = FontMatcher.Match(faces, FontWeight.Bold, FontStyle.Normal, FontStretch.SemiCondensed);

        Assert.That(match.Stretch, Is.EqualTo(FontStretch.Condensed), "a narrower width first when asked below normal");
    }

    [Test]
    public void AVariableFace_CoversItsRange()
    {
        var faces = new[] { Face(400, minWeight: 100, maxWeight: 900), Face(700) };

        Assert.That(FontMatcher.Match(faces, new FontWeight(650), FontStyle.Normal, FontStretch.Normal).IsVariable);
        Assert.That(FontMatcher.Match(faces, FontWeight.Bold, FontStyle.Normal, FontStretch.Normal).IsVariable,
            Is.False, "a static face of the exact weight wins over a variable one");
    }

    [Test]
    public void FacesAreReadFromTheirHeaders()
    {
        var bold = FontFaceReader.Read(Path.Combine(Folder("TTFFonts"), "SourceSans3-BoldIt.ttf")).Single();
        var variable = FontFaceReader
            .Read(Path.Combine(Folder("source-sans-3v028R"), "VAR", "SourceSans3VF-Roman.otf")).Single();

        Assert.That(bold.Family, Is.EqualTo("Source Sans 3"));
        Assert.That(bold.Weight, Is.EqualTo(FontWeight.Bold));
        Assert.That(bold.Style, Is.EqualTo(FontStyle.Italic));
        Assert.That(variable.IsVariable);
        Assert.That(variable.MinWeight.Value, Is.LessThan(300));
        Assert.That(variable.MaxWeight.Value, Is.GreaterThanOrEqualTo(900));
    }

    [Test]
    public void AFamilyInAFolder_IsMatchedByWeightAndSlant()
    {
        var collection = new FontCollection();
        collection.AddFolder(Folder("TTFFonts"));

        var semiboldItalic = collection.Match("Source Sans 3", FontWeight.SemiBold, FontStyle.Italic);
        var medium = collection.Match("Source Sans 3", FontWeight.Medium);

        Assert.That(collection.FacesOf("Source Sans 3"), Has.Count.EqualTo(12));
        Assert.That(Path.GetFileName(semiboldItalic.Path), Is.EqualTo("SourceSans3-SemiboldIt.ttf"));
        Assert.That(Path.GetFileName(medium.Path), Is.EqualTo("SourceSans3-Regular.ttf"),
            "500 looks up to 500, then down to 400");
        Assert.That(FontCollection.Load(semiboldItalic).Typeface, Is.Not.Null);
    }

    [Test]
    public void TheIndexIsKeptOnDisk_AndReadBack()
    {
        var cache = Path.Combine(Path.GetTempPath(), $"font-index-{Guid.NewGuid():N}.txt");
        try
        {
            var first = new FontCollection(cache);
            first.AddFolder(Folder("TTFFonts"));
            var second = new FontCollection(cache);
            second.AddFolder(Folder("TTFFonts"));

            Assert.That(File.Exists(cache));
            Assert.That(second.FacesOf("Source Sans 3").Select(f => f.ToString()),
                Is.EquivalentTo(first.FacesOf("Source Sans 3").Select(f => f.ToString())));
        }
        finally
        {
            File.Delete(cache);
        }
    }

    [Test]
    public void SegoeUi_IsFoundByEachOfItsNames()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("Segoe UI is a Windows font");
        }

        var fonts = FontCollection.System;

        Assert.That(Path.GetFileName(fonts.Match("Segoe UI", FontWeight.Bold).Path), Is.EqualTo("segoeuib.ttf").IgnoreCase);
        Assert.That(Path.GetFileName(fonts.Match("Segoe UI Semibold").Path), Is.EqualTo("seguisb.ttf").IgnoreCase);
        Assert.That(Path.GetFileName(fonts.Match("Segoe UI", FontWeight.SemiBold).Path), Is.EqualTo("seguisb.ttf").IgnoreCase);
        Assert.That(fonts.Match("Segoe UI", FontWeight.Normal, FontStyle.Italic).Style, Is.EqualTo(FontStyle.Italic));
    }

    [Test]
    public void TheSecondFontOfACollection_ReadsItsOwnTables()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("SimSun is a Windows font");
        }

        var face = FontCollection.System.Match("NSimSun");
        var font = FontCollection.Load(face);

        Assert.That(face.CollectionIndex, Is.EqualTo(1), "NSimSun is the second font of simsun.ttc");
        Assert.That(font.TryGetGlyphIndex(0x4E2D, out var glyph) && glyph != 0, "its character map, not left out as shared");
        Assert.That(font.GetAdvanceWidth(glyph), Is.GreaterThan(0));
        Assert.That(FontCollection.Load(FontCollection.System.Match("Segoe UI", FontWeight.Bold)).Weight,
            Is.EqualTo(FontWeight.Bold));
    }

    [TestCase("SemiBold", 600)]
    [TestCase("650", 650)]
    [TestCase("regular", 400)]
    public void AWeightIsParsedFromANameOrANumber(string text, int expected)
    {
        Assert.That(FontWeight.Parse(text).Value, Is.EqualTo(expected));
    }

    [TestCase("Condensed", 75)]
    [TestCase("87.5%", 87.5)]
    public void AStretchIsParsedFromANameOrAPercentage(string text, double expected)
    {
        Assert.That(FontStretch.Parse(text).Percent, Is.EqualTo(expected));
    }
}
