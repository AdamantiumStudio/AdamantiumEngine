using System.Runtime.InteropServices;

namespace Adamantium.MacOS.CoreGraphics;

/// <summary>A size in Core Graphics.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct CGSize(double Width, double Height);
