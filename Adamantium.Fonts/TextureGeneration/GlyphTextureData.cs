using Adamantium.Mathematics;

namespace Adamantium.Fonts.TextureGeneration
{
    public class GlyphTextureData
    {
        public GlyphTextureData()
        {

        }

        public GlyphTextureData(uint width, uint height, uint glyphIndex, uint margin, char character, uint cellSize = 0)
        {
            Character = character;
            BoundingRect.Width = (int)width;
            BoundingRect.Height = (int)height;
            GlyphIndex = glyphIndex;
            Key = glyphIndex;
            Margin = margin;
            // cellSize > 0: the body (width x height) sits centered inside a fixed square cell and the field
            // is computed across the whole cell. Otherwise fall back to a tight body + margin bitmap.
            FullGlyphSize = cellSize > 0
                ? new Size<double>(cellSize, cellSize)
                : new Size<double>(width + (Margin * 2), height + (Margin * 2));
            Pixels = new byte[(int)FullGlyphSize.Width * (int)FullGlyphSize.Height * 4];
        }
        
        public char Character { get; set; }

        public Rectangle<int> BoundingRect;

        public RectangleF UVRect;

        // UV of the WHOLE cell (body + margin), not just the body. Effects that reach outside the contour
        // (outline/glow/shadow) sample this so the field in the margin is available; plain text can keep
        // using UVRect (body only).
        public RectangleF UVRectFull;

        public uint GlyphIndex { get; }

        /// <summary>The glyph's name in its atlas: its index in an atlas of one font, its typeface and index in an atlas
        /// several fonts share (<see cref="KeyOf"/>).</summary>
        public ulong Key { get; set; }

        /// <summary>The key of glyph <paramref name="glyphIndex"/> of <paramref name="typeface"/> in a shared atlas, by the
        /// outlines it draws (<see cref="Typeface.OutlinesId"/>): instances that vary only metrics and colors share
        /// their font's cells.</summary>
        public static ulong KeyOf(Typeface typeface, uint glyphIndex) => (ulong)(uint)typeface.OutlinesId << 32 | glyphIndex;

        public byte[] Pixels { get; set; }

        public bool IsEmpty => Pixels == null || Pixels.Length == 0;
        
        public uint Margin { get; }

        public Size<double> FullGlyphSize { get; }
        
        public uint IndexInAtlas { get; set; }
        
        public uint DepthLayer { get; set; }

        public void CalculateUV(Size<double> atlasSize)
        {
            // The body is centered inside its (FullGlyphSize) cell; the UV must point at the body, not the
            // whole cell. The centering offset (cell - body)/2 reduces to Margin in the tight body+margin
            // layout, so this stays correct for both layouts.
            var padX = ((int)FullGlyphSize.Width - BoundingRect.Width) / 2;
            var padY = ((int)FullGlyphSize.Height - BoundingRect.Height) / 2;
            UVRect = Rectangle.CreateFromCorners(
                (float)((BoundingRect.Left + padX) / atlasSize.Width),
                (float)((BoundingRect.Top + padY) / atlasSize.Height),
                (float)((BoundingRect.Left + padX + BoundingRect.Width) / atlasSize.Width),
                (float)((BoundingRect.Top + padY + BoundingRect.Height) / atlasSize.Height));

            // Whole-cell UV: the full bitmap (body + margin) is uploaded with its top-left at BoundingRect
            // (see FontAtlas.ProcessTextureData), so it spans [Left, Left+FullWidth) x [Top, Top+FullHeight).
            UVRectFull = Rectangle.CreateFromCorners(
                (float)(BoundingRect.Left / atlasSize.Width),
                (float)(BoundingRect.Top / atlasSize.Height),
                (float)((BoundingRect.Left + (int)FullGlyphSize.Width) / atlasSize.Width),
                (float)((BoundingRect.Top + (int)FullGlyphSize.Height) / atlasSize.Height));
        }

        public override string ToString()
        {
            return $"Index: {GlyphIndex}, Related Char: {Character}, Rect: {BoundingRect}";
        }
    }
}
