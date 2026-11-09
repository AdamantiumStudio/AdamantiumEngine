using System;
using System.Collections.Generic;

namespace Adamantium.Fonts.Tables.CMAP
{
    /// <summary>
    /// Mixed 16-bit and 32-bit coverage: groups of sequential codes as in format 12, with a bit array telling which
    /// 16-bit values start a 32-bit code when text is read as 16-bit units.
    /// </summary>
    public class CharacterMapFormat8 : CharacterMap
    {
        public CharacterMapFormat8()
        {
            Is32 = new byte[8192];
        }

        public override UInt16 Format => 8;

        public UInt16 Reserved { get; set; }

        public UInt32 Length { get; set; }

        public UInt32 Language { get; set; }

        /// <summary>One bit for each 16-bit value, set when it is the first half of a 32-bit code.</summary>
        public byte[] Is32 { get; }

        public UInt32 NumGroups { get; set; }

        /// <summary>The groups, in order of their first code.</summary>
        public SequentialMapGroup[] Groups { get; set; }

        /// <summary>Whether the 16-bit <paramref name="unit"/> starts a 32-bit code.</summary>
        public bool StartsLongCode(ushort unit) => (Is32[unit / 8] & (0x80 >> (unit % 8))) != 0;

        public override uint GetGlyphIndex(uint character)
        {
            if (Groups == null)
            {
                return 0;
            }

            int low = 0, high = Groups.Length - 1;
            while (low <= high)
            {
                var middle = (low + high) / 2;
                var group = Groups[middle];
                if (character < group.StartCharCode)
                {
                    high = middle - 1;
                }
                else if (character > group.EndCharCode)
                {
                    low = middle + 1;
                }
                else
                {
                    return group.StartGlyphId + (character - group.StartCharCode);
                }
            }

            return 0;
        }

        public override void CollectUnicodeChars(List<uint> unicodes)
        {
            var from = 0u;
            foreach (var group in Groups ?? [])
            {
                foreach (var code in CodesOf(group.StartCharCode, group.EndCharCode, from))
                {
                    unicodes.Add(code);
                }

                from = After(group.EndCharCode, from);
            }
        }

        public override void GetUnicodeToGlyphMappings(Dictionary<uint, uint> unicodeToGlyph)
        {
            var from = 0u;
            foreach (var group in Groups ?? [])
            {
                foreach (var code in CodesOf(group.StartCharCode, group.EndCharCode, from))
                {
                    unicodeToGlyph[code] = group.StartGlyphId + (code - group.StartCharCode);
                }

                from = After(group.EndCharCode, from);
            }
        }
    }
}
