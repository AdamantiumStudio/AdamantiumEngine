using System;
using System.Collections.Generic;
using Adamantium.Imaging.Icc;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>A JPEG 2000 picture as RGBA: the full one decoded at once, its smaller resolutions as mip levels and its
/// coarser qualities as quality levels, each decoded from the same data once, when first asked for, and kept. Its
/// levels are not a GPU mip chain - a JPEG 2000 resolution is the ceiling of the half, a mip the floor - so the
/// texture it describes has one level.</summary>
public sealed class Jpeg2000Bitmap : IRawBitmap
{
    private readonly byte[] _data;
    private readonly long _maxPixels;
    private readonly Dictionary<(uint Mip, uint Quality), Lazy<FrameData>> _levels = [];
    private readonly object _levelsLock = new();
    private readonly byte[] _pixels;
    private readonly FrameData _frame;
    private readonly IccPixelConverter _profile;
    private int _generation;

    /// <summary>Decodes the full picture of <paramref name="data"/>, a JP2 file or a codestream.</summary>
    public Jpeg2000Bitmap(byte[] data, long maxPixels) : this(data, maxPixels, BitmapLoadOptions.Default)
    {
    }

    /// <summary>Decodes the full picture of <paramref name="data"/>, a JP2 file or a codestream, as
    /// <paramref name="options"/> say.</summary>
    public Jpeg2000Bitmap(byte[] data, long maxPixels, BitmapLoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _maxPixels = maxPixels;
        var image = Jpeg2000Decoder.Decode(data, maxPixels);
        Width = (uint)image.Width;
        Height = (uint)image.Height;
        Levels = image.Levels;
        Layers = image.Layers;
        if ((options ?? BitmapLoadOptions.Default).ApplyColorProfile && image.IccProfile != null)
        {
            _profile = IccPixels.Converter(image.IccProfile, Math.Min(image.ColorChannels, 4),
                (long)image.Width * image.Height);
        }

        ColorProfile = _profile == null ? image.IccProfile : null;
        _pixels = Rgba(image, _profile);
        _frame = new FrameData(_pixels, ImageDescription.Default2D(Width, Height, PixelFormat));
    }

    public uint Width { get; }

    public uint Height { get; }

    /// <summary>How many resolutions below the full one the picture has.</summary>
    public int Levels { get; }

    /// <summary>The picture's quality layers - the most any of its tiles has.</summary>
    public int Layers { get; }

    public SurfaceFormat PixelFormat => SurfaceFormat.R8G8B8A8.UNorm;

    public bool IsMultiFrame => false;

    public bool HasMipLevels => Levels > 0;

    public uint MipLevelsCount => Levels > 0 ? (uint)Levels + 1 : 0;

    public uint QualityLevelsCount => (uint)Math.Max(1, Layers);

    public bool DecodesLevelsOnDemand => true;

    public uint NumberOfReplays => 0;

    public uint FramesCount => 1;

    /// <inheritdoc/>
    public byte[] ColorProfile { get; }

    public byte[] GetRawPixels(uint frameIndex) => _pixels;

    public FrameData GetMipLevelData(uint mipLevel) => GetLevelData(mipLevel, 0);

    public FrameData GetLevelData(uint mipLevel, uint qualityLevel)
    {
        if (mipLevel > Levels || qualityLevel >= QualityLevelsCount)
        {
            return null;
        }

        if (mipLevel == 0 && qualityLevel == 0)
        {
            return _frame;
        }

        Lazy<FrameData> level;
        int generation;
        lock (_levelsLock)
        {
            generation = _generation;
            if (!_levels.TryGetValue((mipLevel, qualityLevel), out level))
            {
                level = new Lazy<FrameData>(() => Decode(mipLevel, qualityLevel));
                _levels[(mipLevel, qualityLevel)] = level;
            }
        }

        var frame = level.Value;
        lock (_levelsLock)
        {
            if (generation != _generation)
            {
                _levels.Remove((mipLevel, qualityLevel));
            }
        }

        return frame;
    }

