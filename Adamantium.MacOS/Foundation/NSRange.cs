using System.Runtime.InteropServices;

namespace Adamantium.MacOS.Foundation;

/// <summary>A run of characters, in UTF-16 units.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct NSRange(nint Location, nint Length)
{
    /// <summary>NSNotFound, where there is none.</summary>
    public static nint NotFound => nint.MaxValue;

    /// <summary>No range.</summary>
    public static NSRange None => new(NotFound, 0);

    /// <summary>The character after the last.</summary>
    public nint End => Location + Length;

    /// <summary>Whether it is a range at all.</summary>
    public bool IsFound => Location != NotFound;
}
