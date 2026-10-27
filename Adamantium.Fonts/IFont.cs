using System;
using System.Collections.Generic;
using Adamantium.Core.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Mathematics;

namespace Adamantium.Fonts
{
    public interface IFont
    {
        #region Name
        
        string Copyright { get; }                
        string FontFamily { get; }              
        string FontSubfamily { get; }            
        string UniqueId { get; }
        string FullName { get; }
        string Version { get; }           
        string Trademark { get; }                
        string Manufacturer { get; }             
        string Designer { get; }                 
        string Description { get; }             
        string VendorUrl { get; }
        string DesignerUrl { get; }
        string LicenseDescription { get; }      
        string LicenseInfoUrl { get; }          
        string TypographicFamilyName { get; }   
        string TypographicSubfamilyName { get; }
        
        /// <summary>
        /// // WWS - weight, width, slope
        /// </summary>
        string WwsFamilyName { get; }
        
        /// <summary>
        /// // WWS - weight, width, slope
        /// </summary>
        string WwsSubfamilyName { get; }        
        string LightBackgroundPalette { get; }  
        string DarkBackgroundPalette { get; }
        
        #endregion
        
        /// <summary>The typeface (the file) this font was loaded from; a collection holds several fonts.</summary>
        public Typeface Typeface { get; }

        /// <summary>How heavy the font is ('OS/2' usWeightClass).</summary>
        public FontWeight Weight { get; }

        /// <summary>Upright, italic or oblique ('OS/2' fsSelection).</summary>
        public FontStyle Style { get; }

        /// <summary>How wide the font is ('OS/2' usWidthClass).</summary>
        public FontStretch Stretch { get; }

        /// <summary>How far the pen moves after a glyph, in font units, from this font's own 'hmtx': the fonts of a
        /// collection share outlines, not metrics.</summary>
        public ushort GetAdvanceWidth(uint glyphIndex);

        /// <summary>The space left of a glyph's outline, in font units, from this font's own 'hmtx'.</summary>
        public short GetLeftSideBearing(uint glyphIndex);

        /// <summary>Whether the font has vertical metrics ('vmtx') for setting text top to bottom.</summary>
        public bool HasVerticalMetrics { get; }

        /// <summary>How far a glyph moves the pen down in vertical text: its 'vmtx' advance, the font's ascender
        /// less its descender without one.</summary>
        public ushort GetAdvanceHeight(uint glyphIndex);

        /// <summary>The height above the baseline of a glyph's vertical origin - where the pen stands in vertical
        /// text - as HarfBuzz finds it: from 'VORG', else the glyph's top plus its 'vmtx' top side bearing, else the
        /// glyph centered in the line's height.</summary>
        public short GetVerticalOriginY(uint glyphIndex);

        /// <summary>The glyphs the font's justification table ('JSTF') gives a script for stretching its words, as
        /// Arabic's kashida, by OpenType script tag (<c>arab</c>); empty when it gives none.</summary>
        public IReadOnlyList<uint> GetJustificationExtenders(string scriptTag);

        /// <summary>The axes this font varies along ('fvar'); empty for a font that does not vary.</summary>
        public IReadOnlyList<FontAxis> Axes { get; }

        /// <summary>The styles a variable font names by their axis values ('fvar'), as "Bold Condensed".</summary>
        public IReadOnlyList<FontNamedInstance> NamedInstances { get; }

        /// <summary>The names the style attributes table ('STAT') gives axis values, as "Light" or "Condensed".</summary>
        public IReadOnlyList<FontAxisValue> AxisValues { get; }

        /// <summary>The style name for when every axis value's name is left out, as "Regular"; null when 'STAT' gives
        /// none.</summary>
        public string ElidedFallbackName { get; }

        /// <summary>The axis values of an instance (<see cref="GetInstance"/>); empty for a font as its file has it.</summary>
        public IReadOnlyList<FontVariation> Variations { get; }

        /// <summary>This variable font at the axis values given, with its own outlines and advances; an axis not given
        /// takes its default, a value outside its axis is clamped. The same values give the same instance; a font that
        /// does not vary, or values that are all defaults, give this font.</summary>
        public IFont GetInstance(IReadOnlyList<FontVariation> variations);

