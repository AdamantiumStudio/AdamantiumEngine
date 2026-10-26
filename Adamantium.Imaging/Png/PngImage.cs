using System;
using System.Collections.Generic;
using System.Linq;

namespace Adamantium.Imaging.Png;

/// <summary>A PNG picture, or an APNG animation: RGBA pixels - 8 bits a channel, 16 for a 16-bit file - and what the
/// file says besides. An animation's frames are decoded when asked for, each drawn on the canvas as APNG composes them:
/// two canvases are kept, so frames asked for in order - by one reader or two in turn - cost one frame each, and an
/// earlier one is composed again from the canvas saved every 16 frames (up to 64 MB of them) - all held until
/// <see cref="ReleaseDecodedFrames"/>.</summary>
public sealed class PngImage : IRawBitmap
{
    private readonly PngFile _file;
    private readonly byte[] _data;
    private readonly object _decodeLock = new();
    private readonly FrameComposer _composer;

    // The pixels last handed out, by frame.
    private byte[] _last;
    private int _lastFrame = -1;
    private FrameData _firstFrame;

    private PngImage(PngFile file, byte[] data)
    {
        _file = file;
        _data = data;
        Frames = file.Frames.Select(frame => frame.Control).ToArray();
        _composer = new FrameComposer(file.Width, file.Height, file.Wide ? 8 : 4, Frames.Select(frame =>
                new ComposedFrame((int)frame.X, (int)frame.Y, (int)frame.Width, (int)frame.Height, frame.Dispose switch
                {
                    PngDispose.Background => FrameDisposal.Background,
                    PngDispose.Previous => FrameDisposal.Previous,
                    _ => FrameDisposal.None
                }, frame.Blend == PngBlend.Over)).ToArray(),
            index => PngRaster.Decode(_file, _data, _file.Frames[index], (int)Frames[index].Width,
                (int)Frames[index].Height));
    }

    /// <summary>Reads the PNG in <paramref name="data"/>; the bytes are kept to decode its frames from.</summary>
    public static PngImage Read(byte[] data) => new(PngFile.Read(data), data);

    public uint Width => (uint)_file.Width;

    public uint Height => (uint)_file.Height;

    public SurfaceFormat PixelFormat => _file.Wide ? SurfaceFormat.R16G16B16A16.UNorm : SurfaceFormat.R8G8B8A8.UNorm;

    public bool IsMultiFrame => Frames.Count > 1;

    public bool HasMipLevels => false;

    public uint MipLevelsCount => 0;

    /// <summary>How many times the animation plays, 0 for forever.</summary>
    public uint NumberOfReplays => _file.Plays;

    public uint FramesCount => (uint)Frames.Count;

    /// <summary>Each frame's place, delay and composition; the still picture's one frame covers it whole.</summary>
    public IReadOnlyList<PngFrame> Frames { get; }

    /// <summary>The bit depth of each sample in the file.</summary>
    public int BitDepth => _file.BitDepth;

    public PngMetadata Metadata => _file.Metadata;

    /// <summary>The canvas as frame <paramref name="frameIndex"/> leaves it shown.</summary>
    public byte[] GetRawPixels(uint frameIndex)
    {
        if (frameIndex >= Frames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex), $"The PNG has {Frames.Count} frames.");
        }

        lock (_decodeLock)
        {
            if (_lastFrame != frameIndex || _last == null)
            {
                (_last, _lastFrame) = (Compose((int)frameIndex), (int)frameIndex);
            }

            return _last;
        }
    }

    public FrameData GetMipLevelData(uint mipLevel) => GetFrameData(0);

    public ImageDescription GetImageDescription() => ImageDescription.Default2D(Width, Height, PixelFormat);

    public FrameData GetFrameData(uint frameIndex) => frameIndex == 0
        ? _firstFrame ??= new FrameData(GetRawPixels(0), GetImageDescription())
        : new FrameData(GetRawPixels(frameIndex), GetImageDescription());

    /// <summary>Drops the decoded frames; a frame asked for again is decoded again.</summary>
    public void ReleaseDecodedFrames()
    {
        lock (_decodeLock)
        {
            _composer.Release();
            (_last, _lastFrame, _firstFrame) = (null, -1, null);
        }
    }

    // A still picture covering the canvas is its own pixels; anything else is drawn on the canvas.
    private byte[] Compose(int index)
    {
        var only = Frames[0];
        return Frames.Count == 1 && only.X == 0 && only.Y == 0 && only.Width == Width && only.Height == Height
            ? PngRaster.Decode(_file, _data, _file.Frames[0], _file.Width, _file.Height)
            : _composer.Compose(index);
    }
}
