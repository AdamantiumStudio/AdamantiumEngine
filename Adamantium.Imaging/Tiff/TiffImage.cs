using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Adamantium.Imaging.Tiff;

/// <summary>A TIFF file read as a picture: its first page, and the pages after it of the same size and format as
/// frames. Reduced-resolution copies and transparency masks are not pages. Pages are decoded when first asked for.
/// </summary>
public sealed class TiffImage : IRawBitmap
{
    private readonly Lock _decoding = new();
    private readonly ITiffSource _source;
    private readonly bool _littleEndian;
    private readonly TiffLayout[] _frames;
    private readonly TiffBitmap[] _decoded;

    private TiffImage(ITiffSource source, bool littleEndian, TiffLayout[] frames)
    {
        _source = source;
        _littleEndian = littleEndian;
        _frames = frames;
        _decoded = new TiffBitmap[frames.Length];
        (var width, var height) = TiffColor.SizeOf(frames[0]);
        Width = (uint)width;
        Height = (uint)height;
        PixelFormat = TiffColor.FormatOf(frames[0]);
    }

    /// <inheritdoc/>
    public uint Width { get; }

    /// <inheritdoc/>
    public uint Height { get; }

    /// <inheritdoc/>
    public SurfaceFormat PixelFormat { get; }

    /// <inheritdoc/>
    public bool IsMultiFrame => _frames.Length > 1;

    /// <inheritdoc/>
    public bool HasMipLevels => false;

    /// <inheritdoc/>
    public uint MipLevelsCount => 0;

    /// <inheritdoc/>
    public uint NumberOfReplays => 0;

    /// <inheritdoc/>
    public uint FramesCount => (uint)_frames.Length;

    /// <summary>Reads the TIFF file in <paramref name="bytes"/>, decoding its first page; a broken file throws
    /// <see cref="System.IO.InvalidDataException"/>, one using what is not read <see cref="NotSupportedException"/>.
    /// </summary>
    public static TiffImage Load(byte[] bytes)
    {
        var file = TiffFile.Open(new TiffMemorySource(bytes));
        var pages = file.Directories.Where(IsPage).ToList();
        var first = TiffLayout.Read(pages.Count > 0 ? pages[0] : file.Directories[0]);
        var size = TiffColor.SizeOf(first);
        var format = TiffColor.FormatOf(first);
        CheckSize(first, format);
        List<TiffLayout> frames = [first];
        foreach (var directory in pages.Skip(1))
        {
            if (Readable(directory) is { } page && TiffColor.SizeOf(page) == size
                                                && TiffColor.FormatOf(page).Equals(format))
            {
                frames.Add(page);
            }
        }

        var image = new TiffImage(file.Source, file.LittleEndian, [.. frames]);
        image.Frame(0);
        return image;
    }

    /// <inheritdoc/>
    public byte[] GetRawPixels(uint frameIndex) => Frame(frameIndex).Pixels;

    /// <inheritdoc/>
    public FrameData GetMipLevelData(uint mipLevel) => GetFrameData(mipLevel);

    /// <inheritdoc/>
    public ImageDescription GetImageDescription() => ImageDescription.Default2D(Width, Height, PixelFormat);

    /// <inheritdoc/>
    public FrameData GetFrameData(uint frameIndex) => new(GetRawPixels(frameIndex), GetImageDescription());

    /// <inheritdoc/>
    public void ReleaseDecodedFrames()
    {
        lock (_decoding)
        {
            Array.Clear(_decoded);
        }
    }

    private TiffBitmap Frame(uint index)
    {
        if (index >= _frames.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"The TIFF image has {_frames.Length} frames.");
        }

        lock (_decoding)
        {
            return _decoded[index] ??= TiffColor.ToBitmap(_frames[index],
                TiffDecoder.Decode(_source, _frames[index], _littleEndian));
        }
    }

    private static bool IsPage(TiffDirectory directory) =>
        (directory.GetUnsigned(TiffTag.NewSubfileType, 0) & 0b101) == 0;

    private static void CheckSize(TiffLayout page, SurfaceFormat format)
    {
        var bytes = (long)page.Width * page.Height * Math.Max(format.SizeInBytes, page.SamplesPerPixel);
        if (bytes > Array.MaxLength)
        {
            throw new NotSupportedException($"The TIFF image of {page.Width}×{page.Height} pixels takes {bytes} bytes - "
                                            + "more than one array holds; read it by tiles instead.");
        }
    }

    private static TiffLayout Readable(TiffDirectory directory)
    {
        try
        {
            var page = TiffLayout.Read(directory);
            CheckSize(page, TiffColor.FormatOf(page));
            return page;
        }
        catch (Exception exception) when (exception is NotSupportedException or System.IO.InvalidDataException)
        {
            return null;
        }
    }
}
