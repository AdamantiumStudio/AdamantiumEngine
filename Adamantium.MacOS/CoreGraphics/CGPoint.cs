using System.Runtime.InteropServices;

namespace Adamantium.MacOS.CoreGraphics;

/// <summary>A point in Core Graphics (CGFloat is a double on 64-bit platforms).</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct CGPoint(double X, double Y);
