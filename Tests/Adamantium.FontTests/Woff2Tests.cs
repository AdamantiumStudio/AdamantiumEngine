using System.IO;
using Adamantium.Fonts;
using NUnit.Framework;

namespace Adamantium.FontTests
{
    public class Woff2Tests
    {
        internal static class WoffFonts
        {
            public static string Sarabun_Regular;
        }

        static Woff2Tests()
        {
            WoffFonts.Sarabun_Regular = Path.Combine("WoffFonts", "Sarabun-Regular.woff2");
        }
        
        [Test]
        public void LoadWoff2Font()
        {
            var typeFace = Typeface.LoadFont(WoffFonts.Sarabun_Regular, 3);
        }
        
        [Test]
        public void LoadWoff2FontCollectionAsana()
        {
            var typeFace = Typeface.LoadFont(Path.Combine("WoffFonts", "Woff2Collection", "ASANA.woff2"), 3);
            
        }
        
        // A transformed hmtx (its side bearings left to the glyphs' left edges) reads as the original: its transformed
        // length went unread, and every table after it was read from the wrong place.
        [Test]
        public void ATransformedHmtx_HasTheOriginalsMetrics()
        {
            var folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "ScriptFonts");
            var original = Typeface.LoadFont(Path.Combine(folder, "AdamantiumMarksTest-Kern2.ttf"), 3).GetFont(0);
            var web = Typeface.LoadFont(Path.Combine(folder, "AdamantiumMarksTest-Kern2.hmtx.woff2"), 3).GetFont(0);

            Assert.That(web.GlyphCount, Is.EqualTo(original.GlyphCount));
            for (uint glyph = 0; glyph < original.GlyphCount; glyph++)
            {
                Assert.That((web.GetAdvanceWidth(glyph), web.GetLeftSideBearing(glyph)),
                    Is.EqualTo((original.GetAdvanceWidth(glyph), original.GetLeftSideBearing(glyph))), $"glyph {glyph}");
            }
        }

        [Test]
        public void LoadWoff2FontCollectionNotoSans()
        {
            var typeFace = Typeface.LoadFont(Path.Combine("WoffFonts", "Woff2Collection", "NotoSansCJK-Regular.woff2"), 3);
            
        }
    }
}