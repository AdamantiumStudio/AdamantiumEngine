using Adamantium.Fonts.Text;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class TextColumnsTests
{
    [TestCase("", 0, TestName = "Nothing takes none")]
    [TestCase("abc", 3, TestName = "Latin letters take one each")]
    [TestCase("漢字", 4, TestName = "Ideographs take two each")]
    [TestCase("한글", 4, TestName = "Hangul syllables take two each")]
    [TestCase("ＡＢ", 4, TestName = "Fullwidth letters take two each")]
    [TestCase("ｱｲ", 2, TestName = "Halfwidth katakana takes one each")]
    [TestCase("é", 1, TestName = "A letter with a combining mark takes one")]
    [TestCase("\U0001F44D\U0001F3FD", 2, TestName = "An emoji with a skin tone takes two")]
    [TestCase("❤️", 2, TestName = "A text pictograph asked for as an emoji takes two")]
    [TestCase("❤", 1, TestName = "A text pictograph takes one")]
    [TestCase("\U0001F468‍\U0001F469‍\U0001F467", 2, TestName = "A joined family takes two")]
    [TestCase("a漢b", 4, TestName = "Mixed text adds up")]
    public void Count(string text, int columns) => Assert.That(TextColumns.Count(text), Is.EqualTo(columns));

    [Test]
    public void OfGrapheme_ReadsOnlyTheGrapheme()
    {
        const string text = "a漢";

        Assert.Multiple(() =>
        {
            Assert.That(TextColumns.OfGrapheme(text, 0, 1), Is.EqualTo(1));
            Assert.That(TextColumns.OfGrapheme(text, 1, 2), Is.EqualTo(2));
            Assert.That(TextColumns.OfGrapheme(text, 2, 2), Is.Zero);
        });
    }
}
