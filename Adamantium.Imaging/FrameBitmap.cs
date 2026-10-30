using System;
using System.Collections.Generic;

namespace Adamantium.Imaging;

/// <summary>Frames of pixels already in memory, as a bitmap.</summary>
public sealed class FrameBitmap : IRawBitmap
{
    private readonly IReadOnlyList<byte[]> _frames;

    public FrameBitmap(uint width, uint height, SurfaceFormat format, IReadOnlyList<byte[]> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("A bitmap has a frame at least.", nameof(frames));
        }

        Width = width;
        Height = height;
        PixelFormat = format;
        _frames = frames;
    }

    public uint Width { get; }

    public uint Height { get; }

    public SurfaceFormat PixelFormat { get; }

    public bool IsMultiFrame => _frames.Count > 1;

    public bool HasMipLevels => false;

    public uint MipLevelsCount => 0;

    public uint NumberOfReplays => 0;

    public uint FramesCount => (uint)_frames.Count;

    public byte[] GetRawPixels(uint frameIndex) => _frames[(int)frameIndex];

    public FrameData GetMipLevelData(uint mipLevel) => GetFrameData(mipLevel);

    public ImageDescription GetImageDescription() => ImageDescription.Default2D(Width, Height, PixelFormat);

    public FrameData GetFrameData(uint frameIndex) => new(GetRawPixels(frameIndex), GetImageDescription());
}
