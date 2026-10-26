using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging;

/// <summary>An image in memory - 1D, 2D, 3D or cube, of layers and mip levels - laid out layer by layer, each layer's
/// levels from the full size down, each level's depth slices one after another, rows of whole texel blocks with no
/// space between. Its memory is one block; <see cref="PixelBuffer"/> has a view of each part of it. Pictures are
/// loaded into it and saved from it by <see cref="BitmapLoader"/>'s codecs.</summary>
public sealed class Image : DisposableObject
{
    private readonly PixelBuffer[] _buffers;
    private readonly int[] _levelStart;
    private readonly int _buffersPerLayer;
    private IntPtr _memory;
    private readonly bool _owned;

    private Image(ImageDescription description, IntPtr memory, bool owned)
    {
        description = Checked(description);
        Description = description;
        var layers = description.Dimension == TextureDimension.Texture3D ? 1 : (int)description.ArraySize;
        _levelStart = new int[description.MipLevels + 1];
        for (var level = 0; level < description.MipLevels; level++)
        {
            _levelStart[level + 1] = _levelStart[level] + (int)Slices(description, level);
        }

        _buffersPerLayer = _levelStart[^1];
        TotalSizeInBytes = 0;
        try
        {
            for (var level = 0; level < description.MipLevels; level++)
            {
                var (row, rows) = Level(description, level);
                var part = checked(row * rows);
                if (part > int.MaxValue)
                {
                    throw new NotSupportedException(
                        $"A level of {part} bytes of a {description.Width}×{description.Height} image is more than one " +
                        "part of an image holds.");
                }

                TotalSizeInBytes = checked(TotalSizeInBytes + (ulong)part * Slices(description, level));
            }

            TotalSizeInBytes = checked(TotalSizeInBytes * (ulong)layers);
        }
        catch (OverflowException)
        {
            throw new NotSupportedException(
                $"A {description.Width}×{description.Height}×{description.Depth} image of {layers} layers is larger " +
                "than memory holds.");
        }
        if (memory == IntPtr.Zero)
        {
            memory = Utilities.AllocateMemory((nuint)TotalSizeInBytes);
            Utilities.ClearMemory(memory, 0, (long)TotalSizeInBytes);
            owned = true;
        }

        (_memory, _owned) = (memory, owned);
        _buffers = new PixelBuffer[_buffersPerLayer * layers];
        var at = memory;
        for (var layer = 0; layer < layers; layer++)
        {
            for (var level = 0; level < description.MipLevels; level++)
            {
                var (row, rows) = Level(description, level);
                for (var slice = 0; slice < Slices(description, level); slice++)
                {
                    _buffers[layer * _buffersPerLayer + _levelStart[level] + slice] = new PixelBuffer(
                        Math.Max(1, description.Width >> level), Math.Max(1, description.Height >> level),
                        description.Format, (int)row, (int)(row * rows), at) { MipLevel = (uint)level };
                    at = (IntPtr)((long)at + row * rows);
                }
            }
        }

        PixelBuffer = _buffers;
    }

    /// <summary>What the image is.</summary>
    public ImageDescription Description { get; }

    /// <summary>Where the image's memory starts.</summary>
    public IntPtr DataPointer => _memory;

    /// <summary>How many bytes the image takes.</summary>
    public ulong TotalSizeInBytes { get; }

    /// <summary>The picture an animation was loaded from - its frames, delays and plays - kept to be saved as it was;
    /// null for an image made here or loaded from a still picture.</summary>
    public IRawBitmap Frames { get; private set; }

    /// <summary>Whether the image was loaded from an animation, a frame a layer.</summary>
    public bool IsAnimated => Frames != null;

    /// <summary>Every part of the image: layer by layer, each layer's mip levels in order, each level's depth slices.
    /// </summary>
    public IReadOnlyList<PixelBuffer> PixelBuffer { get; }

