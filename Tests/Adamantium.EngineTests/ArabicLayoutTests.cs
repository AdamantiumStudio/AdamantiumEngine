using System.IO;
using System.Linq;
using Adamantium.Core.Fonts.Shaping;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Arabic laid out: letters join across the runs a text is cut into, and the caret stands between the letters
/// of a ligature.</summary>
[TestFixture]
public class ArabicLayoutTests
{
    private static TextLayout NewLayout()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansArabic-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        return new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
    }

    private static uint[] Glyphs(TextLayout layout) =>
        layout.GetTextData().OrderBy(g => g.PositionInString).Select(g => g.Glyph.Index).ToArray();

    // A run of its own for the second letter (its own features) must not break the word: each letter keeps the form it
    // has among its neighbours.
    [Test]
    public void AWordCutIntoRuns_JoinsAsAWhole()
    {
        const string word = "محمد";
        var whole = NewLayout();
        whole.ProcessText(word, 20, new Size(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var cut = NewLayout();
        cut.ProcessText(new AttributedText(word).Apply(1, 1, new TextAttributes { Features = FontFeature.ParseList("liga=0") }),
            20, new Size(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None, HorizontalTextAlignment.Left,
            VerticalTextAlignment.Top);

        Assert.That(Glyphs(cut), Is.EqualTo(Glyphs(whole)));
    }

    // Joined letters, ligatures among them, still take the caret between each two, right to left.
    [TestCase("لا")]
    [TestCase("الله")]
    [TestCase("السلام")]
    public void TheCaretStopsBetweenEveryTwoLettersOfAJoinedWord(string word)
    {
        var layout = NewLayout();
        layout.ProcessText(word, 20, new Size(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.GetCaretStops().Select(s => s.X), Is.Ordered.Descending.And.Unique);
    }
}
