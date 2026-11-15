using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>Reads one block's data out of the arithmetic coding of ECMA-363 clause 10; throws when it reads past the
/// data's end.</summary>
internal sealed class U3dBitReader
{
    /// <summary>How far past the end of the data the coder may look before the data is taken as cut short.</summary>
    internal const int SlackBits = 64;

    private const uint Top = 0xFFFF;
    private const uint HalfBit = 0x8000;
    private const uint QuarterBits = 0xC000;
    private const uint FirstQuarter = 0x4000;
    private const uint ByteRange = 256;

    private readonly byte[] _bytes;
    private readonly int _start;
    private readonly long _bitLength;
    private readonly bool _noCompression;
    private readonly Dictionary<string, U3dDynamicHistogram> _histograms = new(StringComparer.Ordinal);
    private long _bitPosition;
    private uint _low;
    private uint _high = Top;
    private uint _code;

    /// <summary>A reader of <paramref name="count"/> bytes of <paramref name="source"/> from <paramref name="offset"/>;
    /// with <paramref name="noCompression"/> compressed fields are read as plain values.</summary>
    internal U3dBitReader(byte[] source, int offset, int count, bool noCompression)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (offset < 0 || count < 0 || (long)offset + count > source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "The data lies outside the bytes given");
        }

        _bytes = source;
        _start = offset;
        _bitLength = (long)count * 8;
        _noCompression = noCompression;
        for (var index = 0; index < 16; index++)
        {
            _code = (_code << 1) | ReadBit();
        }
    }

    /// <summary>A reader of all of <paramref name="data"/>.</summary>
    internal U3dBitReader(byte[] data, bool noCompression = false) : this(data, 0, data?.Length ?? 0, noCompression)
    {
    }

    /// <summary>Roughly how many bits of the data are not read yet.</summary>
    internal long RemainingBits => Math.Max(0, _bitLength - _bitPosition + 16);

    /// <summary>Whether more has been read than the data holds.</summary>
    internal bool IsPastEnd => _bitPosition - 16 > _bitLength;

    /// <summary>How many whole bytes have been read; exact for data of plain values alone.</summary>
    internal long PlainBytesRead => (_bitPosition - 16) / 8;

    /// <summary>Throws when <see cref="IsPastEnd"/>.</summary>
    internal void EnsureWithinData()
    {
        if (IsPastEnd)
        {
            throw new InvalidDataException("The U3D data ends before its values do");
        }
    }

    /// <summary>Throws unless <paramref name="count"/> items of at least <paramref name="bitsEach"/> bits could still be
    /// in the data; a count read from the file is checked so before anything is allocated for it.</summary>
    internal void CheckCount(ulong count, int bitsEach, string what)
    {
        if (count > 0 && (decimal)count * Math.Max(1, bitsEach) > RemainingBits + SlackBits)
        {
            throw new InvalidDataException($"The U3D data names {count} {what}, more than its remaining bytes can hold");
        }
    }

    /// <summary>An uncompressed U8.</summary>
    internal byte ReadU8() => (byte)ReverseBits(DecodeStatic(ByteRange));

    /// <summary>An uncompressed U16, its low byte first.</summary>
    internal ushort ReadU16()
    {
        uint lowByte = ReadU8();
        uint highByte = ReadU8();
        return (ushort)(lowByte | (highByte << 8));
    }

    /// <summary>An uncompressed U32, its low U16 first.</summary>
    internal uint ReadU32()
    {
        uint lowHalf = ReadU16();
        uint highHalf = ReadU16();
        return lowHalf | (highHalf << 16);
    }

    /// <summary>An uncompressed U64, its low U32 first.</summary>
    internal ulong ReadU64()
    {
        ulong lowHalf = ReadU32();
        ulong highHalf = ReadU32();
        return lowHalf | (highHalf << 32);
    }

    /// <summary>An I16, as the U16 of the same bits.</summary>
    internal short ReadI16() => unchecked((short)ReadU16());

    /// <summary>An I32, as the U32 of the same bits.</summary>
    internal int ReadI32() => unchecked((int)ReadU32());

    /// <summary>An F32, as the U32 of the same bits.</summary>
    internal float ReadF32() => BitConverter.UInt32BitsToSingle(ReadU32());

    /// <summary>An F64, as the U64 of the same bits.</summary>
    internal double ReadF64() => BitConverter.UInt64BitsToDouble(ReadU64());

    /// <summary>A String: a U16 count of bytes, then the bytes in UTF-8.</summary>
    internal string ReadString()
    {
        var length = ReadU16();
        CheckCount(length, 8, "bytes of a string");
        var text = new byte[length];
        for (var index = 0; index < length; index++)
        {
            text[index] = ReadU8();
        }

        return Encoding.UTF8.GetString(text);
    }

    /// <summary><paramref name="count"/> uncompressed U8 values.</summary>
    internal byte[] ReadBytes(int count)
    {
        CheckCount((ulong)Math.Max(0, count), 8, "bytes");
        var read = new byte[count];
        for (var index = 0; index < count; index++)
        {
            read[index] = ReadU8();
        }

        return read;
    }

    /// <summary>A compressed U8 in <paramref name="context"/>.</summary>
    internal byte ReadCompressedU8(U3dContext context) => (byte)ReadCompressed(context, sizeof(byte));

    /// <summary>A compressed U16 in <paramref name="context"/>.</summary>
    internal ushort ReadCompressedU16(U3dContext context) => (ushort)ReadCompressed(context, sizeof(ushort));

    /// <summary>A compressed U32 in <paramref name="context"/>.</summary>
    internal uint ReadCompressedU32(U3dContext context) => ReadCompressed(context, sizeof(uint));

    private uint ReadCompressed(U3dContext context, int size)
    {
        if (_noCompression)
        {
            return ReadPlain(size);
        }

        if (!context.IsDynamic)
        {
            return context.Range > U3dContext.MaximumStaticRange ? ReadPlain(size) : DecodeStaticValue(context.Range);
        }

        var histogram = GetHistogram(context.Name);
        var symbol = DecodeDynamic(histogram);
        if (symbol != 0)
        {
            return symbol - 1;
        }

        var value = ReadPlain(size);
        histogram.Add(unchecked(value + 1u));
        return value;
    }

    private uint ReadPlain(int size) => size switch
    {
        sizeof(byte) => ReadU8(),
        sizeof(ushort) => ReadU16(),
        _ => ReadU32()
    };

    private U3dDynamicHistogram GetHistogram(string name)
    {
        if (!_histograms.TryGetValue(name, out var histogram))
        {
            histogram = new U3dDynamicHistogram();
            _histograms.Add(name, histogram);
        }

        return histogram;
    }

    private uint DecodeStaticValue(uint range)
    {
        if (range == 0)
        {
            throw new InvalidDataException("The U3D data reads a value from an empty range");
        }

        return DecodeStatic(range);
    }

    private uint DecodeStatic(uint range)
    {
        var span = _high - _low + 1;
        var cumulative = GetCumulative(range, span);
        Narrow(span, cumulative, 1, range);
        return cumulative;
    }

    private uint DecodeDynamic(U3dDynamicHistogram histogram)
    {
        var total = histogram.Total;
        var span = _high - _low + 1;
        var cumulative = GetCumulative(total, span);
        var symbol = histogram.GetSymbolAt(cumulative);
        var frequency = histogram.GetFrequency(symbol);
        if (frequency == 0)
        {
            throw new InvalidDataException("The U3D compressed data is corrupt");
        }

        Narrow(span, histogram.GetCumulativeFrequency(symbol), frequency, total);
        histogram.Add(symbol);
        return symbol;
    }

    private uint GetCumulative(uint total, uint span)
    {
        if (_code < _low || _code > _high)
        {
            throw new InvalidDataException("The U3D compressed data is corrupt");
        }

        var cumulative = (total * (_code - _low + 1) - 1) / span;
        if (cumulative >= total)
        {
            throw new InvalidDataException("The U3D compressed data is corrupt");
        }

        return cumulative;
    }

    private void Narrow(uint span, uint cumulative, uint frequency, uint total)
    {
        _high = _low + span * (cumulative + frequency) / total - 1;
        _low += span * cumulative / total;
        while (true)
        {
            if ((_high & HalfBit) == (_low & HalfBit))
            {
                _low = (_low << 1) & Top;
                _high = ((_high << 1) & Top) | 1;
                _code = ((_code << 1) & Top) | ReadBit();
            }
            else if ((_low & QuarterBits) == FirstQuarter && (_high & QuarterBits) == HalfBit)
            {
                _low = (_low - FirstQuarter) << 1;
                _high = ((_high - FirstQuarter) << 1) | 1;
                _code = ((_code - FirstQuarter) << 1) | ReadBit();
            }
            else
            {
                return;
            }
        }
    }

    private uint ReadBit()
    {
        if (_bitPosition >= _bitLength + SlackBits)
        {
            throw new InvalidDataException("The U3D data ends before its values do");
        }

        uint bit = 0;
        if (_bitPosition < _bitLength)
        {
            bit = (uint)(_bytes[_start + (int)(_bitPosition >> 3)] >> (int)(_bitPosition & 7)) & 1;
        }

        _bitPosition++;
        return bit;
    }

    private static uint ReverseBits(uint value)
    {
        var reversed = 0u;
        for (var bit = 0; bit < 8; bit++)
        {
            reversed = (reversed << 1) | ((value >> bit) & 1);
        }

        return reversed;
    }
}