    /// <summary>Mip level <paramref name="mipmap"/> of layer <paramref name="arrayOrZSliceIndex"/> - of depth slice
    /// <paramref name="arrayOrZSliceIndex"/> for a 3D image.</summary>
    public PixelBuffer GetPixelBuffer(int arrayOrZSliceIndex, int mipmap) =>
        Description.Dimension == TextureDimension.Texture3D
            ? GetPixelBuffer(0, arrayOrZSliceIndex, mipmap)
            : GetPixelBuffer(arrayOrZSliceIndex, 0, mipmap);

    /// <summary>Depth slice <paramref name="zIndex"/> of mip level <paramref name="mipmap"/> of layer
    /// <paramref name="arrayIndex"/>.</summary>
    public PixelBuffer GetPixelBuffer(int arrayIndex, int zIndex, int mipmap)
    {
        if (mipmap < 0 || mipmap >= Description.MipLevels)
        {
            throw new ArgumentOutOfRangeException(nameof(mipmap), mipmap, $"The image has {Description.MipLevels} levels.");
        }

        if (arrayIndex < 0 || arrayIndex * _buffersPerLayer >= _buffers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayIndex), arrayIndex, "The image has no such layer.");
        }

        if (zIndex < 0 || zIndex >= _levelStart[mipmap + 1] - _levelStart[mipmap])
        {
            throw new ArgumentOutOfRangeException(nameof(zIndex), zIndex, "The level has no such depth slice.");
        }

        return _buffers[arrayIndex * _buffersPerLayer + _levelStart[mipmap] + zIndex];
    }

    /// <summary>A new image of <paramref name="description"/>, zeroed.</summary>
    public static Image New(ImageDescription description) => new(description, IntPtr.Zero, true);

    /// <summary>An image of <paramref name="description"/> in the memory at <paramref name="dataPointer"/>, which the
    /// caller keeps and frees; zeroed memory of its own when it is zero.</summary>
    public static Image New(ImageDescription description, IntPtr dataPointer) =>
        new(description, dataPointer, dataPointer == IntPtr.Zero);

    /// <summary>A new 1D image, zeroed.</summary>
    public static Image New1D(uint width, MipMapCount mipMapCount, SurfaceFormat format, uint arraySize = 1) =>
        New1D(width, mipMapCount, format, arraySize, IntPtr.Zero);

    /// <summary>A new 2D image of one level, zeroed.</summary>
    public static Image New2D(uint width, uint height, SurfaceFormat format, uint arraySize = 1) =>
        New2D(width, height, 1, format, arraySize);

    /// <summary>A new 2D image, zeroed.</summary>
    public static Image New2D(uint width, uint height, MipMapCount mipMapCount, SurfaceFormat format,
        uint arraySize = 1) => New2D(width, height, mipMapCount, format, arraySize, IntPtr.Zero);

    /// <summary>A new cube image, zeroed.</summary>
    public static Image NewCube(uint width, MipMapCount mipMapCount, SurfaceFormat format) =>
        NewCube(width, mipMapCount, format, IntPtr.Zero);

    /// <summary>A new 3D image, zeroed.</summary>
    public static Image New3D(uint width, uint height, uint depth, MipMapCount mipMapCount, SurfaceFormat format) =>
        New3D(width, height, depth, mipMapCount, format, IntPtr.Zero);

    /// <summary>A 1D image in the memory at <paramref name="dataPointer"/>, or zeroed memory of its own.</summary>
    public static Image New1D(uint width, MipMapCount mipMapCount, SurfaceFormat format, uint arraySize,
        IntPtr dataPointer) =>
        New(Make(TextureDimension.Texture1D, width, 1, 1, mipMapCount, format, arraySize), dataPointer);

    /// <summary>A 2D image in the memory at <paramref name="dataPointer"/>, or zeroed memory of its own.</summary>
    public static Image New2D(uint width, uint height, MipMapCount mipMapCount, SurfaceFormat format, uint arraySize,
        IntPtr dataPointer) =>
        New(Make(TextureDimension.Texture2D, width, height, 1, mipMapCount, format, arraySize), dataPointer);

    /// <summary>A cube image in the memory at <paramref name="dataPointer"/>, or zeroed memory of its own.</summary>
    public static Image NewCube(uint width, MipMapCount mipMapCount, SurfaceFormat format, IntPtr dataPointer) =>
        New(Make(TextureDimension.TextureCube, width, width, 1, mipMapCount, format, 6), dataPointer);

    /// <summary>A 3D image in the memory at <paramref name="dataPointer"/>, or zeroed memory of its own.</summary>
    public static Image New3D(uint width, uint height, uint depth, MipMapCount mipMapCount, SurfaceFormat format,
        IntPtr dataPointer) =>
        New(Make(TextureDimension.Texture3D, width, height, depth, mipMapCount, format, 1), dataPointer);

    /// <summary>The picture in the <paramref name="dataSize"/> bytes at <paramref name="dataPointer"/>, decoded into
    /// memory of the image's own.</summary>
    public static Image Load(IntPtr dataPointer, ulong dataSize) => From(BitmapLoader.Load(dataPointer, dataSize));

    /// <summary>The picture in <paramref name="dataBuffer"/>, decoded into memory of the image's own.</summary>
    public static Image Load(DataPointer dataBuffer) => Load(dataBuffer.Pointer, dataBuffer.Size);

    /// <summary>The picture in <paramref name="buffer"/>.</summary>
    public static unsafe Image Load(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        fixed (byte* bytes = buffer)
        {
            return Load((IntPtr)bytes, (ulong)buffer.Length);
        }
    }

    /// <summary>The picture in <paramref name="imageStream"/>, read to its end; its format is told by its bytes.
    /// </summary>
    public static Image Load(Stream imageStream)
    {
        var memory = new MemoryStream();
        imageStream.CopyTo(memory);
        return Load(memory.ToArray());
    }

    /// <summary>The picture in the file <paramref name="fileName"/>.</summary>
    public static Image Load(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentNullException(nameof(fileName));
        }

        return Load(File.ReadAllBytes(fileName));
    }

    /// <summary>The picture's type by the extension of <paramref name="fileName"/>; unknown for any other.</summary>
    public static ImageFileType GetImageTypeFromFileName(string fileName) =>
        (Path.GetExtension(fileName) ?? string.Empty).TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => ImageFileType.Jpg,
            "dds" => ImageFileType.Dds,
            "gif" => ImageFileType.Gif,
            "bmp" => ImageFileType.Bmp,
            "png" => ImageFileType.Png,
            "tga" => ImageFileType.Tga,
            "tif" or "tiff" => ImageFileType.Tiff,
            "ico" => ImageFileType.Ico,
            _ => ImageFileType.Unknown
        };

    /// <summary>Writes the image to the file <paramref name="fileName"/> as <paramref name="fileType"/>.</summary>
    public void Save(string fileName, ImageFileType fileType)
    {
        var memory = new MemoryStream();
        Save(memory, fileType);
        using var stream = new FileStream(fileName, FileMode.Create, FileAccess.Write);
        memory.Position = 0;
        memory.CopyTo(stream);
    }

    /// <summary>Writes the image to <paramref name="imageStream"/> as <paramref name="fileType"/>.</summary>
    public void Save(Stream imageStream, ImageFileType fileType) =>
        BitmapLoader.Save(this.ConvertToRawBitmap(), imageStream, fileType);

    /// <summary>The mip levels of a <paramref name="width"/> × <paramref name="height"/> image: all of them down to one
    /// texel for <see cref="MipMapCount.Auto"/>, else as many as asked, no more than there are.</summary>
    public static uint CalculateMipLevels(uint width, uint height, MipMapCount mipLevels) =>
        CalculateMipLevels(width, height, 1, mipLevels);

    /// <summary>The mip levels of a <paramref name="width"/> × <paramref name="height"/> × <paramref name="depth"/>
    /// image, as <see cref="CalculateMipLevels(uint, uint, MipMapCount)"/>.</summary>
    public static uint CalculateMipLevels(uint width, uint height, uint depth, MipMapCount mipLevels)
    {
        var largest = Math.Max(width, Math.Max(height, depth));
        var all = 1u;
        while (largest > 1)
        {
            largest >>= 1;
            all++;
        }

        return mipLevels.Count == 0 ? all : Math.Min(mipLevels.Count, all);
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        if (_owned && _memory != IntPtr.Zero)
        {
            Utilities.FreeMemory(_memory);
        }

        _memory = IntPtr.Zero;
        base.Dispose(disposeManagedResources);
    }

    /// <summary>An image of <paramref name="bitmap"/>: every layer, mip level and depth slice of one that has them, as
    /// a DDS does; another's frames as layers, the picture kept at <see cref="Frames"/> when it has several.</summary>
    public static Image From(IRawBitmap bitmap)
    {
        var description = bitmap.GetImageDescription();
        if (bitmap is ISubresources subresources)
        {
            var image = New(description with { MipLevels = Math.Max(1, description.MipLevels) });
            var layers = image._buffers.Length / image._buffersPerLayer;
            for (var layer = 0; layer < layers; layer++)
            {
                for (var level = 0; level < image.Description.MipLevels; level++)
                {
                    for (var slice = 0; slice < image._levelStart[level + 1] - image._levelStart[level]; slice++)
                    {
                        subresources.CopySubresource(layer, level, slice, image.GetPixelBuffer(layer, slice, level));
                    }
                }
            }

            return image;
        }

        var frames = Math.Max(1u, bitmap.FramesCount);
        var made = New(description with
        {
            Dimension = TextureDimension.Texture2D, Depth = 1, MipLevels = 1, ArraySize = frames
        });
        var size = (int)Math.Min(made._buffers[0].BufferStride, (long)made.TotalSizeInBytes);
        for (uint frame = 0; frame < frames; frame++)
        {
            var pixels = bitmap.GetRawPixels(frame);
            Marshal.Copy(pixels, 0, made._buffers[frame].DataPointer, Math.Min(size, pixels.Length));
        }

        made.Frames = bitmap.IsMultiFrame ? bitmap : null;
        return made;
    }

    private static ImageDescription Make(TextureDimension dimension, uint width, uint height, uint depth,
        MipMapCount levels, SurfaceFormat format, uint arraySize) => new()
    {
        Dimension = dimension, Width = width, Height = height, Depth = depth, MipLevels = levels.Count,
        Format = format, ArraySize = arraySize
    };

    private static ImageDescription Checked(ImageDescription description)
    {
        if (description.Format == Format.UNDEFINED)
        {
            throw new InvalidOperationException("An image has a format.");
        }

        var (width, height, depth, layers) = (description.Width, description.Height, description.Depth,
            description.ArraySize);
        var fits = description.Dimension switch
        {
            TextureDimension.Texture1D => width > 0 && height == 1 && depth == 1 && layers > 0,
            TextureDimension.Texture2D => width > 0 && height > 0 && depth == 1 && layers > 0,
            TextureDimension.TextureCube => width > 0 && height > 0 && depth == 1 && layers > 0 && layers % 6 == 0,
            TextureDimension.Texture3D => width > 0 && height > 0 && depth > 0 && layers == 1,
            _ => false
        };
        if (!fits)
        {
            throw new InvalidOperationException(
                $"A {description.Dimension} image of {width}×{height}×{depth} and {layers} layers.");
        }

        return description with { MipLevels = CalculateMipLevels(width, height, depth, description.MipLevels) };
    }

    private static (long Row, long Rows) Level(ImageDescription description, int level) =>
        TexelBlocks.Rows(description.Format, Math.Max(1, description.Width >> level),
            Math.Max(1, description.Height >> level));

    private static uint Slices(ImageDescription description, int level) =>
        description.Dimension == TextureDimension.Texture3D ? Math.Max(1, description.Depth >> level) : 1;
}

/// <summary>A picture whose every part - layer, mip level, depth slice - an <see cref="Image"/> takes as it is.</summary>
internal interface ISubresources
{
    /// <summary>Copies mip level <paramref name="level"/>'s depth slice <paramref name="slice"/> of layer
    /// <paramref name="layer"/> into <paramref name="target"/>, of its size and format.</summary>
    void CopySubresource(int layer, int level, int slice, PixelBuffer target);
}
