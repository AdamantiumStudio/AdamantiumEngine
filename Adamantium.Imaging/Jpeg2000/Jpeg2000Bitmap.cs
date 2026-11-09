using System;
using System.Collections.Generic;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>A JPEG 2000 picture as RGBA: the full one decoded at once, its smaller resolutions as mip levels and its
/// coarser qualities as quality levels, each decoded from the same data the first time it is asked for and kept.
/// </summary>
public sealed class Jpeg2000Bitmap : IRawBitmap
{
    private readonly byte[] _data;
    private readonly long _maxPixels;
    private readonly Dictionary<(uint Mip, uint Quality), FrameData> _levels = [];
    private readonly object _levelsLock = new();
    private readonly byte[] _pixels;

    /// <summary>Decodes the full picture of <paramref name="data"/>, a JP2 file or a codestream.</summary>
    public Jpeg2000Bitmap(byte[] data, long maxPixels)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _maxPixels = maxPixels;
        var image = Jpeg2000Decoder.Decode(data, maxPixels);
        Width = (uint)image.Width;
        Height = (uint)image.Height;
        Levels = image.Levels;
        Layers = image.Layers;
        _pixels = Rgba(image);
    }

    public uint Width { get; }

    public uint Height { get; }

    /// <summary>How many resolutions below the full one the picture has.</summary>
    public int Levels { get; }

    /// <summary>The picture's quality layers.</summary>
    public int Layers { get; }

    public SurfaceFormat PixelFormat => SurfaceFormat.R8G8B8A8.UNorm;

    public bool IsMultiFrame => false;

    public bool HasMipLevels => Levels > 0;

    public uint MipLevelsCount => (uint)Levels + 1;

    public uint QualityLevelsCount => (uint)Math.Max(1, Layers);

    public uint NumberOfReplays => 0;

    public uint FramesCount => 1;

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
            return GetFrameData(0);
        }

        lock (_levelsLock)
        {
            if (_levels.TryGetValue((mipLevel, qualityLevel), out var kept))
            {
                return kept;
            }
        }

        var image = Jpeg2000Decoder.Decode(_data, _maxPixels, new Jpeg2000Options
        {
            DiscardLevels = (int)mipLevel,
            Layers = Layers - (int)qualityLevel,
        });
        var frame = new FrameData(Rgba(image),
            ImageDescription.Default2D((uint)image.Width, (uint)image.Height, PixelFormat), mipLevel);
        lock (_levelsLock)
        {
            return _levels.TryAdd((mipLevel, qualityLevel), frame) ? frame : _levels[(mipLevel, qualityLevel)];
        }
    }

    public ImageDescription GetImageDescription() => ImageDescription.Default2D(Width, Height, PixelFormat) with
    {
        MipLevels = MipLevelsCount,
    };

    public FrameData GetFrameData(uint frameIndex) =>
        new(_pixels, ImageDescription.Default2D(Width, Height, PixelFormat));

    public void ReleaseDecodedFrames()
    {
        lock (_levelsLock)
        {
            _levels.Clear();
        }
    }

    /// <summary>The picture's channels as RGBA: gray spread to the three colors, CMYK made RGB by its ink, the opacity
    /// unmultiplied from the colors when they carry it.</summary>
    public static byte[] Rgba(Jpeg2000Image image)
    {
        var count = image.Width * image.Height;
        var rgba = new byte[count * 4];
        var channels = image.Channels;
        var colors = image.ColorChannels;
        var samples = image.Samples;
        for (var i = 0; i < count; i++)
        {
            var at = i * channels;
            byte r, g, b;
            if (colors >= 4)
            {
                var k = 255 - samples[at + 3];
                r = (byte)((255 - samples[at]) * k / 255);
                g = (byte)((255 - samples[at + 1]) * k / 255);
                b = (byte)((255 - samples[at + 2]) * k / 255);
            }
            else if (colors == 3)
            {
                (r, g, b) = (samples[at], samples[at + 1], samples[at + 2]);
            }
            else
            {
                r = g = b = samples[at];
            }

            var alpha = image.HasAlpha ? samples[at + channels - 1] : (byte)255;
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
}
