using System.Runtime.InteropServices;

namespace Adamantium.Imaging;

/// <summary>How many mip levels an image has: a number of them, or 0 for every level down to one texel. A bool reads
/// as all of them (true) or the full size alone (false).</summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly record struct MipMapCount(uint Count)
{
    /// <summary>Every level down to one texel.</summary>
    public static readonly MipMapCount Auto = new(0);

    /// <summary>Every level (true) or the full size alone (false).</summary>
    public MipMapCount(bool allMipMaps) : this(allMipMaps ? 0u : 1u)
    {
    }

    public static implicit operator bool(MipMapCount count) => count.Count == 0;

    public static implicit operator MipMapCount(bool allMipMaps) => new(allMipMaps);

    public static implicit operator uint(MipMapCount count) => count.Count;

    public static implicit operator MipMapCount(uint count) => new(count);
}
