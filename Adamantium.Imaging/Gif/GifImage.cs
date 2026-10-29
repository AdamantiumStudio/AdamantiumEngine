using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Adamantium.Imaging.Gif;

/// <summary>A GIF picture or animation (GIF89a, GIF87a) as RGBA of 8 bits a channel, transparent where the file is.
/// Frames are decoded when asked for and drawn on a transparent canvas as browsers draw them - a frame cleared to the
/// background is cleared to transparent; two canvases are kept, so frames asked for in order cost one frame each, and
/// an earlier one is drawn again from the canvas saved every 16 frames (up to 64 MB of them) - all held until
/// <see cref="ReleaseDecodedFrames"/>. A file that breaks off keeps the frames before it, a frame that breaks off the
/// pixels it has.</summary>
public sealed class GifImage : IRawBitmap
{
    private readonly GifFile _file;
    private readonly object _decodeLock = new();
    private readonly ComposedFrame[] _regions;
    private readonly FrameComposer _composer;

    // The pixels last handed out, by frame.
    private byte[] _last;
    private int _lastFrame = -1;
    private FrameData _firstFrame;

    private GifImage(GifFile file)
    {
        _file = file;
        Frames = file.Images.Select(image => new GifFrame((uint)image.X, (uint)image.Y, (uint)image.Width,
            (uint)image.Height, image.Delay, image.Disposal)).ToArray();
        // Each image's region on the screen, cut to it.
        _regions = file.Images.Select(image =>
        {
            int x = Math.Min(image.X, file.Width), y = Math.Min(image.Y, file.Height);
            return new ComposedFrame(x, y, Math.Min(image.X + image.Width, file.Width) - x,
                Math.Min(image.Y + image.Height, file.Height) - y, image.Disposal switch
                {
                    GifDisposal.Background => FrameDisposal.Background,
                    GifDisposal.Previous => FrameDisposal.Previous,
                    _ => FrameDisposal.None
                }, true);
        }).ToArray();
        _composer = new FrameComposer(file.Width, file.Height, 4, _regions, Decode);
    }

    /// <summary>Reads the GIF in <paramref name="data"/>.</summary>
    public static GifImage Read(ReadOnlySpan<byte> data) => new(GifFile.Read(data));

    public uint Width => (uint)_file.Width;

    public uint Height => (uint)_file.Height;

    public SurfaceFormat PixelFormat => SurfaceFormat.R8G8B8A8.UNorm;

    public bool IsMultiFrame => Frames.Count > 1;

    public bool HasMipLevels => false;

    public uint MipLevelsCount => 0;

    /// <summary>How many times the animation plays, 0 for forever; once when the file does not say.</summary>
    public uint NumberOfReplays => _file.Plays;

    public uint FramesCount => (uint)Frames.Count;

    /// <summary>Each frame's place as the file gives it, its delay and its disposal.</summary>
    public IReadOnlyList<GifFrame> Frames { get; }

    /// <summary>The file's comments.</summary>
    public IReadOnlyList<string> Comments => _file.Comments;

    /// <summary>The canvas as frame <paramref name="frameIndex"/> leaves it shown.</summary>
    public byte[] GetRawPixels(uint frameIndex)
    {
        Check(frameIndex);
        lock (_decodeLock)
        {
            if (_lastFrame != frameIndex || _last == null)
            {
                (_last, _lastFrame) = (_composer.Compose((int)frameIndex), (int)frameIndex);
            }

            return _last;
        }
    }

    /// <summary>The canvas as frame <paramref name="frameIndex"/> leaves it, written into
    /// <paramref name="destination"/> without a copy kept.</summary>
    public void CopyFrame(uint frameIndex, Span<byte> destination)
    {
        Check(frameIndex);
        lock (_decodeLock)
        {
            if (_lastFrame == frameIndex && _last != null)
            {
                _last.CopyTo(destination);
                return;
            }

            _composer.ComposeInto((int)frameIndex, destination);
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

    private void Check(uint frameIndex)
    {
        if (frameIndex >= Frames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex), $"The GIF has {Frames.Count} frames.");
        }
    }

    // An image's pixels in its region on the screen: its colors, transparent at its transparent index and where its
    // data ends before it does.
    private byte[] Decode(int index)
    {
        var image = _file.Images[index];
        var region = _regions[index];
        var decodable = region.Width == 0 ? 0
            : (int)Math.Min(image.Height, (image.Data.Length * GifFile.MostIndicesPerByte + image.Width - 1) / image.Width);
        var rows = 0;
        var reach = 0;
        for (var row = 0; row < image.Height && region.Width > 0; row++)
        {
            var y = image.Y + (image.Interlaced ? InterlacedRow(row, image.Height) : row);
            if (y >= region.Y && y < region.Y + region.Height)
            {
                rows = row + 1;
                if (row < decodable)
                {
                    reach = Math.Max(reach, y - region.Y + 1);
                }
            }
        }

        Span<uint> colors = stackalloc uint[256];
        for (var i = 0; i < 256; i++)
        {
            colors[i] = image.Palette == null ? 0xFF000000u | (uint)(i * 0x010101)
                : i * 3 + 2 < image.Palette.Length
                    ? 0xFF000000u | image.Palette[i * 3] | (uint)image.Palette[i * 3 + 1] << 8
                      | (uint)image.Palette[i * 3 + 2] << 16
                    : 0xFF000000u;
        }

        if (image.Transparent >= 0)
        {
            colors[image.Transparent] = 0;
        }

        var rowBytes = region.Width * 4;
        var pixels = new byte[reach * rowBytes];
        var left = region.X - image.X;
        var indices = new byte[image.Width * Math.Min(rows, decodable)];
        var given = new GifLzwReader(image.Data, image.MinCodeSize).Read(indices);
        for (var row = 0; row < rows && row * image.Width < given; row++)
        {
            var line = indices.AsSpan(row * image.Width, Math.Min(image.Width, given - row * image.Width));
            var y = image.Y + (image.Interlaced ? InterlacedRow(row, image.Height) : row) - region.Y;
            if (y >= 0 && y < reach && line.Length > left)
            {
                var to = MemoryMarshal.Cast<byte, uint>(pixels.AsSpan(y * rowBytes, rowBytes));
                for (var x = 0; x < region.Width && left + x < line.Length; x++)
                {
                    to[x] = colors[line[left + x]];
                }
            }
        }

        return pixels;
    }

    // The image row the rowth row of interlaced data is (GIF89a, appendix E): every 8th from 0, every 8th from 4,
    // every 4th from 2, every 2nd from 1.
    private static int InterlacedRow(int row, int height)
    {
        var first = (height + 7) / 8;
        if (row < first)
        {
            return row * 8;
        }

        row -= first;
        var second = (height + 3) / 8;
        if (row < second)
        {
            return 4 + row * 8;
        }

        row -= second;
        var third = (height + 1) / 4;
        return row < third ? 2 + row * 4 : 1 + (row - third) * 2;
    }
}
