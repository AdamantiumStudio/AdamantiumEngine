using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Adamantium.Imaging.Png;

/// <summary>A PNG frame's compressed data into RGBA pixels (PNG, 7-9): inflated, unfiltered row by row, Adam7 passes
/// put in their places, and the samples brought to RGBA - 8 bits a channel, 16 little-endian for 16-bit images;
/// low bit depths scaled to the full range, palettes and tRNS applied.</summary>
internal static class PngRaster
{
    // Adam7 (8.2): where each pass starts and how far apart its pixels are.
    private static readonly (int X, int Y, int StepX, int StepY)[] Passes =
        [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    // DEFLATE codes 258 bytes in a symbol of at least 2 bits: no stream inflates by more.
    private const long MostInflation = 1040;

    /// <summary>The <paramref name="width"/> × <paramref name="height"/> pixels of <paramref name="frame"/> of
    /// <paramref name="file"/>, whose bytes are <paramref name="data"/>.</summary>
    public static byte[] Decode(PngFile file, byte[] data, PngFrameData frame, int width, int height)
    {
        CheckCanHold(file, frame, width, height);
        var bits = file.Channels * file.BitDepth;
        var output = new byte[(long)width * height * (file.Wide ? 8 : 4)];
        using var inflater = new BufferedStream(
            new ZLibStream(new SegmentStream(data, frame.Segments), CompressionMode.Decompress), 1 << 16);
        var rowBytes = (int)(((long)width * bits + 7) / 8);
        var bytesPerPixel = Math.Max(1, bits / 8);
        var row = new byte[rowBytes + 1];
        var prior = new byte[rowBytes + 1];
        try
        {
            foreach (var pass in file.Interlaced ? Passes : [(0, 0, 1, 1)])
            {
                var (across, down) = PassSize(width, height, pass);
                if (across == 0 || down == 0)
                {
                    continue;
                }

                var length = (int)(((long)across * bits + 7) / 8) + 1;
                Array.Clear(prior);
                for (var y = 0; y < down; y++)
                {
                    inflater.ReadExactly(row, 0, length);
                    Unfilter(row[0], row.AsSpan(1, length - 1), prior.AsSpan(1, length - 1), bytesPerPixel);
                    Convert(file, row.AsSpan(1, length - 1), across, output,
                        (pass.Y + y * pass.StepY) * (long)width + pass.X, pass.StepX);
                    (row, prior) = (prior, row);
                }
            }
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The PNG image data ends before the image does.", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidDataException("The PNG image data is broken: " + exception.Message, exception);
        }

        return output;
    }

    /// <summary>Refuses <paramref name="frame"/>'s data when it could not inflate to a <paramref name="width"/> ×
    /// <paramref name="height"/> image - before anything that size is made.</summary>
    public static void CheckCanHold(PngFile file, PngFrameData frame, int width, int height)
    {
        var bits = file.Channels * file.BitDepth;
        long raw = 0;
        foreach (var pass in file.Interlaced ? Passes : [(0, 0, 1, 1)])
        {
            var (across, down) = PassSize(width, height, pass);
            raw += across == 0 || down == 0 ? 0 : down * (1 + ((long)across * bits + 7) / 8);
        }

        long compressed = 0;
        foreach (var (_, length) in frame.Segments)
        {
            compressed += length;
        }

        if (raw > MostInflation * compressed + 1024)
        {
            throw new InvalidDataException($"PNG data of {compressed} bytes cannot hold a {width}×{height} image.");
        }
    }

    private static (int Across, int Down) PassSize(int width, int height, (int X, int Y, int StepX, int StepY) pass) =>
        ((width - pass.X + pass.StepX - 1) / pass.StepX, (height - pass.Y + pass.StepY - 1) / pass.StepY);

    // 9.2: the filter of a row undone against the row before it (zeros for the first), bytesPerPixel to the left.
    private static void Unfilter(byte filter, Span<byte> row, ReadOnlySpan<byte> prior, int bytesPerPixel)
    {
        switch (filter)
        {
            case 0:
                return;
            case 1:
            {
                ref var at = ref MemoryMarshal.GetReference(row);
                for (var i = bytesPerPixel; i < row.Length; i++)
                {
                    Unsafe.Add(ref at, i) += Unsafe.Add(ref at, i - bytesPerPixel);
                }

                return;
            }
            case 2:
            {
                var i = 0;
                var size = Vector<byte>.Count;
                for (; i + size <= row.Length; i += size)
                {
                    (new Vector<byte>(row[i..]) + new Vector<byte>(prior[i..])).CopyTo(row[i..]);
                }

                for (; i < row.Length; i++)
                {
                    row[i] += prior[i];
                }

                return;
            }
            case 3:
            {
                for (var i = 0; i < bytesPerPixel && i < row.Length; i++)
                {
                    row[i] += (byte)(prior[i] >> 1);
                }

                ref var at = ref MemoryMarshal.GetReference(row);
                ref var above = ref MemoryMarshal.GetReference(prior);
                for (var i = bytesPerPixel; i < row.Length; i++)
                {
                    Unsafe.Add(ref at, i) += (byte)((Unsafe.Add(ref at, i - bytesPerPixel) + Unsafe.Add(ref above, i)) >> 1);
                }

                return;
            }
            case 4:
            {
                if (bytesPerPixel is 2 or 3 or 4 or 6 or 8 && Vector128.IsHardwareAccelerated)
                {
                    PaethPixels(row, prior, bytesPerPixel);
                    return;
                }

                for (var i = 0; i < bytesPerPixel && i < row.Length; i++)
                {
                    row[i] += prior[i];
                }

                ref var at = ref MemoryMarshal.GetReference(row);
                ref var above = ref MemoryMarshal.GetReference(prior);
                for (var i = bytesPerPixel; i < row.Length; i++)
                {
                    Unsafe.Add(ref at, i) += Paeth(Unsafe.Add(ref at, i - bytesPerPixel), Unsafe.Add(ref above, i),
                        Unsafe.Add(ref above, i - bytesPerPixel));
                }

                return;
            }
            default:
                throw new InvalidDataException($"A PNG row of filter type {filter}.");
        }
    }

    // Paeth a pixel at a time, its bytes side by side: each pixel waits on the one to its left, its bytes on nothing.
    // Distances as 9.4 has them: |p - left| = |above - upperLeft|, |p - above| = |left - upperLeft|,
    // |p - upperLeft| = |left + above - 2 upperLeft|.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void PaethPixels(Span<byte> row, ReadOnlySpan<byte> prior, int bytesPerPixel)
    {
        ref var at = ref MemoryMarshal.GetReference(row);
        ref var over = ref MemoryMarshal.GetReference(prior);
        var left = Vector128<short>.Zero;
        var upperLeft = Vector128<short>.Zero;
        var mask = Vector128.Create((short)0xFF);
        for (var offset = 0; offset + bytesPerPixel <= row.Length; offset += bytesPerPixel)
        {
            var above = ReadPixel(ref Unsafe.Add(ref over, offset), bytesPerPixel);
            var filtered = ReadPixel(ref Unsafe.Add(ref at, offset), bytesPerPixel);
            var toLeft = Vector128.Abs(above - upperLeft);
            var toAbove = Vector128.Abs(left - upperLeft);
            var toUpperLeft = Vector128.Abs(left + above - upperLeft - upperLeft);
            var useLeft = Vector128.LessThanOrEqual(toLeft, toAbove) & Vector128.LessThanOrEqual(toLeft, toUpperLeft);
            var useAbove = Vector128.LessThanOrEqual(toAbove, toUpperLeft);
            var predictor = Vector128.ConditionalSelect(useLeft, left,
                Vector128.ConditionalSelect(useAbove, above, upperLeft));
            var pixel = (filtered + predictor) & mask;
            WritePixel(ref Unsafe.Add(ref at, offset), bytesPerPixel,
                Vector128.Narrow(pixel.AsUInt16(), pixel.AsUInt16()).AsUInt64().ToScalar());
            (left, upperLeft) = (pixel, above);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> ReadPixel(ref byte first, int bytesPerPixel)
    {
        var bytes = bytesPerPixel switch
        {
            2 => Unsafe.ReadUnaligned<ushort>(ref first),
            3 => Unsafe.ReadUnaligned<ushort>(ref first) | ((ulong)Unsafe.Add(ref first, 2) << 16),
            4 => Unsafe.ReadUnaligned<uint>(ref first),
            6 => Unsafe.ReadUnaligned<uint>(ref first) | ((ulong)Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref first, 4)) << 32),
            _ => Unsafe.ReadUnaligned<ulong>(ref first)
        };
        return Vector128.WidenLower(Vector128.CreateScalar(bytes).AsByte()).AsInt16();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WritePixel(ref byte first, int bytesPerPixel, ulong bytes)
    {
        switch (bytesPerPixel)
        {
            case 2:
                Unsafe.WriteUnaligned(ref first, (ushort)bytes);
                break;
            case 3:
                Unsafe.WriteUnaligned(ref first, (ushort)bytes);
                Unsafe.Add(ref first, 2) = (byte)(bytes >> 16);
                break;
            case 4:
                Unsafe.WriteUnaligned(ref first, (uint)bytes);
                break;
            case 6:
                Unsafe.WriteUnaligned(ref first, (uint)bytes);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref first, 4), (ushort)(bytes >> 32));
                break;
            default:
                Unsafe.WriteUnaligned(ref first, bytes);
                break;
        }
    }

