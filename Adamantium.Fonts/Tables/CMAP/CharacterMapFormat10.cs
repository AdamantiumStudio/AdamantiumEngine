using System;
using System.Collections.Generic;

namespace Adamantium.Fonts.Tables.CMAP
{
    /// <summary>
    /// Trimmed array: one glyph for each code of a single run of 32-bit codes from <see cref="StartCharCode"/>. Little
    /// used; suited to fonts covering one range of the supplementary planes.
    /// </summary>
    public class CharacterMapFormat10 : CharacterMap
    {
        public override UInt16 Format => 10;

        public UInt16 Reserved { get; set; }

        public UInt32 Length { get; set; }

        public UInt32 Language { get; set; }

        public UInt32 StartCharCode { get; set; }

        public UInt32 NumChars { get; set; }

        public UInt16[] GlyphIdArray { get; set; }

        public override uint GetGlyphIndex(uint character)
        {
            if (GlyphIdArray == null || character < StartCharCode)
            {
                return 0;
            }

            var index = (long)character - StartCharCode;
            return index < GlyphIdArray.Length ? GlyphIdArray[index] : 0u;
        }

        public override void CollectUnicodeChars(List<uint> unicodes)
        {
            foreach (var code in Codes())
            {
                unicodes.Add(code);
            }
        }

        public override void GetUnicodeToGlyphMappings(Dictionary<uint, uint> unicodeToGlyph)
        {
            foreach (var code in Codes())
            {
                unicodeToGlyph[code] = GetGlyphIndex(code);
            }
        }

        private IEnumerable<uint> Codes()
        {
            if (GlyphIdArray == null || GlyphIdArray.Length == 0)
            {
                return [];
            }

            var last = (ulong)StartCharCode + (ulong)GlyphIdArray.Length - 1;
            return CodesOf(StartCharCode, (uint)Math.Min(last, uint.MaxValue), 0);
        }
    }
}
