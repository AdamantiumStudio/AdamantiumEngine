using System.IO;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A layout's revision: whoever keeps a layout that is laid out again in place tells new glyphs from old by it,
/// when the text and the size stay the same.</summary>
[TestFixture]
public class TextLayoutRevisionTests
{
    [Test]
    public void ARevision_GrowsWithEachNewLayout_NotWithARepeatedOne()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };

        Size<double> Lay() => layout.ProcessText("one two three four", 20, new Size<double>(80, double.NaN), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Justify, VerticalTextAlignment.Top);

        Lay();
        var first = layout.Revision;
        Lay();
        Assert.That(layout.Revision, Is.EqualTo(first), "nothing changed, nothing was laid out");

        layout.LastLineAlignment = HorizontalTextAlignment.Right;
        Lay();
        Assert.That(layout.Revision, Is.GreaterThan(first), "the same text and size, laid out anew");
    }
}