    public ImageDescription GetImageDescription() => ImageDescription.Default2D(Width, Height, PixelFormat);

    public FrameData GetFrameData(uint frameIndex) => _frame;

    public void ReleaseDecodedFrames()
    {
        lock (_levelsLock)
        {
            _levels.Clear();
            _generation++;
        }
    }

    private FrameData Decode(uint mipLevel, uint qualityLevel)
    {
        var image = Jpeg2000Decoder.Decode(_data, _maxPixels, new Jpeg2000Options
        {
            DiscardLevels = (int)mipLevel,
            Layers = qualityLevel == 0 ? 0 : Layers - (int)qualityLevel,
        });
        return new FrameData(Rgba(image, _profile), ImageDescription.Default2D((uint)image.Width, (uint)image.Height,
            PixelFormat), mipLevel);
    }

    /// <summary>The picture's channels as RGBA: gray spread to the three colors, CMYK made RGB by its ink, a fourth
    /// channel of a picture that names no color space taken as opacity, the opacity unmultiplied from the colors when
    /// they carry it.</summary>
    public static byte[] Rgba(Jpeg2000Image image) => Rgba(image, null);

    private static byte[] Rgba(Jpeg2000Image image, IccPixelConverter profile)
    {
        if (profile != null)
        {
            return Managed(image, profile);
        }

        var count = image.Width * image.Height;
        var rgba = new byte[count * 4];
        var channels = image.Channels;
        var colors = image.ColorChannels;
        var samples = image.Samples;
        var cmyk = image.ColorSpace == Jpeg2000ColorSpace.Cmyk && colors >= 4;
        var opacity = image.HasAlpha ? channels - 1 : !cmyk && colors >= 4 ? 3 : -1;
        for (var i = 0; i < count; i++)
        {
            var at = i * channels;
            byte r, g, b;
            if (cmyk)
            {
                var k = 255 - samples[at + 3];
                r = (byte)((255 - samples[at]) * k / 255);
                g = (byte)((255 - samples[at + 1]) * k / 255);
                b = (byte)((255 - samples[at + 2]) * k / 255);
            }
            else if (colors >= 3)
            {
                (r, g, b) = (samples[at], samples[at + 1], samples[at + 2]);
            }
            else
            {
                r = g = b = samples[at];
            }

            var alpha = opacity >= 0 ? samples[at + opacity] : (byte)255;
            if (image.AlphaIsPremultiplied && alpha is > 0 and < 255)
            {
                r = (byte)Math.Min(255, r * 255 / alpha);
                g = (byte)Math.Min(255, g * 255 / alpha);
                b = (byte)Math.Min(255, b * 255 / alpha);
            }

            rgba[i * 4] = r;
            rgba[i * 4 + 1] = g;
            rgba[i * 4 + 2] = b;
            rgba[i * 4 + 3] = alpha;
        }

        return rgba;
    }

    private static byte[] Managed(Jpeg2000Image image, IccPixelConverter profile)
    {
        var count = image.Width * image.Height;
        var channels = image.Channels;
        var samples = image.Samples;
        var device = new byte[count * profile.Channels];
        var premultiplied = image.HasAlpha && image.AlphaIsPremultiplied;
        for (var i = 0; i < count; i++)
        {
            var alpha = premultiplied ? samples[i * channels + channels - 1] : 255;
            for (var c = 0; c < profile.Channels; c++)
            {
                var value = samples[i * channels + c];
                device[i * profile.Channels + c] = alpha == 255 ? value
                    : alpha == 0 ? (byte)0
                    : (byte)Math.Min(255, value * 255 / alpha);
            }
        }

        var rgba = new byte[count * 4];
        IccPixels.ToRgba(profile, device, false, rgba);
        if (image.HasAlpha)
        {
            for (var i = 0; i < count; i++)
            {
                rgba[i * 4 + 3] = samples[i * channels + channels - 1];
            }
        }

        return rgba;
    }
}
