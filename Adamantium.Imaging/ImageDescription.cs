using System.Runtime.InteropServices;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging;

/// <summary>What an image is: its kind, its size in texels, layers and mip levels, and its pixel format.</summary>
[StructLayout(LayoutKind.Sequential)]
public record struct ImageDescription
{
    /// <summary>1D, 2D, 3D or cube.</summary>
    public TextureDimension Dimension;

    /// <summary>Width in texels.</summary>
    public uint Width;

    /// <summary>Height in texels; 1 for 1D.</summary>
    public uint Height;

    /// <summary>Depth in texels; 1 but for 3D.</summary>
    public uint Depth;

    /// <summary>Layers - six a cube.</summary>
    public uint ArraySize;

    /// <summary>Mip levels, the full size the first.</summary>
    public uint MipLevels;

    /// <summary>The texels' format.</summary>
    public Format Format;

    /// <summary>Bytes a row of the full size takes - a row of blocks for a compressed format.</summary>
    public readonly long RowStride => TexelBlocks.Rows(Format, Width, Height).RowBytes;

    /// <summary>Bytes the full size's first layer takes, rows of <see cref="RowStride"/>.</summary>
    public readonly long TotalSizeInBytes
    {
        get
        {
            var (row, rows) = TexelBlocks.Rows(Format, Width, Height);
            return row * rows;
        }
    }

    /// <summary>A 2D image of one layer and one mip level.</summary>
    public static ImageDescription Default2D(uint width, uint height, SurfaceFormat format) => new()
    {
        Dimension = TextureDimension.Texture2D,
        Width = width,
        Height = height,
        Depth = 1,
        ArraySize = 1,
        MipLevels = 1,
        Format = format
    };
}
