using Adamantium.Fonts;
using Adamantium.Fonts.TextureGeneration;
using NUnit.Framework;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Adamantium.Imaging;
using Adamantium.Mathematics;

namespace Adamantium.FontTests
{
    public class MSDFTests
    {
        [Test]
        public void MSDFGenerator()
        {
            var timer = Stopwatch.StartNew();
            var typeface = Typeface.LoadFont(@"OTFFonts/Crimson-Italic.otf", 3);
            var font = typeface.GetFont(0);

            // Dynamic atlas: pack the string's glyphs into a fixed-size atlas and copy their MSDF fields in - the
            // flow the other MSDF tests use. (PrepareTextureAtlas auto-sizes from FontParameters' glyph RANGE, and
            // the default range is empty, so it would yield a 0x0 atlas.)
            var atlasData = new FontAtlasData(64, new Size(1024, 1024));
            var atlasGen = new TextureAtlasGenerator(typeface, font, atlasData, FontParameters.Default());

            var glyphs = font.TranslateIntoGlyphs("Hello string").DistinctBy(x => x.Index).ToArray();
            var textureData = atlasGen.GenerateTextureForGlyphs(glyphs);
            atlasGen.CopyTextureDataToImage(textureData);
            timer.Stop();

            var img = Image.New2D((uint)atlasData.AtlasSize.Width, (uint)atlasData.AtlasSize.Height, SurfaceFormat.R8G8B8A8.UNorm);
            img.GetPixelBuffer(0, 0).SetPixels(atlasData.ImageData);
            img.Save("msdf.png", ImageFileType.Png);

            Assert.Pass($"Atlas data for {glyphs.Length} glyphs was generated in {timer.ElapsedMilliseconds}ms");
        }
        
        [Test]
        public void MSDFGenerator2()
        {
            var atlasData = BuildSampleAtlas();
            SaveAtlasPng(atlasData, atlasData.ImageData, "msdf2.png");
            Assert.Pass($"Atlas {atlasData.AtlasSize.Width}x{atlasData.AtlasSize.Height} generated");
        }

        // Median (coverage) preview — exactly what the shader thresholds at 0.5.
        // Solid glyph interior => white, exterior => black, edges => gray ramp.
        [Test]
        public void MSDF_MedianPreview()
        {
            var atlasData = BuildSampleAtlas();
            var med = (byte[])atlasData.ImageData.Clone();
            for (int i = 0; i < med.Length; i += 4)
            {
                byte r = atlasData.ImageData[i], g = atlasData.ImageData[i + 1], b = atlasData.ImageData[i + 2];
                byte m = System.Math.Max(System.Math.Min(r, g), System.Math.Min(System.Math.Max(r, g), b));
                med[i] = med[i + 1] = med[i + 2] = m;
                med[i + 3] = 255;
            }
            SaveAtlasPng(atlasData, med, "msdf_median.png");
        }

        // Opaque RGB preview: the colored MSDF field lives in RGB, but the alpha channel (true SDF) falls
        // to 0 outside the glyph, so an RGBA viewer shows the cell background as transparent (= black/white
        // depending on the viewer) and the colored field looks "missing". Force alpha opaque to see the
        // actual field that fills the cell — this is the msdf-atlas-gen style view.
        [Test]
        public void MSDF_OpaqueRgbPreview()
        {
            var atlasData = BuildSampleAtlas();
            var rgb = (byte[])atlasData.ImageData.Clone();
            for (int i = 3; i < rgb.Length; i += 4) rgb[i] = 255;
            SaveAtlasPng(atlasData, rgb, "msdf2_rgb.png");
        }

        // Verifies the widened field supports an outline effect, using the SAME median thresholds the
        // RenderMsdfOutline shader pass uses: white body where median >= 0.5, a red ring where median is
        // within OutlineWidth below 0.5 (i.e. just OUTSIDE the contour). A visible ring at a real offset
        // proves the distance field is valid beyond the contour - the whole point of widening PxRange.
        [Test]
        public void MSDF_OutlineVerification()
        {
            var atlasData = BuildSampleAtlas();
            const float outlineW = 0.25f;
            var outline = new byte[atlasData.ImageData.Length];
            for (int i = 0; i < atlasData.ImageData.Length; i += 4)
            {
                float r = atlasData.ImageData[i] / 255f, gg = atlasData.ImageData[i + 1] / 255f, b = atlasData.ImageData[i + 2] / 255f;
                float md = System.Math.Max(System.Math.Min(r, gg), System.Math.Min(System.Math.Max(r, gg), b));
                byte cr = 0, cg = 0, cb = 0;
                if (md >= 0.5f) { cr = cg = cb = 255; }
                else if (md >= 0.5f - outlineW) { cr = 255; }
                outline[i] = cr; outline[i + 1] = cg; outline[i + 2] = cb; outline[i + 3] = 255;
            }
            SaveAtlasPng(atlasData, outline, "msdf2_outline.png");
        }


        // Builds the dynamic atlas for a sample string and copies the RGBA MSDF field into
        // atlasData.ImageData. Shared setup for the atlas-preview tests above.
        private static FontAtlasData BuildSampleAtlas()
        {
            var typeface = Typeface.LoadFont(@"OTFFonts/Crimson-Italic.otf", 3);
            var font = typeface.GetFont(0);
            var atlasData = new FontAtlasData(64, new Size(1024, 1024));
            var atlasGen = new TextureAtlasGenerator(typeface, font, atlasData, FontParameters.Default());
            var glyphs = font.TranslateIntoGlyphs("Hello string").DistinctBy(x => x.Index).ToArray();
            var textureData = atlasGen.GenerateTextureForGlyphs(glyphs);
            atlasGen.CopyTextureDataToImage(textureData);
            return atlasData;
        }

        private static void SaveAtlasPng(FontAtlasData atlasData, byte[] pixels, string path)
        {
            var img = Image.New2D((uint)atlasData.AtlasSize.Width, (uint)atlasData.AtlasSize.Height, SurfaceFormat.R8G8B8A8.UNorm);
            img.GetPixelBuffer(0, 0).SetPixels(pixels);
            img.Save(path, ImageFileType.Png);
        }
    }
}
