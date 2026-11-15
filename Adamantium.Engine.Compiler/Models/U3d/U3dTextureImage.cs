using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A continuation image of a texture (ECMA-363 9.8.5.1.4) and the bytes its continuation blocks brought.</summary>
internal sealed class U3dTextureImage
{
    internal const byte Jpeg24 = 0x01;
    internal const byte Png = 0x02;
    internal const byte Jpeg8 = 0x03;
    internal const byte Tiff = 0x04;

    /// <summary>The channel bits of a color image: blue, green, red, luminance.</summary>
    internal const byte ColorChannels = 0x1E;

    internal byte CompressionType { get; init; }

    /// <summary>The channels the image gives: 0x1 alpha, 0x2 blue, 0x4 green, 0x8 red, 0x10 luminance.</summary>
    internal byte Channels { get; init; }

    internal bool IsExternal { get; init; }

    internal uint ByteCount { get; init; }

    internal List<string> Urls { get; } = [];

    internal List<byte[]> Chunks { get; } = [];
}
