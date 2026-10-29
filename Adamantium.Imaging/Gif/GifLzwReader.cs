using System;
using System.Runtime.CompilerServices;

namespace Adamantium.Imaging.Gif;

/// <summary>Unpacks GIF LZW data (<see cref="GifLzw"/>) a part at a time. Each string is the string before it and one
/// byte; where a string was already given out in the part being filled, it is copied from there whole, else made from
/// its last byte back by the string before.</summary>
internal sealed class GifLzwReader(byte[] input, int minCodeSize)
{
    private readonly int _clear = 1 << minCodeSize;
    private readonly short[] _prefix = new short[GifLzw.MaxCodes];
    private readonly byte[] _suffix = new byte[GifLzw.MaxCodes];
    private readonly byte[] _first = new byte[GifLzw.MaxCodes];
    private readonly short[] _length = new short[GifLzw.MaxCodes];
    private readonly int[] _start = new int[GifLzw.MaxCodes];
    private readonly byte[] _pending = new byte[GifLzw.MaxCodes];
    private int _pendingCount;
    private int _width = minCodeSize + 1;
    private int _next = (1 << minCodeSize) + 2;
    private int _previous = -1;
    private long _previousStart = -1;
    private long _given;
    private ulong _bits;
    private int _count;
    private int _at;
    private bool _ended;

    /// <summary>Fills <paramref name="output"/> with the indices that come next and says how many - fewer when the data
    /// ends or breaks off.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int Read(Span<byte> output)
    {
        var written = 0;
        var partStart = _given;
        while (written < output.Length)
        {
            if (_pendingCount > 0)
            {
                output[written++] = _pending[--_pendingCount];
                _given++;
                continue;
            }

            if (_ended || !Code(out var code))
            {
                _ended = true;
                break;
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
                break;
            }

            if (code < _clear)
            {
                (_first[code], _length[code]) = ((byte)code, 1);
            }

            var length = code == _next ? _length[_previous] + 1 : _length[code];
            var start = _given;
            var source = code == _next ? _previousStart : code < _clear ? -1 : _start[code];
            if (output.Length - written < length)
            {
                Spell(output, written, code, false);
            }
            else
            {
                if (source >= partStart)
                {
                    var from = (int)(source - partStart);
                    var copied = code == _next ? length - 1 : length;
                    ref var origin = ref output[from];
                    ref var to = ref output[written];
                    if (copied > 16)
                    {
                        output.Slice(from, copied).CopyTo(output[written..]);
                    }
                    else
                    {
                        for (var i = 0; i < copied; i++)
                        {
                            Unsafe.Add(ref to, i) = Unsafe.Add(ref origin, i);
                        }
                    }

                    if (code == _next)
                    {
                        Unsafe.Add(ref to, copied) = origin;
                    }
                }
                else if (code < _clear)
                {
                    output[written] = (byte)code;
                }
                else
                {
                    Spell(output, written + length, code, true);
                }

                written += length;
                _given += length;
            }

            var first = code == _next ? _first[_previous] : _first[code];
            if (_previous >= 0 && _next < GifLzw.MaxCodes)
            {
                (_prefix[_next], _suffix[_next], _first[_next], _length[_next], _start[_next]) =
                    ((short)_previous, first, _first[_previous], (short)(_length[_previous] + 1),
                        _previousStart < int.MaxValue ? (int)_previousStart : -1);
                _next++;
            }

            // Widened once the next code no longer fits - after every code, the first after a clear too: of a code
            // size of 1 the table starts at 4, already past 2 bits.
            if (_next >= 1 << _width && _width < 12)
            {
                _width++;
            }

            _previous = code;
            _previousStart = start;
        }

        return written;
    }

    private void Spell(Span<byte> output, int end, int code, bool direct)
    {
        var at = end;
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
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
