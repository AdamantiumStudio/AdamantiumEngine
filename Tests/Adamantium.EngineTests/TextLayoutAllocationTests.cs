using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>What laying a paragraph out again costs in memory: the glyphs it lays out, not the buffers shaping them.</summary>
[TestFixture]
public class TextLayoutAllocationTests
{
    [Test]
    public void ALongParagraph_LaidOutAgain_AllocatesLittleMoreThanItsGlyphs()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        var font = typeface.Fonts[0];
        var layout = new TextLayout(typeface, font) { Fallback = null };
        var text = string.Concat(Enumerable.Repeat("words of a long paragraph ", 50));
        var attributes = new TextAttributes { Font = font, Language = "en" };

        void Lay(int typed)
        {
            var changed = text.Insert(500, new string('x', typed));
            layout.ProcessText(new AttributedText(changed, attributes), 16, new Size(700, double.NaN),
                TextWrapping.WrapByWords, TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        }

        for (var i = 0; i < 20; i++)
        {
            Lay(i % 3);
        }

        const int layouts = 50;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < layouts; i++)
        {
            Lay(i % 3);
        }

        var perCharacter = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)layouts / text.Length;
        TestContext.Out.WriteLine($"{perCharacter:0} bytes a character");
        Assert.That(perCharacter, Is.LessThan(300), "282 now; 522 while shaping took new buffers for each run");
    }
}