    // 9.4: the one of left, above and upper-left nearest to left + above - upper-left, in that order on ties.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static byte Paeth(byte left, byte above, byte upperLeft)
    {
        var estimate = left + above - upperLeft;
        var toLeft = Math.Abs(estimate - left);
        var toAbove = Math.Abs(estimate - above);
        var toUpperLeft = Math.Abs(estimate - upperLeft);
        return toLeft <= toAbove && toLeft <= toUpperLeft ? left : toAbove <= toUpperLeft ? above : upperLeft;
    }

    // Count samples of a row into RGBA pixels from pixel first of the output, step pixels apart.
    private static void Convert(PngFile file, ReadOnlySpan<byte> row, int count, byte[] output, long first, int step)
    {
        if (file.Wide)
        {
            Wide(file, row, count, output, first, step);
            return;
        }

        var key = file.TransparentKey;
        var into = output.AsSpan((int)(first * 4));
        switch (file.ColorType, file.BitDepth)
        {
            case (6, 8) when step == 1:
                row[..(count * 4)].CopyTo(into);
                return;
            case (6, 8):
                for (var i = 0; i < count; i++)
                {
                    row.Slice(i * 4, 4).CopyTo(into[(i * step * 4)..]);
                }

                return;
            case (2, 8):
            {
                var i = 0;
                if (key == null && step == 1 && Vector128.IsHardwareAccelerated)
                {
                    ref var from = ref MemoryMarshal.GetReference(row);
                    ref var to = ref MemoryMarshal.GetReference(into);
                    var spread = Vector128.Create((byte)0, 1, 2, 255, 3, 4, 5, 255, 6, 7, 8, 255, 9, 10, 11, 255);
                    var opaque = Vector128.Create(0xFF000000u).AsByte();
                    for (; i + 6 <= count; i += 4)
                    {
                        (Vector128.Shuffle(Vector128.LoadUnsafe(ref from, (nuint)(i * 3)), spread) | opaque)
                            .StoreUnsafe(ref to, (nuint)(i * 4));
                    }
                }

                for (; i < count; i++)
                {
                    var at = i * step * 4;
                    byte r = row[i * 3], g = row[i * 3 + 1], b = row[i * 3 + 2];
                    (into[at], into[at + 1], into[at + 2]) = (r, g, b);
                    into[at + 3] = key != null && r == key[0] && g == key[1] && b == key[2] ? (byte)0 : (byte)255;
                }

                return;
            }
            case (4, 8):
                for (var i = 0; i < count; i++)
                {
                    var at = i * step * 4;
                    into[at] = into[at + 1] = into[at + 2] = row[i * 2];
                    into[at + 3] = row[i * 2 + 1];
                }

                return;
            case (3, _):
            {
                var palette = MemoryMarshal.Cast<byte, uint>(file.Palette.AsSpan());
                var depth = file.BitDepth;
                var mask = (1 << depth) - 1;
                if (depth == 8 && step == 1 && palette.Length == 256)
                {
                    ref var entries = ref MemoryMarshal.GetReference(palette);
                    ref var pixels = ref Unsafe.As<byte, uint>(ref MemoryMarshal.GetReference(into));
                    for (var i = 0; i < count; i++)
                    {
                        Unsafe.Add(ref pixels, i) = Unsafe.Add(ref entries, row[i]);
                    }

                    return;
                }

                for (var i = 0; i < count; i++)
                {
                    var index = depth == 8 ? row[i] : (row[i * depth / 8] >> (8 - depth - i * depth % 8)) & mask;
                    MemoryMarshal.Write(into[(i * step * 4)..], in palette[index]);
                }

                return;
            }
            default:
            {
                // Gray of 1 to 8 bits, scaled to 8 by its largest value; tRNS compares the sample as written.
                var depth = file.BitDepth;
                var mask = (1 << depth) - 1;
                for (var i = 0; i < count; i++)
                {
                    var value = depth == 8 ? row[i] : (row[i * depth / 8] >> (8 - depth - i * depth % 8)) & mask;
                    var at = i * step * 4;
                    into[at] = into[at + 1] = into[at + 2] = (byte)(value * 255 / mask);
                    into[at + 3] = key != null && value == key[0] ? (byte)0 : (byte)255;
                }

                return;
            }
        }
    }

