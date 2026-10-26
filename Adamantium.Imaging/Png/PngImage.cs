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
    private const int CheckpointStride = 16;
    private const long CheckpointBudget = 64L << 20;

    private readonly PngFile _file;
    private readonly byte[] _data;
    private readonly object _decodeLock = new();

    // Two canvases, each kept after the frame it was drawn up to so the next is drawn on it - two readers going on in
    // turn each keep theirs; the pixels last handed out, by frame; the canvas as frame 16k + 15 leaves it, at k, to go
    // back to.
    private readonly Canvas[] _canvases = [new(), new()];
    private long _canvasUses;
    private byte[] _last;
    private int _lastFrame = -1;
    private FrameData _firstFrame;
    private readonly List<(byte[] Canvas, byte[] Before)> _checkpoints = new();
    private long _checkpointBytes;

    private PngImage(PngFile file, byte[] data)
    {
        _file = file;
        _data = data;
        Frames = file.Frames.Select(frame => frame.Control).ToArray();
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
            foreach (var canvas in _canvases)
            {
                (canvas.Pixels, canvas.Frame, canvas.BeforeFrame) = (null, -1, null);
            }

            (_last, _lastFrame, _firstFrame) = (null, -1, null);
            _checkpoints.Clear();
            _checkpointBytes = 0;
        }
    }

    // The canvas after frame index: drawn on from the kept canvas furthest along short of it, or from the nearest saved
    // one before it, or from the start - into the canvas used longest ago when neither kept one is short of it.
    private byte[] Compose(int index)
    {
        var pixel = _file.Wide ? 8 : 4;
        var only = Frames[0];
        if (Frames.Count == 1 && only.X == 0 && only.Y == 0 && only.Width == Width && only.Height == Height)
        {
            return PngRaster.Decode(_file, _data, _file.Frames[0], _file.Width, _file.Height);
        }

        var canvas = _canvases.Where(kept => kept.Pixels != null && kept.Frame <= index).MaxBy(kept => kept.Frame);
        var saved = Math.Min((index + 1) / CheckpointStride, _checkpoints.Count) - 1;
        var savedFrame = saved * CheckpointStride + CheckpointStride - 1;
        if (canvas == null || canvas.Frame < savedFrame)
        {
            canvas ??= _canvases.MinBy(kept => kept.Used);
            if (saved >= 0)
            {
                canvas.Pixels = (byte[])_checkpoints[saved].Canvas.Clone();
                (canvas.Frame, canvas.BeforeFrame) = (savedFrame, _checkpoints[saved].Before);
            }
            else
            {
                canvas.Pixels = new byte[(long)_file.Width * _file.Height * pixel];
                (canvas.Frame, canvas.BeforeFrame) = (-1, null);
            }
        }

        canvas.Used = ++_canvasUses;
        var target = canvas.Pixels;
        while (canvas.Frame < index)
        {
            // Decoded before the canvas is touched: a frame that fails to decode leaves it as it was.
            var number = canvas.Frame + 1;
            var frame = Frames[number];
            var pixels = PngRaster.Decode(_file, _data, _file.Frames[number], (int)frame.Width, (int)frame.Height);
            if (number > 0)
            {
                // The frame before is disposed of before this one is drawn (APNG, fcTL); the first frame's Previous is
                // Background.
                var before = Frames[number - 1];
                var dispose = before.Dispose == PngDispose.Previous && number == 1 ? PngDispose.Background : before.Dispose;
                if (dispose == PngDispose.Background)
                {
                    Region(before, row => target.AsSpan(row.Start, row.Length).Clear());
                }
                else if (dispose == PngDispose.Previous && canvas.BeforeFrame != null)
                {
                    var previous = canvas.BeforeFrame;
                    Region(before, row => previous.AsSpan(row.Start, row.Length).CopyTo(target.AsSpan(row.Start)));
                }
            }

            canvas.BeforeFrame = frame.Dispose == PngDispose.Previous ? (byte[])target.Clone() : null;
            Draw(frame, pixels, target, pixel);
            canvas.Frame = number;

            // The before-frame canvas is never written to, so a checkpoint shares it.
            var size = target.LongLength + (canvas.BeforeFrame?.LongLength ?? 0);
            if ((number + 1) % CheckpointStride == 0 && _checkpoints.Count == number / CheckpointStride
                && _checkpointBytes + size <= CheckpointBudget)
            {
                _checkpoints.Add(((byte[])target.Clone(), canvas.BeforeFrame));
                _checkpointBytes += size;
            }
        }

        return (byte[])target.Clone();
    }

    private void Region(PngFrame frame, Action<(int Start, int Length)> row)
    {
        var pixel = _file.Wide ? 8 : 4;
        for (var y = 0; y < frame.Height; y++)
        {
            row(((int)((frame.Y + y) * (long)_file.Width + frame.X) * pixel, (int)frame.Width * pixel));
        }
    }

    // The frame's pixels on the canvas: replacing the region, or over it by their alpha (APNG's "over", straight
    // alpha).
    private void Draw(PngFrame frame, byte[] pixels, byte[] canvas, int pixel)
    {
        var rowBytes = (int)frame.Width * pixel;
        for (var y = 0; y < frame.Height; y++)
        {
            var source = pixels.AsSpan(y * rowBytes, rowBytes);
            var target = canvas.AsSpan((int)((frame.Y + y) * (long)_file.Width + frame.X) * pixel, rowBytes);
            if (frame.Blend == PngBlend.Source)
            {
                source.CopyTo(target);
            }
            else if (pixel == 4)
            {
                Over(source, target);
            }
            else
            {
                OverWide(source, target);
            }
        }
    }

    private static void Over(ReadOnlySpan<byte> source, Span<byte> target)
    {
        for (var i = 0; i < source.Length; i += 4)
        {
            int alpha = source[i + 3];
            if (alpha == 255)
            {
                source.Slice(i, 4).CopyTo(target[i..]);
                continue;
            }

            if (alpha == 0)
            {
                continue;
            }

            int under = target[i + 3];
            var result = alpha * 255 + under * (255 - alpha);
            for (var c = 0; c < 3; c++)
            {
                target[i + c] = (byte)((source[i + c] * alpha * 255 + target[i + c] * under * (255 - alpha)
                                        + result / 2) / result);
            }

            target[i + 3] = (byte)((result + 127) / 255);
        }
    }

    private static void OverWide(ReadOnlySpan<byte> source, Span<byte> target)
    {
        var from = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(source);
        var to = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(target);
        for (var i = 0; i < from.Length; i += 4)
        {
            long alpha = from[i + 3];
            if (alpha == 65535)
            {
                from.Slice(i, 4).CopyTo(to[i..]);
                continue;
            }

            if (alpha == 0)
            {
                continue;
            }

            long under = to[i + 3];
            var result = alpha * 65535 + under * (65535 - alpha);
            for (var c = 0; c < 3; c++)
            {
                to[i + c] = (ushort)((from[i + c] * alpha * 65535 + to[i + c] * under * (65535 - alpha) + result / 2)
                                     / result);
            }

            to[i + 3] = (ushort)((result + 32767) / 65535);
        }
    }

    // A canvas after the frame of that number - and, when that frame is to be disposed of as Previous, the canvas
    // before it.
    private sealed class Canvas
    {
        public byte[] Pixels;
        public int Frame = -1;
        public byte[] BeforeFrame;
        public long Used;
    }
}
