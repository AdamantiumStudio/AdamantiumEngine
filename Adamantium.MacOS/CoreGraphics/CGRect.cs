using System.Runtime.InteropServices;

namespace Adamantium.MacOS.CoreGraphics;

/// <summary>A rectangle in Core Graphics: its origin is the bottom-left corner in AppKit's flipped-up coordinates.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct CGRect(CGPoint Origin, CGSize Size)
{
    /// <summary>A rectangle of the given origin and size.</summary>
    public CGRect(double x, double y, double width, double height) : this(new CGPoint(x, y), new CGSize(width, height))
    {
    }
}
