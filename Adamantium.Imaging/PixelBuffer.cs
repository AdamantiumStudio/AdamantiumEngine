using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging;

/// <summary>One part of an image's memory: a mip level of a layer, or a depth slice of it - its size, its format, how
/// far apart its rows lie and where it starts. Pixels are read and written by value, every access checked against its
/// bounds. The memory is the image's; a pixel buffer lives no longer than it.</summary>
public sealed class PixelBuffer
{
    /// <summary>The <paramref name="width"/> × <paramref name="height"/> pixels of <paramref name="format"/> at
    /// <paramref name="dataPointer"/>, rows <paramref name="rowStride"/> bytes apart and
    /// <paramref name="bufferStride"/> bytes in all.</summary>
    public PixelBuffer(uint width, uint height, Format format, int rowStride, int bufferStride, IntPtr dataPointer)
    {
        (Width, Height, Format, RowStride, BufferStride, DataPointer) =
            (width, height, format, rowStride, bufferStride, dataPointer);
    }

    /// <summary>Width in pixels.</summary>
    public uint Width { get; }

    /// <summary>Height in pixels.</summary>
    public uint Height { get; }

    /// <summary>The pixels' format.</summary>
    public Format Format { get; }

    /// <summary>Bytes a pixel takes - a block's for a compressed format.</summary>
    public int PixelSize => TexelBlocks.Of(Format).Bytes;

    /// <summary>Bytes from one row to the next - a row of blocks for a compressed format.</summary>
    public int RowStride { get; }

    /// <summary>Bytes the buffer takes.</summary>
    public int BufferStride { get; }

    /// <summary>Where the pixels start.</summary>
    public IntPtr DataPointer { get; }

    /// <summary>The mip level this buffer is of.</summary>
    public uint MipLevel { get; init; }

    /// <summary>The buffer as a 2D image of one layer and one level.</summary>
    public ImageDescription GetDescription() => ImageDescription.Default2D(Width, Height, Format);

    /// <summary>The pixel at <paramref name="x"/>, <paramref name="y"/>, as a value of the pixel's size.</summary>
    public unsafe T GetPixel<T>(int x, int y) where T : unmanaged =>
        Unsafe.ReadUnaligned<T>((byte*)DataPointer + At<T>(x, y));

    /// <summary>Writes <paramref name="value"/> as the pixel at <paramref name="x"/>, <paramref name="y"/>.</summary>
    public unsafe void SetPixel<T>(int x, int y, T value) where T : unmanaged =>
        Unsafe.WriteUnaligned((byte*)DataPointer + At<T>(x, y), value);

    /// <summary>Every row's pixels, one after another, without the space between rows.</summary>
    public T[] GetPixels<T>() where T : unmanaged
    {
        var row = (int)Math.Min(RowStride, TexelBlocks.Rows(Format, Width, 1).RowBytes);
        var rows = (int)TexelBlocks.Rows(Format, Width, Height).Rows;
        var pixels = new T[(long)row * rows / Unsafe.SizeOf<T>()];
        var target = MemoryMarshal.AsBytes(pixels.AsSpan());
        for (var y = 0; y < rows; y++)
        {
            Row(y, row).CopyTo(target.Slice(y * row, row));
        }

        return pixels;
    }

    /// <summary>Writes <paramref name="pixels"/> - every row's, one after another - into the rows.</summary>
    public void SetPixels<T>(T[] pixels) where T : unmanaged
    {
        var row = (int)Math.Min(RowStride, TexelBlocks.Rows(Format, Width, 1).RowBytes);
        var rows = (int)TexelBlocks.Rows(Format, Width, Height).Rows;
        var source = MemoryMarshal.AsBytes(pixels.AsSpan());
        if (source.Length < (long)row * rows)
        {
            throw new ArgumentException($"{Width}×{Height} pixels take {row * rows} bytes, not {source.Length}.",
                nameof(pixels));
        }

        for (var y = 0; y < rows; y++)
        {
            source.Slice(y * row, row).CopyTo(Row(y, row));
        }
    }

    /// <summary>Copies the pixels into <paramref name="destination"/>, of the same size and format, row by row.</summary>
    public void CopyTo(PixelBuffer destination)
    {
        if (destination.Width != Width || destination.Height != Height || destination.Format != Format)
        {
            throw new ArgumentException("A pixel buffer is copied into one of its own size and format.",
                nameof(destination));
        }

        var row = (int)Math.Min(Math.Min(RowStride, destination.RowStride), TexelBlocks.Rows(Format, Width, 1).RowBytes);
        var rows = (int)TexelBlocks.Rows(Format, Width, Height).Rows;
        for (var y = 0; y < rows; y++)
        {
            Row(y, row).CopyTo(destination.Row(y, row));
        }
    }

    private unsafe Span<byte> Row(int y, int length) => new((byte*)DataPointer + (long)y * RowStride, length);

    private long At<T>(int x, int y) where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();
        if (x < 0 || y < 0 || x >= Width || y >= Height || (long)y * RowStride + (long)(x + 1) * size > BufferStride)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside the {Width}×{Height} pixels.");
        }

        return (long)y * RowStride + (long)x * size;
    }
}
