using System.Runtime.InteropServices;

namespace Adamantium.Win32.Printing.Xps;

/// <summary>A glyph's place in a run of glyphs, in hundredths of the em (XPS 1.0, 12.1.3): which glyph, how far the pen
/// moves after it, and how far it is moved from the pen - up for the vertical.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct XpsGlyphIndex(int index, float advanceWidth, float horizontalOffset, float verticalOffset)
{
    public int Index = index;
    public float AdvanceWidth = advanceWidth;
    public float HorizontalOffset = horizontalOffset;
    public float VerticalOffset = verticalOffset;
}
