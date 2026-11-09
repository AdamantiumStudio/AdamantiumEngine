using System;
using System.Collections.Generic;

namespace Adamantium.Fonts.Tables.CMAP
{
    public abstract class CharacterMap
    {
        public abstract UInt16 Format { get; }
        
        public UInt16 PlatformId { get; set; }
        
        public UInt16 EncodingId { get; set; }

        public abstract uint GetGlyphIndex(uint character);

        public abstract void CollectUnicodeChars(List<uint> unicodes);

        public abstract void GetUnicodeToGlyphMappings(Dictionary<UInt32, UInt32> unicodeToGlyph);

        private const uint LastCodePoint = 0x10FFFF;

        /// <summary>The codes of a range of a table whose ranges run upward: from <paramref name="start"/>, or from
        /// <paramref name="from"/> where an earlier range already covered it, to <paramref name="end"/> or the last
        /// Unicode code point. Damaged ranges that overlap, or end at their type's maximum, then visit each code at most
        /// once in the whole table.</summary>
        protected static IEnumerable<uint> CodesOf(uint start, uint end, uint from)
        {
            var last = Math.Min(end, LastCodePoint);
            for (var code = Math.Max(start, from); code <= last; code++)
            {
                yield return code;
            }
        }

        /// <summary>Where the next range may start after one that ends at <paramref name="end"/>.</summary>
        protected static uint After(uint end, uint from) => Math.Max(from, Math.Min(end, LastCodePoint) + 1);
    }
}