        /// <summary>This variable font at a point on the way from one set of axis values to another, as an animation
        /// passes it, <paramref name="progress"/> 0 at the first and 1 at the second: laid out at exactly the values
        /// there, and drawn between the two key instances around it (<see cref="Blend"/>), whose glyphs are kept and
        /// serve every animation that passes them. The point itself is not kept as an instance is.</summary>
        public IFont GetInstance(IReadOnlyList<FontVariation> from, IReadOnlyList<FontVariation> to, float progress);

        /// <summary>The key instances a font on the way between two sets of axis values is drawn between; null for a
        /// font drawn as itself.</summary>
        public FontBlend Blend { get; }

        /// <summary>This font at the optical size of text set at <paramref name="size"/>, its other axes kept, as CSS's
        /// <c>font-optical-sizing: auto</c> sets 'opsz'; the font itself when it has no 'opsz' axis or its 'opsz' was
        /// asked for by value.</summary>
        public IFont AtOpticalSize(float size);

        /// <summary>The layers a color glyph is drawn as, bottom first, each an ordinary glyph in a color of the font's
        /// first palette ('COLR' and 'CPAL'); empty for a glyph drawn as its own outline.</summary>
        public IReadOnlyList<ColorLayer> GetColorLayers(uint glyphIndex);

        /// <summary>The layers of a color glyph as <see cref="GetColorLayers(uint)"/> gives them, in the font's palette
        /// <paramref name="palette"/>; a palette the font lacks gives its first.</summary>
        public IReadOnlyList<ColorLayer> GetColorLayers(uint glyphIndex, int palette);

        /// <summary>The palettes the font's color glyphs can be drawn in ('CPAL'); empty for a font without.</summary>
        public IReadOnlyList<ColorPalette> ColorPalettes { get; }

        /// <summary>A color glyph's paint graph ('COLR' version 1) as the steps that draw it, in the font's first palette;
        /// empty for a glyph without one. A font may have layers as well: this is the glyph's richer form.</summary>
        public IReadOnlyList<ColorPaintOperation> GetColorPaint(uint glyphIndex);

        /// <summary>A color glyph's paint graph as <see cref="GetColorPaint(uint)"/> gives it, in the font's palette
        /// <paramref name="palette"/>; a palette the font lacks gives its first.</summary>
        public IReadOnlyList<ColorPaintOperation> GetColorPaint(uint glyphIndex, int palette);

        /// <summary>The box a 'COLR' version 1 glyph is drawn within (its clip list), in font units, at the font's axis
        /// values; false when the font gives none for the glyph.</summary>
        public bool TryGetColorClipBox(uint glyphIndex, out RectangleF box);

        /// <summary>The sizes, in pixels per em, the font has color images at ('CBDT' or 'sbix'); empty for a font
        /// without.</summary>
        public IReadOnlyList<int> ColorBitmapSizes { get; }

        /// <summary>A glyph's color image at the size nearest <paramref name="pixelsPerEm"/> from above, or the largest
        /// when none is as large (0 asks for the largest), as HarfBuzz chooses it; null when that size has no image of
        /// the glyph, or it is not a PNG.</summary>
        public ColorBitmap GetColorBitmap(uint glyphIndex, int pixelsPerEm);

        /// <summary>The OpenType features this font offers, per script and language system.</summary>
        public FeatureCatalog FeatureCatalog { get; }

        /// <summary>The string the 'name' table holds under <paramref name="nameId"/> - English when there is a choice -
        /// or null when it holds none: the labels of stylistic sets and character variants, for one.</summary>
        public string GetName(ushort nameId);

        /// <summary>Every glyph a single or alternate substitution of a 'GSUB' feature turns this glyph into, with the
        /// feature and value that ask for it - what a glyph panel offers for a character.</summary>
        public IReadOnlyList<GlyphAlternate> GetGlyphAlternates(uint glyphIndex);

