using System;
using System.IO;

namespace Adamantium.Imaging.Gif;

/// <summary>GIF's variable-length LZW (GIF89a, appendix F): codes of the minimum code size plus one bit up to 12, least
/// significant bit first, a clear code and an end code past the roots; the width grows when the next code to be made
/// no longer fits, and a full table is kept until the next clear code.</summary>
internal static class GifLzw
{
    private const int MaxCodes = 4096;

    /// <summary>Unpacks <paramref name="input"/> into <paramref name="output"/> and says how many bytes it filled -
    /// fewer when the data ends early or breaks off; what is past the output is not read.</summary>
    public static int Decode(ReadOnlySpan<byte> input, int minCodeSize, Span<byte> output) =>
        new Reader(input.ToArray(), minCodeSize).Read(output);

    /// <summary>Unpacks LZW data a part at a time, so an image is read a row at a time and nothing its size is kept.
    /// Each string is kept as the string before it and one byte, and given out from its first byte.</summary>
    public sealed class Reader(byte[] input, int minCodeSize)
    {
        private readonly int _clear = 1 << minCodeSize;
        private readonly short[] _prefix = new short[MaxCodes];
        private readonly byte[] _suffix = new byte[MaxCodes];
        private readonly byte[] _first = new byte[MaxCodes];
        private readonly short[] _length = new short[MaxCodes];
        // A string not yet given out, its first byte on top.
        private readonly byte[] _pending = new byte[MaxCodes];
        private int _pendingCount;
        private int _width = minCodeSize + 1;
        private int _next = (1 << minCodeSize) + 2;
        private int _previous = -1;
        private ulong _bits;
        private int _count;
        private int _at;
        private bool _ended;

        /// <summary>Fills <paramref name="output"/> with the indices that come next and says how many - fewer when the
        /// data ends or breaks off.</summary>
        public int Read(Span<byte> output)
        {
            var written = 0;
            while (written < output.Length)
            {
                if (_pendingCount > 0)
                {
                    output[written++] = _pending[--_pendingCount];
                    continue;
                }

                if (_ended || !Code(out var code))
                {
                    _ended = true;
                    return written;
                }

                if (code == _clear)
                {
                    (_width, _next, _previous) = (minCodeSize + 1, _clear + 2, -1);
                    continue;
                }

                // The end code, a code not yet made but the next, or a root past a byte - of a code size past 8 - end
                // the data.
                if (code == _clear + 1 || code > _next || code == _next && _previous < 0 || code < _clear && code > 255)
                {
                    _ended = true;
                    return written;
                }

                // The string, written last byte first - into the output when it fits there, else held back; a code not
                // yet made is the string before and its first byte.
                if (code < _clear)
                {
                    (_first[code], _length[code]) = ((byte)code, 1);
                }

                var length = code == _next ? _length[_previous] + 1 : _length[code];
                var direct = output.Length - written >= length;
                var at = direct ? written + length : 0;
                var walk = code;
                if (code == _next)
                {
                    Put(output, ref at, direct, _first[_previous]);
                    walk = _previous;
                }

                while (walk >= _clear)
                {
                    Put(output, ref at, direct, _suffix[walk]);
                    walk = _prefix[walk];
                }

                Put(output, ref at, direct, (byte)walk);
                if (direct)
                {
                    written += length;
                }

                var first = (byte)walk;
                if (_previous >= 0 && _next < MaxCodes)
                {
                    (_prefix[_next], _suffix[_next], _first[_next], _length[_next]) =
                        ((short)_previous, first, _first[_previous], (short)(_length[_previous] + 1));
                    _next++;
                }

                // Widened once the next code no longer fits - after every code, the first after a clear too: of a code
                // size of 1 the table starts at 4, already past 2 bits.
                if (_next >= 1 << _width && _width < 12)
                {
                    _width++;
                }

                _previous = code;
            }

            return written;
        }

        // A byte of a string written backwards: into the output before at, or onto the bytes held back.
        private void Put(Span<byte> output, ref int at, bool direct, byte value)
        {
            if (direct)
            {
                output[--at] = value;
            }
            else
            {
                _pending[_pendingCount++] = value;
            }
        }

        private bool Code(out int code)
        {
            while (_count < _width)
            {
                if (_at == input.Length)
                {
                    code = 0;
                    return false;
                }

                _bits |= (ulong)input[_at++] << _count;
                _count += 8;
            }

            code = (int)(_bits & ((1u << _width) - 1));
            _bits >>= _width;
            _count -= _width;
            return true;
        }
    }

    /// <summary>Packs <paramref name="indices"/>, each below 2^<paramref name="minCodeSize"/>, as a clear code, the
    /// codes and the end code, in sub-blocks of up to 255 bytes and the empty block that ends them.</summary>
    public static void Encode(ReadOnlySpan<byte> indices, int minCodeSize, Stream output)
    {
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        // The code of a string and one byte more, by the string's code and the byte; stamped with the table's
        // generation, so a clear code empties it without clearing it.
        var table = new int[MaxCodes << minCodeSize];
        var generation = 1;
        var width = minCodeSize + 1;
        var next = clear + 2;
        var writer = new BlockWriter(output);
        writer.Write(clear, width);
        if (indices.IsEmpty)
        {
            writer.Write(end, width);
            writer.Finish();
            return;
        }

        int prefix = indices[0];
        for (var i = 1; i < indices.Length; i++)
        {
            var symbol = indices[i];
            var key = (prefix << minCodeSize) | symbol;
            var entry = table[key];
            if (entry >> 12 == generation)
            {
                prefix = entry & (MaxCodes - 1);
                continue;
            }

            writer.Write(prefix, width);
            if (next < MaxCodes)
            {
                table[key] = (generation << 12) | next++;
                // The reader makes each code one code later than the writer: it widens when the writer's next code
                // is one past the width.
                if (next > 1 << width && width < 12)
                {
                    width++;
                }
            }
            else
            {
                writer.Write(clear, width);
                (width, next) = (minCodeSize + 1, clear + 2);
                generation++;
            }

            prefix = symbol;
        }

        writer.Write(prefix, width);
        // The reader makes a code of the last one too, and widens for the end code if that fills the width.
        if (next >= 1 << width && width < 12)
        {
            width++;
        }

        writer.Write(end, width);
        writer.Finish();
    }

    // Codes least significant bit first, in sub-blocks of 255 bytes.
    private sealed class BlockWriter(Stream output)
    {
        private readonly byte[] _block = new byte[256];
        private int _filled;
        private uint _bits;
        private int _count;

        public void Write(int code, int width)
        {
            _bits |= (uint)code << _count;
            _count += width;
            while (_count >= 8)
            {
                Put((byte)_bits);
                _bits >>= 8;
                _count -= 8;
            }
        }

        public void Finish()
        {
            if (_count > 0)
            {
                Put((byte)_bits);
            }

            Flush();
            output.WriteByte(0);
        }

        private void Put(byte value)
        {
            _block[1 + _filled++] = value;
            if (_filled == 255)
            {
                Flush();
            }
        }

        private void Flush()
        {
            if (_filled == 0)
            {
                return;
            }

            _block[0] = (byte)_filled;
            output.Write(_block, 0, _filled + 1);
            _filled = 0;
        }
    }
}
