using System;
using System.Collections.Generic;

namespace Adamantium.Fonts.Tables.CMAP
{
    /// <summary>
    /// High-byte mapping through table: the mixed one- and two-byte codes of Japanese, Chinese and Korean encodings.
    /// A first byte whose key is 0 is a character by itself, mapped through the first subheader; any other is the lead
    /// byte of a two-byte character, mapped through the subheader its key names.
    /// </summary>
    public class CharacterMapFormat2 : CharacterMap
    {
        private const int SubHeaderSize = 8;
        private const int IdRangeOffsetPosition = 6;

        public CharacterMapFormat2()
        {
            SubheaderKeys = new UInt16[256];
        }

        public override UInt16 Format => 2;

        public UInt16 Length { get; set; }

        public UInt16 Language { get; set; }

        /// <summary>For each first byte, its subheader's index times 8.</summary>
        public UInt16[] SubheaderKeys { get; }

        public CharacterMapSubHeader[] SubHeaders { get; set; }

        public UInt16[] GlyphIdArray { get; set; }

        public override uint GetGlyphIndex(uint character)
        {
            if (character > ushort.MaxValue)
            {
                return 0;
            }

            var high = (int)(character >> 8);
            var low = (int)(character & 0xFF);
            int subHeader;
            if (high == 0)
            {
                if (SubheaderKeys[low] != 0)
                {
                    return 0;
                }

                subHeader = 0;
            }
            else
            {
                subHeader = SubheaderKeys[high] / SubHeaderSize;
                if (subHeader == 0)
                {
                    return 0;
                }
            }

            return GlyphOf(subHeader, low);
        }

        public override void CollectUnicodeChars(List<uint> unicodes)
        {
            foreach (var (code, _) in Mappings())
            {
                unicodes.Add(code);
            }
        }

        public override void GetUnicodeToGlyphMappings(Dictionary<uint, uint> unicodeToGlyph)
        {
            foreach (var (code, glyph) in Mappings())
            {
                unicodeToGlyph[code] = glyph;
            }
        }

        private IEnumerable<(uint Code, uint Glyph)> Mappings()
        {
            if (SubHeaders == null || SubHeaders.Length == 0)
            {
                yield break;
            }

            for (var first = 0; first < 256; first++)
            {
                if (SubheaderKeys[first] != 0)
                {
                    continue;
                }

                var glyph = GlyphOf(0, first);
                if (glyph != 0)
                {
                    yield return ((uint)first, glyph);
                }
            }

            for (var high = 1; high < 256; high++)
            {
                var subHeader = SubheaderKeys[high] / SubHeaderSize;
                if (subHeader == 0 || subHeader >= SubHeaders.Length)
                {
                    continue;
                }

                var range = SubHeaders[subHeader];
                var last = Math.Min(255, range.FirstCode + range.EntryCount - 1);
                for (var low = (int)range.FirstCode; low <= last; low++)
                {
                    var glyph = GlyphOf(subHeader, low);
                    if (glyph != 0)
                    {
                        yield return ((uint)(high << 8 | low), glyph);
                    }
                }
            }
        }

        private uint GlyphOf(int subHeader, int low)
        {
            if (SubHeaders == null || subHeader >= SubHeaders.Length || GlyphIdArray == null)
            {
                return 0;
            }

            var range = SubHeaders[subHeader];
            if (low < range.FirstCode || low >= range.FirstCode + range.EntryCount)
            {
                return 0;
            }

            var fromArrayStart = subHeader * SubHeaderSize + IdRangeOffsetPosition + range.IdRangeOffset -
                                 SubHeaders.Length * SubHeaderSize;
            var index = fromArrayStart / 2 + (low - range.FirstCode);
            if (fromArrayStart < 0 || index >= GlyphIdArray.Length)
            {
                return 0;
            }

            var glyph = GlyphIdArray[index];
            return glyph == 0 ? 0u : (ushort)((glyph + range.IdDelta) % 65536);
        }
    }
}
