using System;
using System.Collections.Concurrent;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging;

/// <summary>How a format lays texels out in memory: in blocks of a width and height of texels and a size in bytes - one
/// texel a block for formats that are not compressed, 4×4 and the like for those that are, read from the format's
/// Vulkan name.</summary>
internal static class TexelBlocks
{
    private static readonly ConcurrentDictionary<Format, (int Width, int Height, int Bytes)> Known = new();

    /// <summary>A block's width and height in texels and its size in bytes.</summary>
    public static (int Width, int Height, int Bytes) Of(Format format) => Known.GetOrAdd(format, Find);

    /// <summary>The bytes a row of <paramref name="width"/> texels takes, whole blocks, and the rows of blocks
    /// <paramref name="height"/> texels make.</summary>
    public static (long RowBytes, long Rows) Rows(Format format, uint width, uint height)
    {
        var (blockWidth, blockHeight, bytes) = Of(format);
        return ((width + blockWidth - 1) / blockWidth * (long)bytes, (height + blockHeight - 1) / blockHeight);
    }

    private static (int Width, int Height, int Bytes) Find(Format format)
    {
        var name = format.ToString();
        if (name.Contains("PLANE"))
        {
            throw new NotSupportedException($"{format} keeps its texels in planes, not texel by texel.");
        }

        if (name.Contains("_BLOCK"))
        {
            if (name.StartsWith("BC1_") || name.StartsWith("BC4_") || name.StartsWith("ETC2_R8G8B8_")
                || name.StartsWith("ETC2_R8G8B8A1_") || name.StartsWith("EAC_R11_"))
            {
                return (4, 4, 8);
            }

            if (name.StartsWith("BC") || name.StartsWith("ETC2_") || name.StartsWith("EAC_"))
            {
                return (4, 4, 16);
            }

            if (name.StartsWith("ASTC_"))
            {
                var size = name.Split('_')[1].Split('x');
                return (int.Parse(size[0]), int.Parse(size[1]), 16);
            }

            if (name.StartsWith("PVRTC"))
            {
                return name.Contains("2BPP") ? (8, 4, 8) : (4, 4, 8);
            }

            throw new NotSupportedException($"The blocks of {format} are not known.");
        }

        if (name.Contains("_422_"))
        {
            return (2, 1, name.Contains("16") || name.Contains("10X6") || name.Contains("12X4") ? 8 : 4);
        }

        var bytes = format.SizeOfInBytes();
        if (bytes == 0)
        {
            throw new NotSupportedException($"The size of {format} is not known.");
        }

        return (1, 1, bytes);
    }
}
