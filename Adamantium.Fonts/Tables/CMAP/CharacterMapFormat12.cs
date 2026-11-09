using System;
using System.Collections.Generic;
using System.Linq;

namespace Adamantium.Fonts.Tables.CMAP
{
    /// <summary>
    /// Segmented coverage                                                                                   
    /// This is the standard character-to-glyph-index mapping subtable for fonts supporting Unicode character
    /// repertoires that include supplementary-plane characters (U+10000 to U+10FFFF).                       
    /// </summary>
    public class CharacterMapFormat12 : CharacterMap
    {
        public override UInt16 Format => 12;
        
        public UInt16 Reserved { get; set; }

        public UInt32 Length { get; set; }

        public UInt32 Language { get; set; }
        
        public UInt32 NumGroups { get; set; }
        
        // array size is NumGroups
        public SequentialMapGroup[] Groups { get; set; }
        
        public uint[] StartCharCodes { get; set; }
        public uint[] EndCharCodes { get; set; }
        
        public uint[] StartGlyphIds { get; set; }

        public override uint GetGlyphIndex(uint character)
        {
            int i = Array.BinarySearch(StartCharCodes, character);
            i = i < 0 ? ~i - 1 : i;
            
            if (i >= 0 && character <= EndCharCodes[i])
            {
                return StartGlyphIds[i] + character - StartCharCodes[i];
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
                    unicodeToGlyph[code] = GetGlyphIndex(code);
                }

                from = After(group.EndCharCode, from);
            }
        }
    }
}