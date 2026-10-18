using System.Globalization;

namespace Adamantium.Fonts.Shaping;

internal struct GlyphInfo
{
    private const byte LigBaseFlag = 0x10;

    public int Codepoint;
    public uint Glyph;
    public int Cluster;
    public uint Mask;
    public ushort Props;
    public byte LigProps;
    public byte CombiningClass;
    public UnicodeCategory Category;
    public UnicodeFlags Flags;
    public SpaceKind Space;

    // A piece of a stretched Syriac abbreviation mark: 1 set once, 2 repeated to the width of its word; 0 neither.
    public byte Stretch;

    public bool IsMark => (Props & GlyphProps.Mark) != 0;

    public bool IsBaseGlyph => (Props & GlyphProps.BaseGlyph) != 0;

    public bool IsLigature => (Props & GlyphProps.Ligature) != 0;

    public bool IsMultiplied => (Props & GlyphProps.Multiplied) != 0;

    public bool IsLigated => (Props & GlyphProps.Ligated) != 0;

    public bool IsUnicodeMark => UnicodeData.IsMark(Category);

    public bool IsDefaultIgnorable => (Flags & UnicodeFlags.Ignorable) != 0 && !IsSubstituted;

    public bool IsDefaultIgnorableAndNotHidden =>
        (Flags & (UnicodeFlags.Ignorable | UnicodeFlags.Hidden)) == UnicodeFlags.Ignorable && !IsSubstituted;

    public bool IsSubstituted => (Props & GlyphProps.Substituted) != 0;

    public bool IsZwnj => (Flags & UnicodeFlags.Zwnj) != 0;

    public bool IsZwj => (Flags & UnicodeFlags.Zwj) != 0;

    public bool IsContinuation => (Flags & UnicodeFlags.Continuation) != 0;

    public int LigId => LigProps >> 5;

    public int LigComp => (LigProps & LigBaseFlag) != 0 ? 0 : LigProps & 0x0F;

    public int LigNumComps => IsLigature && (LigProps & LigBaseFlag) != 0 ? LigProps & 0x0F : 1;

    public void SetLigPropsForLigature(int ligId, int numComps)
    {
        LigProps = (byte)((ligId << 5) | LigBaseFlag | (numComps & 0x0F));
    }

    public void SetLigPropsForMark(int ligId, int ligComp)
    {
        LigProps = (byte)((ligId << 5) | (ligComp & 0x0F));
    }
}
