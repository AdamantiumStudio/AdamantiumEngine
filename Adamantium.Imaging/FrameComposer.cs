using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Adamantium.Imaging;

/// <summary>How a frame of an animation is done with before the next is drawn (APNG's dispose_op, GIF's disposal
/// method).</summary>
internal enum FrameDisposal
{
    /// <summary>Left as it is.</summary>
    None,

    /// <summary>Its region cleared to transparent.</summary>
    Background,

    /// <summary>Its region given back what was under it.</summary>
    Previous
}

/// <summary>A frame's region on the canvas, how it is drawn - replacing the region or over it by its alpha - and how it
/// is done with.</summary>
internal readonly record struct ComposedFrame(int X, int Y, int Width, int Height, FrameDisposal Disposal, bool Over);

/// <summary>An animation's frames drawn on a transparent canvas, each when asked for: two canvases are kept, so frames
/// asked for in order - by one reader or two in turn - cost one frame each, and an earlier one is drawn again from the
/// canvas saved every 16 frames (up to 64 MB of them). RGBA of 8 bits a channel, or of 16 in little-endian order. Not
/// thread-safe: its owner takes turns.</summary>
internal sealed class FrameComposer
{
    private const int CheckpointStride = 16;
    private const long CheckpointBudget = 64L << 20;

    private readonly int _width;
    private readonly int _height;
    private readonly int _pixel;
    private readonly IReadOnlyList<ComposedFrame> _frames;
    private readonly Func<int, byte[]> _decode;

    // Two canvases, each kept after the frame it was drawn up to; the canvas as frame 16k + 15 leaves it, at k.
    private readonly Canvas[] _canvases = [new(), new()];
    private long _canvasUses;
    private readonly List<(byte[] Canvas, byte[] Before)> _checkpoints = new();
    private long _checkpointBytes;

    /// <summary>Frames of a <paramref name="width"/> × <paramref name="height"/> canvas of <paramref name="pixel"/>
    /// bytes a pixel (4 or 8); <paramref name="decode"/> gives a frame's own pixels, its region's size.</summary>
    public FrameComposer(int width, int height, int pixel, IReadOnlyList<ComposedFrame> frames, Func<int, byte[]> decode)
    {
        (_width, _height, _pixel, _frames, _decode) = (width, height, pixel, frames, decode);
    }

    /// <summary>The canvas as frame <paramref name="index"/> leaves it, a copy of its own.</summary>
    public byte[] Compose(int index) => (byte[])DrawnUpTo(index).Clone();

    /// <summary>The canvas as frame <paramref name="index"/> leaves it, written into <paramref name="destination"/>.
    /// </summary>
    public void ComposeInto(int index, Span<byte> destination) => DrawnUpTo(index).CopyTo(destination);

    // The kept canvas drawn up to frame index: from the kept canvas furthest along short of it, or from the nearest
    // saved one before it, or from the start.
    private byte[] DrawnUpTo(int index)
    {
        var canvas = _canvases.Where(kept => kept.Pixels != null && kept.Frame <= index).MaxBy(kept => kept.Frame);
        var saved = Math.Min((index + 1) / CheckpointStride, _checkpoints.Count) - 1;
        var savedFrame = saved * CheckpointStride + CheckpointStride - 1;
        if (canvas == null || canvas.Frame < savedFrame)
        {
            // Into the canvas used longest ago when neither kept one is short of it.
            canvas ??= _canvases.MinBy(kept => kept.Used);
            if (saved >= 0)
            {
                canvas.Pixels = (byte[])_checkpoints[saved].Canvas.Clone();
                (canvas.Frame, canvas.BeforeFrame) = (savedFrame, _checkpoints[saved].Before);
            }
            else
            {
                canvas.Pixels = new byte[(long)_width * _height * _pixel];
                (canvas.Frame, canvas.BeforeFrame) = (-1, null);
            }
        }

        canvas.Used = ++_canvasUses;
        var target = canvas.Pixels;
        while (canvas.Frame < index)
        {
            // Decoded before the canvas is touched: a frame that fails to decode leaves it as it was.
            var number = canvas.Frame + 1;
            var frame = _frames[number];
            var pixels = _decode(number);
            if (number > 0)
            {
                // The frame before is done with before this one is drawn; the first frame's Previous is Background.
                var before = _frames[number - 1];
                var disposal = before.Disposal == FrameDisposal.Previous && number == 1
                    ? FrameDisposal.Background
                    : before.Disposal;
                if (disposal == FrameDisposal.Background)
                {
                    Region(before, row => target.AsSpan(row.Start, row.Length).Clear());
                }
                else if (disposal == FrameDisposal.Previous && canvas.BeforeFrame != null)
                {
                    var previous = canvas.BeforeFrame;
                    Region(before, row => previous.AsSpan(row.Start, row.Length).CopyTo(target.AsSpan(row.Start)));
                }
            }

            canvas.BeforeFrame = frame.Disposal == FrameDisposal.Previous ? (byte[])target.Clone() : null;
            Draw(frame, pixels, target);
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

        return target;
    }

    /// <summary>Drops every canvas; frames are drawn again from the start.</summary>
    public void Release()
    {
        foreach (var canvas in _canvases)
        {
            (canvas.Pixels, canvas.Frame, canvas.BeforeFrame) = (null, -1, null);
        }

        _checkpoints.Clear();
        _checkpointBytes = 0;
    }

    private void Region(ComposedFrame frame, Action<(int Start, int Length)> row)
    {
        for (var y = 0; y < frame.Height; y++)
        {
            row(((int)((frame.Y + y) * (long)_width + frame.X) * _pixel, frame.Width * _pixel));
        }
    }

    // The frame's pixels on the canvas: replacing the region, or over it by their alpha (straight alpha).
    private void Draw(ComposedFrame frame, byte[] pixels, byte[] canvas)
    {
        var rowBytes = frame.Width * _pixel;
        for (var y = 0; y < frame.Height; y++)
        {
            var source = pixels.AsSpan(y * rowBytes, rowBytes);
            var target = canvas.AsSpan((int)((frame.Y + y) * (long)_width + frame.X) * _pixel, rowBytes);
            if (!frame.Over)
            {
                source.CopyTo(target);
            }
            else if (_pixel == 4)
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
        var from = MemoryMarshal.Cast<byte, ushort>(source);
        var to = MemoryMarshal.Cast<byte, ushort>(target);
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
