using System;
using System.Collections.Generic;

namespace Adamantium.Fonts.Tables
{
    internal class KerningSubtable
    {
        public UInt16 Version { get; internal set; }
        public UInt16 Length { get; internal set; }

        public bool Horizontal { get; internal set; } // true - horizontal kerning, false - vertical
        public bool Minimum { get; internal set; }
        
        /// <summary>Kerning across the writing direction: vertical for horizontal text, horizontal for vertical text;
        /// positive values move up or right.</summary>
        public bool CrossStream { get; internal set; }
        public bool Override { get; internal set; }
        public Byte Format { get; internal set; } // set the format of this subtable (0-3 currently defined)
        public UInt16 NumberOfPairs { get; internal set; }
        public UInt16 SearchRange { get; internal set; }
        public UInt16 EntrySelector { get; internal set; }
        public UInt16 RangeShift { get; internal set; }
        public Dictionary<UInt32, Int16> KerningValues { get; } // key is  "left | right" glyph indexes

        // Apple's 'kern' (version 1.0): a longer header, a state machine (format 1) and compact classes (format 3).
        public bool Apple { get; internal set; }

        // An Apple subtable for a variation tuple: HarfBuzz leaves it out.
        public bool Variation { get; internal set; }

        // The subtable as written, its header included: what formats 1, 2 and 3 are read from.
        public byte[] Data { get; internal set; }

        public int HeaderSize => Apple ? 8 : 6;

        public KerningSubtable()
        {
            KerningValues = new Dictionary<uint, short>();
        }
    };
}
