using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Adamantium.Imaging.Icc;

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
    private readonly bool _applyProfiles;
    private readonly List<(byte[] Profile, int Channels, IccPixelConverter Converter)> _converters = [];
    private IccPixelConverter _firstPageProfile;

    private TiffImage(ITiffSource source, bool littleEndian, TiffLayout[] frames, BitmapLoadOptions options)
    {
        _source = source;
        _littleEndian = littleEndian;
        _frames = frames;
        _decoded = new TiffBitmap[frames.Length];
        _applyProfiles = options.ApplyColorProfile;

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
    public static TiffImage Load(byte[] bytes) => Load(bytes, BitmapLoadOptions.Default);

    /// <summary>Reads the TIFF file in <paramref name="bytes"/> as <paramref name="options"/> say, decoding its first
    /// page; it throws as <see cref="Load(byte[])"/> does.</summary>
    public static TiffImage Load(byte[] bytes, BitmapLoadOptions options)
    {
        var file = TiffFile.Open(new TiffMemorySource(bytes));
        var pages = file.Directories.Where(directory => directory.IsPage).ToList();
        var first = TiffLayout.Read(pages.Count > 0 ? pages[0] : file.Directories[0]);
        var size = TiffColor.SizeOf(first);
        var format = TiffColor.FormatOf(first);
        CheckSize(first, format);
        List<TiffLayout> frames = [first];
        foreach (var directory in pages.Skip(1))
        {
            if (TiffTry.Read(() => Checked(TiffLayout.Read(directory)), null) is { } page
                && TiffColor.SizeOf(page) == size && TiffColor.FormatOf(page).Equals(format))
            {
                frames.Add(page);
            }
        }

        var image = new TiffImage(file.Source, file.LittleEndian, [.. frames], options ?? BitmapLoadOptions.Default);
        image.Frame(0);
        return image;
    }

    /// <inheritdoc/>
    public byte[] ColorProfile => _firstPageProfile != null ? null : TiffColor.ProfileOf(_frames[0]);

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
            if (_decoded[index] == null)
            {
                var profile = ProfileOf(_frames[index]);
                if (index == 0)
                {
                    _firstPageProfile = profile;
                }

                _decoded[index] = TiffColor.ToBitmap(_frames[index],
                    TiffDecoder.Decode(_source, _frames[index], _littleEndian), profile);
            }

            return _decoded[index];
        }
    }

    private IccPixelConverter ProfileOf(TiffLayout page)
    {
        var channels = _applyProfiles ? TiffColor.ProfileChannels(page) : 0;
        if (channels == 0 || TiffColor.ProfileOf(page) is not { } profile)
        {
            return null;
        }

        foreach (var (known, knownChannels, converter) in _converters)
        {
            if (knownChannels == channels && known.AsSpan().SequenceEqual(profile))
            {
                return converter;
            }
        }

        var made = IccPixels.Converter(profile, channels, (long)page.Width * page.Height);
        _converters.Add((profile, channels, made));
        return made;
    }

    private static void CheckSize(TiffLayout page, SurfaceFormat format)
    {
        var bytes = (long)page.Width * page.Height * Math.Max(format.SizeInBytes, page.SamplesPerPixel);
        if (bytes > Array.MaxLength)
        {
            throw new NotSupportedException($"The TIFF image of {page.Width}×{page.Height} pixels takes {bytes} bytes - "
                                            + "more than one array holds; read it by tiles instead.");
        }
    }

    private static TiffLayout Checked(TiffLayout page)
    {
        CheckSize(page, TiffColor.FormatOf(page));
        return page;
    }
}
