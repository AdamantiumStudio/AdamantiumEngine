using System;
using System.Collections.Generic;
using System.Linq;

namespace Adamantium.Fonts.Tables.CMAP
{
    public class CharacterMapFormat4 : CharacterMap
    {
        public override UInt16 Format => 4;

        public UInt16 Length { get; set; }

        public UInt16 Language { get; set; }
        
        public UInt16 SegCountX2 { get; set; }
        
        public UInt16 SegCount { get; set; }
        
        public UInt16 SearchRange { get; set; }
        
        public UInt16 EntrySelector { get; set; }
        
        public UInt16 RangeShift { get; set; }
        
        // array size is number of segments count
        public UInt16[] EndCode { get; set; }
        
        public UInt16 ReservedPad { get; set; }
        
        // array size is number of segments count
        public UInt16[] StartCode { get; set; }
        
        // array size is number of segments count
        public Int16[] IdDelta { get; set; }
        
        // array size is number of segments count
        public UInt16[] IdRangeOffsets { get; set; }
        
        public UInt16[] GlyphIdArray { get; set; }
        
        public override uint GetGlyphIndex(uint character)
        {
            if (character > ushort.MaxValue) return 0;

            int i = Array.BinarySearch(EndCode, (ushort)character);
            i = i < 0 ? ~i : i;
            if (i >= EndCode.Length || i >= StartCode.Length || i >= IdDelta.Length || i >= IdRangeOffsets.Length ||
                StartCode[i] > character)
            {
                return 0;
            }

            if (IdRangeOffsets[i] == 0)
            {
                return (ushort)((character + IdDelta[i]) % 65536);
            }

            var index = IdRangeOffsets[i] / 2 + (character - StartCode[i]) - IdRangeOffsets.Length + i;
            if (index < 0 || index >= GlyphIdArray.Length)
            {
                return 0;
            }

            var glyph = GlyphIdArray[index];
            return glyph == 0 ? 0u : (ushort)((glyph + IdDelta[i]) % 65536);
        }

        public override void CollectUnicodeChars(List<uint> unicodes)
        {
            var from = 0u;
            foreach (var i in SegmentsInOrder())
            {
                foreach (var code in CodesOf(StartCode[i], EndCode[i], from))
                {
                    unicodes.Add(code);
                }

                from = After(EndCode[i], from);
            }
        }

        private IEnumerable<int> SegmentsInOrder() =>
            Enumerable.Range(0, Math.Min(StartCode.Length, EndCode.Length)).OrderBy(i => StartCode[i]);

        public override void GetUnicodeToGlyphMappings(Dictionary<uint, uint> unicodeToGlyph)
        {
            var from = 0u;
            foreach (var i in SegmentsInOrder())
            {
                foreach (var code in CodesOf(StartCode[i], EndCode[i], from))
                {
                    unicodeToGlyph[code] = GetGlyphIndex(code);
                }

                from = After(EndCode[i], from);
            }
        }
    }
}