        /// <summary>The glyphs a 'GSUB' feature turns this glyph into in text of the script of
        /// <paramref name="codepoint"/> in <paramref name="language"/>: from that feature of the language system a shaper
        /// takes for them, none when the system has no such feature - so 'jalt' gives Hebrew's wide letters to Hebrew
        /// text only.</summary>
        public IReadOnlyList<uint> GetGlyphAlternates(uint glyphIndex, string feature, int codepoint, string language);

        /// <summary>The texts this glyph stands for: its characters from 'cmap', those of the glyphs a substitution
        /// makes it from, and the joined characters of a ligature - so a glyph put in by hand can be copied as text.</summary>
        public IReadOnlyList<string> GetGlyphText(uint glyphIndex);
        
        public IReadOnlyCollection<uint> Unicodes { get; }
        
        public uint GlyphCount { get; }
        
        public ushort UnitsPerEm { get; }
        
        public Int16 Ascender { get; } 
        
        public Int16 Descender { get; }
        
        public Int16 CapsHeight { get; }
        
        /// <summary>How far a line reaches above its baseline, in font units: the typographic ascender when the font
        /// asks for its typographic metrics (OS/2 USE_TYPO_METRICS), otherwise the horizontal header's ('hhea').</summary>
        public Int16 LineAscent { get; }

        /// <summary>How far a line reaches below its baseline, in font units, positive; from the same table as
        /// <see cref="LineAscent"/>.</summary>
        public Int16 LineDescent { get; }

        /// <summary>The space the font adds between lines, in font units, from the same table as
        /// <see cref="LineAscent"/>. A line is <see cref="LineAscent"/> + <see cref="LineDescent"/> + this tall, with half
        /// of this above the ascent and half below the descent.</summary>
        public Int16 LineGap { get; }

        /// <summary>Top of the underline relative to the baseline, upward positive, in font units ('post').</summary>
        public Int16 UnderlinePosition { get; }

        public Int16 UnderlineThickness { get; }

        /// <summary>Top of the strikeout line above the baseline, in font units ('OS/2').</summary>
        public Int16 StrikeoutPosition { get; }

        public Int16 StrikeoutSize { get; }

        /// <summary>How far above the baseline the font sets a superscript, in font units ('OS/2').</summary>
        public Int16 SuperscriptYOffset { get; }

        /// <summary>How far below the baseline the font sets a subscript, in font units, positive ('OS/2').</summary>
        public Int16 SubscriptYOffset { get; }
        /// <summary>
        /// smallest readable size in pixels
        /// </summary>
        public UInt16 LowestRecPPEM { get; }

        /// <summary>
        /// space between lines
        /// </summary>
        public Double LineSpacingMultiplier { get; }

        public DateTime Created { get; }

        public DateTime Modified { get; }

        public IReadOnlyCollection<Glyph> Glyphs { get; }

        internal void UpdateGlyphNamesCache();

        internal void SetGlyphUnicodes(Dictionary<uint, List<uint>> glyphMapping);

        internal OpenTypeLayout Layout { get; }

        internal float[] NormalizedCoordinates { get; }

        /// <summary>The glyph the font maps <paramref name="codepoint"/> to; false when it has none.</summary>
        public bool TryGetGlyphIndex(int codepoint, out uint glyphIndex);

        /// <summary>The glyph the font maps <paramref name="codepoint"/> followed by <paramref name="variationSelector"/>
        /// to ('cmap' format 14): a glyph of the sequence's own, or the character's usual glyph for a default sequence;
        /// false when the font does not support the sequence.</summary>
        public bool TryGetGlyphIndex(int codepoint, int variationSelector, out uint glyphIndex);

        IReadOnlyList<Glyph> TranslateIntoGlyphs(string input);

        Glyph GetGlyphByIndex(uint index);

        Glyph GetGlyphByName(string name);
        
        Glyph GetGlyphByUnicode(uint unicode);

        Glyph GetGlyphByCharacter(char character);

        public Int16 GetKerningValue(UInt16 leftGlyphIndex, UInt16 rightGlyphIndex);
    }
}