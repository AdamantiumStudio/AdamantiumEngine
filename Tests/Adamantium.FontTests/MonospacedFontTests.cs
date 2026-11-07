using System.IO;
using Adamantium.Fonts;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class MonospacedFontTests
{
    [TestCase("TTFFonts/CascadiaCode-Regular.ttf", true, TestName = "A monospace font says so")]
    [TestCase("ScriptFonts/NotoSansHebrew-Regular.ttf", false, TestName = "A proportional font does not")]
    public void TheFontTellsWhetherItIsMonospaced(string file, bool monospaced)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, file);

        Assert.That(Typeface.LoadFont(path, 3).GetFont(0).IsMonospaced, Is.EqualTo(monospaced));
    }
}
