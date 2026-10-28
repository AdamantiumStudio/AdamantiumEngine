using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.TextureGeneration;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.FontTests;

// The atlas rasterizes the letters a frame asks for as one batch. One glyph that threw took the whole batch down with
// it, and every letter of the text it came with went missing - a whole editor drew blank for one broken "w".
public class AtlasBatchFailureTests
{
    [Test]
    public void AGlyphThatCannotBeRasterized_DoesNotHoldBackTheRestOfItsBatch()
    {
        var typeface = Typeface.LoadFont("TTFFonts/SourceSans3-Regular.ttf", 3);
        var font = typeface.GetFont(0);
        var parameters = FontParameters.Default(32);
        var generator = new TextureAtlasGenerator(null, null, new FontAtlasData(32, new Size<double>(1024, 1024), 8), parameters);
        var letters = font.TranslateIntoGlyphs("Helo").ToList();
        var broken = OpenContour(parameters.SampleRate);
        var batch = letters.Select(x => (font, x)).Append((font, broken)).ToList();

        var data = generator.GenerateTextureForGlyphs(batch);

        Assert.That(data.Select(x => x.GlyphIndex), Is.EquivalentTo(letters.Select(x => x.Index)));
        Assert.That(typeface.ErrorMessages, Has.Some.Contains($"Glyph {broken.Index} could not be rasterized"));
    }

    private static Glyph OpenContour(byte sampleRate)
    {
        var glyph = new Glyph(60000, OutlineType.TrueType) { BoundingRectangle = Rectangle.CreateFromCorners<int>(0, 0, 10, 100) };
        glyph.SetOutlinesForRate(sampleRate,
            [new SampledOutline([new LineSegment2D(new Vector2<double>(0, 0), new Vector2<double>(0, 100))])]);
        return glyph;
    }
}