    // 16-bit samples, big-endian in the file, into RGBA of 16 bits little-endian.
    private static void Wide(PngFile file, ReadOnlySpan<byte> row, int count, byte[] output, long first, int step)
    {
        var channels = file.Channels;
        var key = file.TransparentKey;
        var into = MemoryMarshal.Cast<byte, ushort>(output.AsSpan((int)(first * 8)));
        if (file.ColorType == 0 && key == null && BitConverter.IsLittleEndian)
        {
            var gray = MemoryMarshal.Cast<byte, ushort>(row[..(count * 2)]);
            for (var i = 0; i < count; i++)
            {
                var value = BinaryPrimitives.ReverseEndianness(gray[i]);
                var at = i * step * 4;
                (into[at], into[at + 1], into[at + 2], into[at + 3]) = (value, value, value, ushort.MaxValue);
            }

            return;
        }

        for (var i = 0; i < count; i++)
        {
            var at = i * step * 4;
            var sample = row[(i * channels * 2)..];
            ushort r, g, b, a;
            switch (file.ColorType)
            {
                case 0:
                    r = g = b = BinaryPrimitives.ReadUInt16BigEndian(sample);
                    a = key != null && r == key[0] ? (ushort)0 : ushort.MaxValue;
                    break;
                case 4:
                    r = g = b = BinaryPrimitives.ReadUInt16BigEndian(sample);
                    a = BinaryPrimitives.ReadUInt16BigEndian(sample[2..]);
                    break;
                case 2:
                    (r, g, b) = (BinaryPrimitives.ReadUInt16BigEndian(sample),
                        BinaryPrimitives.ReadUInt16BigEndian(sample[2..]), BinaryPrimitives.ReadUInt16BigEndian(sample[4..]));
                    a = key != null && r == key[0] && g == key[1] && b == key[2] ? (ushort)0 : ushort.MaxValue;
                    break;
                default:
                    (r, g, b, a) = (BinaryPrimitives.ReadUInt16BigEndian(sample),
                        BinaryPrimitives.ReadUInt16BigEndian(sample[2..]), BinaryPrimitives.ReadUInt16BigEndian(sample[4..]),
                        BinaryPrimitives.ReadUInt16BigEndian(sample[6..]));
                    break;
            }

            if (!BitConverter.IsLittleEndian)
            {
                (r, g, b, a) = (BinaryPrimitives.ReverseEndianness(r), BinaryPrimitives.ReverseEndianness(g),
                    BinaryPrimitives.ReverseEndianness(b), BinaryPrimitives.ReverseEndianness(a));
            }

            (into[at], into[at + 1], into[at + 2], into[at + 3]) = (r, g, b, a);
        }
    }

    /// <summary>The data of a frame's chunks read as one stream, without copying them together.</summary>
    private sealed class SegmentStream(byte[] data, System.Collections.Generic.List<(int Start, int Length)> segments)
        : Stream
    {
        private int _segment;
        private int _offset;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            while (_segment < segments.Count && _offset == segments[_segment].Length)
            {
                (_segment, _offset) = (_segment + 1, 0);
            }

            if (_segment == segments.Count)
            {
                return 0;
            }

            var (start, length) = segments[_segment];
            var taken = Math.Min(buffer.Length, length - _offset);
            data.AsSpan(start + _offset, taken).CopyTo(buffer);
            _offset += taken;
            return taken;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
