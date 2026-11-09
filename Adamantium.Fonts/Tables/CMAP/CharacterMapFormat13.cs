using System;
using System.Collections.Generic;
using System.Linq;

namespace Adamantium.Fonts.Tables.CMAP
{
    public class CharacterMapFormat13 : CharacterMap
    {
        public override UInt16 Format => 13;
        
        public UInt16 Reserved { get; set; }

        public UInt32 Length { get; set; }

        public UInt32 Language { get; set; }
        
        public UInt32 NumGroups { get; set; }
        
        public ConstantMapGroup[] Groups { get; set; }
        
        public override uint GetGlyphIndex(uint character)
        {
            var low = 0;
            var high = Groups.Length - 1;
            while (low <= high)
            {
                var middle = (low + high) / 2;
                if (character < Groups[middle].StartCharCode)
                {
                    high = middle - 1;
                }
                else if (character > Groups[middle].EndCharCode)
                {
                    low = middle + 1;
                }
                else
                {
                    return Groups[middle].GlyphId;
                }
            }

            return 0;
        }

        public override void CollectUnicodeChars(List<uint> unicodes)
        {
            var from = 0u;
            foreach (var group in Groups.OrderBy(g => g.StartCharCode))
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
            foreach (var group in Groups.OrderBy(g => g.StartCharCode))
            {
                foreach (var code in CodesOf(group.StartCharCode, group.EndCharCode, from))
                {
                    unicodeToGlyph[code] = group.GlyphId;
                }

                from = After(group.EndCharCode, from);
            }
        }
    }
}