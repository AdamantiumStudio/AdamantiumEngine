using System;
using System.Runtime.InteropServices;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core;

/// <summary>
/// Decribes texture parameters which will be used to create or load texture
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TextureDescription
{
    /// <summary>
    /// The dimension of a texture.
    /// </summary>
    public TextureDimension Dimension;

    /// <summary>Width in texels, of every texture.</summary>
    public uint Width;

    /// <summary>Height in texels, of 2D, 3D and cube textures.</summary>
    public uint Height;

    /// <summary>Depth in texels, of 3D textures.</summary>
    public uint Depth;

    /// <summary>How many layers the texture has, for 1D, 2D and cube textures.</summary>
    public uint ArrayLayers;

    /// <summary>How many mip levels the texture has.</summary>
    public uint MipLevels;

    /// <summary>The texels' format.</summary>
    public Format Format;

    /// <summary>Samples a texel, for 2D textures.</summary>
    public MSAALevel Samples;

    /// <summary>What the texture is used for.</summary>
    public ImageUsageFlagBits Usage;

    /// <summary>Whether queue families share the texture.</summary>
    public SharingMode SharingMode;

    public ImageTiling ImageTiling;

    public ImageCreateFlagBits Flags;
        
    public ImageType ImageType;

    public ImageLayout InitialLayout;

    public ImageLayout DesiredImageLayout;

    public ImageAspectFlagBits ImageAspect;

    /// <summary>
    /// Gets the staging description for this instance.
    /// </summary>
    /// <returns>A Staging description</returns>
    public TextureDescription ToStagingDescription()
    {
        var copy = this;
        copy.SharingMode = SharingMode.Exclusive;
        copy.Samples = MSAALevel.None;
        copy.Usage = ImageUsageFlagBits.StorageBit;
        return copy;
    }

    public bool Equals(TextureDescription other)
    {
        return Dimension == other.Dimension &&
               Width == other.Width &&
               Height == other.Height &&
               Depth == other.Depth &&
               ArrayLayers == other.ArrayLayers &&
               MipLevels == other.MipLevels &&
               Format == other.Format &&
               Samples == other.Samples &&
               Usage == other.Usage &&
               SharingMode == other.SharingMode &&
               ImageTiling == other.ImageTiling &&
               ImageType == other.ImageType &&
               DesiredImageLayout == other.DesiredImageLayout &&
               Flags == other.Flags;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }
        return obj is TextureDescription && Equals((TextureDescription)obj);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = Dimension.GetHashCode();
            hashCode = (hashCode * 397) ^ (int)Width;
            hashCode = (hashCode * 397) ^ (int)Height;
            hashCode = (hashCode * 397) ^ (int)Depth;
            hashCode = (hashCode * 397) ^ (int)ArrayLayers;
            hashCode = (hashCode * 397) ^ (int)MipLevels;
            hashCode = (hashCode * 397) ^ Format.GetHashCode();
            hashCode = (hashCode * 397) ^ Samples.GetHashCode();
            hashCode = (hashCode * 397) ^ Usage.GetHashCode();
            hashCode = (hashCode * 397) ^ SharingMode.GetHashCode();
            hashCode = (hashCode * 397) ^ ImageTiling.GetHashCode();
            hashCode = (hashCode * 397) ^ ImageType.GetHashCode();
            hashCode = (hashCode * 397) ^ DesiredImageLayout.GetHashCode();
            hashCode = (hashCode * 397) ^ Flags.GetHashCode();

            return hashCode;
        }
    }

    public static bool operator ==(TextureDescription left, TextureDescription right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(TextureDescription left, TextureDescription right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Performs an explicit conversion from <see cref="Texture2DDescription"/> to <see cref="TextureDescription"/>.
    /// </summary>
    /// <param name="description">The texture description.</param>
    /// <returns>The result of the conversion.</returns>
    public static implicit operator TextureDescription(ImageCreateInfo description)
    {
        return new TextureDescription()
        {
            Dimension = (TextureDimension)description.ImageType,
            Width = description.Extent.Width,
            Height = description.Extent.Height,
            Depth = description.Extent.Depth,
            MipLevels = description.MipLevels,
            ArrayLayers = description.ArrayLayers,
            Format = description.Format,
            Samples = MSAALevel.None,
            Usage = description.Usage,
            SharingMode = description.SharingMode,
            ImageTiling = description.Tiling,
            ImageType = description.ImageType,
            DesiredImageLayout = description.InitialLayout,
            Flags = description.Flags
        };
    }

    /// <summary>
    /// Performs an explicit conversion from <see cref="TextureDescription"/> to <see cref="Texture2DDescription"/>.
    /// </summary>
    /// <param name="description">The texture description.</param>
    /// <returns>The result of the conversion.</returns>
    public ImageCreateInfo ToImageCreateInfo()
    {
        return new ImageCreateInfo()
        {
            ImageType = ImageType,
            Extent = new Extent3D() { Width = Width, Height = Height, Depth = Depth },
            MipLevels = MipLevels,
            ArrayLayers = ArrayLayers,
            Format = Format,
            Samples = (SampleCountFlagBits)Samples,
            Usage = Usage,
            SharingMode = SharingMode,
            Tiling = ImageTiling,
            InitialLayout = InitialLayout,
            Flags = Flags,
        };
    }

    /// <summary>
    /// Performs an explicit conversion from <see cref="ImageDescription"/> to <see cref="TextureDescription"/>.
    /// </summary>
    /// <param name="description">The image description.</param>
    /// <returns>The result of the conversion.</returns>
    public static implicit operator ImageDescription(TextureDescription description)
    {
        return new ImageDescription()
        {
            Dimension = description.Dimension,
            Width = description.Width,
            Height = description.Height,
            Depth = description.Depth,
            ArraySize = description.ArrayLayers,
            MipLevels = description.MipLevels,
            Format = description.Format,
        };
    }

    /// <summary>
    /// Performs an explicit conversion from <see cref="ImageDescription"/> to <see cref="TextureDescription"/>.
    /// </summary>
    /// <param name="description">The image description.</param>
    /// <returns>The result of the conversion.</returns>
    public static implicit operator TextureDescription(ImageDescription description)
    {
        return new TextureDescription()
        {
            Dimension = description.Dimension,
            Width = description.Width,
            Height = description.Height,
            Depth = description.Depth,
            ArrayLayers = description.ArraySize,
            MipLevels = description.MipLevels,
            Format = description.Format,
            Samples = MSAALevel.None,
            Usage = ImageUsageFlagBits.SampledBit,
            ImageTiling = ImageTiling.Optimal,
            SharingMode = SharingMode.Exclusive,
            ImageType = ConvertToImageType(description.Dimension),
            InitialLayout = ImageLayout.Undefined,
            ImageAspect = ImageAspectFlagBits.ColorBit,
            Flags = description.Dimension == TextureDimension.TextureCube ? ImageCreateFlagBits.CubeCompatibleBit : 0
        };
    }

    private static ImageType ConvertToImageType(TextureDimension dimension)
    {
        switch (dimension)
        {
            case TextureDimension.Texture1D:
                return ImageType._1d;
            case TextureDimension.Texture2D:
            case TextureDimension.TextureCube:
                return ImageType._2d;
            case TextureDimension.Texture3D:
                return ImageType._3d;
            default:
                throw new ArgumentOutOfRangeException($"cannot convert {dimension} to a valid ImageType");
        }
    }
}