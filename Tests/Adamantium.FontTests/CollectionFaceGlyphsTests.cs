using System.IO;
using Adamantium.Fonts;
using NUnit.Framework;

namespace Adamantium.FontTests;

/// <summary>A collection whose faces each have glyphs of their own (Apple's Helvetica.ttc): each face draws with its own
/// glyphs, not with those of the face read last; faces sharing their glyphs share one typeface.</summary>
public class CollectionFaceGlyphsTests
{
    private const string Cascadia = "TTFFonts/CascadiaCode-Regular.ttf";
    private const string Adlam = "ScriptFonts/NotoSansAdlam-Regular.ttf";

    [Test]
    public void EachFaceOfACollection_DrawsWithItsOwnGlyphs()
    {
        var cascadia = File.ReadAllBytes(Cascadia);
        var adlam = File.ReadAllBytes(Adlam);
        var collection = Typeface.LoadFont(
            SfntBuilder.Collection(SfntBuilder.TablesOf(cascadia), SfntBuilder.TablesOf(adlam)), 3);
        var alone = Typeface.LoadFont(cascadia, 3).GetFont(0);

        var first = collection.GetFont(0);
        var second = collection.GetFont(1);
        var drawn = first.GetGlyphByUnicode('A');
        var expected = alone.GetGlyphByUnicode('A');

        Assert.Multiple(() =>
        {
            Assert.That(collection.Fonts, Has.Count.EqualTo(2));
            Assert.That(drawn.Index, Is.EqualTo(expected.Index));
            Assert.That(drawn.BoundingRectangle, Is.EqualTo(expected.BoundingRectangle), "the first face's own A");
            Assert.That(first.Typeface.GlyphCount, Is.EqualTo(alone.Typeface.GlyphCount));
            Assert.That(second.Typeface, Is.Not.SameAs(first.Typeface));
        });
    }

    [Test]
    public void FacesSharingTheirGlyphs_ShareOneTypeface()
    {
        var collection = Typeface.LoadFont("OTFFonts/FontCollections/NotoSansCJK-Regular.ttc", 3);

        Assert.That(collection.Fonts, Has.Count.GreaterThan(1));
        Assert.That(collection.Fonts, Has.All.Property(nameof(IFont.Typeface)).SameAs(collection));
    }
}
