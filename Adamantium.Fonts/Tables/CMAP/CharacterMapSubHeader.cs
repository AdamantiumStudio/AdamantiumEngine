using System;

namespace Adamantium.Fonts.Tables.CMAP
{
    /// <summary>A range of second bytes in a format 2 subtable, and where their glyphs are.</summary>
    public struct CharacterMapSubHeader
    {
        public UInt16 FirstCode;

        public UInt16 EntryCount;

        public Int16 IdDelta;

        /// <summary>Bytes from this field to the glyph of <see cref="FirstCode"/> in the glyph array.</summary>
        public UInt16 IdRangeOffset;
    }
}
