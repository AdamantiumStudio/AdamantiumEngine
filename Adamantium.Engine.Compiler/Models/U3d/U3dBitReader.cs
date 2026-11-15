using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>Reads the values of one block's data back out of the bit encoding of ECMA-363 clause 10. Every value of a
/// U3D block goes through one arithmetic coder: a plain one as bytes in a static context of 256 with their bits turned
/// round - which leaves a block of plain values byte for byte as it would be uncompressed - and a compressed one as a
/// symbol of the context the spec gives its field, an escape and the plain value after it when a dynamic context has
/// not seen it yet. Bits are taken from each byte lowest first. Each block starts the coder and its contexts afresh.
/// Reading past the block's end by more than the coder's look-ahead throws.</summary>
public sealed class U3dBitReader
{
    /// <summary>How far past the end of the data the coder may look before the data is taken as cut short: its window
    /// is 16 bits, and a block of compressed values ends with an uncompressed 0 to fill it.</summary>
    public const int SlackBits = 64;

    private const uint Top = 0xFFFF;
    private const uint HalfBit = 0x8000;
    private const uint QuarterBits = 0xC000;
    private const uint FirstQuarter = 0x4000;
    private const uint ByteRange = 256;

    private readonly byte[] bytes;
    private readonly int start;
    private readonly long bitLength;
    private readonly bool noCompression;
    private readonly Dictionary<string, U3dDynamicHistogram> histograms = new(StringComparer.Ordinal);
    private long bitPosition;
    private uint low;
    private uint high = Top;
    private uint code;

    /// <summary>A reader of <paramref name="count"/> bytes of <paramref name="source"/> from
    /// <paramref name="offset"/>. With <paramref name="noCompression"/> - the file header's no compression profile bit -
    /// a compressed field is read as the plain value of its size.</summary>
    public U3dBitReader(byte[] source, int offset, int count, bool noCompression)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (offset < 0 || count < 0 || (long)offset + count > source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "The data lies outside the bytes given");
        }

        bytes = source;
        start = offset;
        bitLength = (long)count * 8;
        this.noCompression = noCompression;
        for (var index = 0; index < 16; index++)
        {
            code = (code << 1) | NextBit();
        }
    }

    /// <summary>A reader of all of <paramref name="data"/>.</summary>
    public U3dBitReader(byte[] data, bool noCompression = false) : this(data, 0, data?.Length ?? 0, noCompression)
    {
    }

    /// <summary>Roughly how many bits of the data are not read yet: those past the coder's window.</summary>
    public long RemainingBits => Math.Max(0, bitLength - bitPosition + 16);

    /// <summary>Whether more has been read than the data holds: the coder has shifted in bits past its end. Never so
    /// for whole data - a compressed block ends with an uncompressed 0 that covers the coder's window.</summary>
    public bool IsPastEnd => bitPosition - 16 > bitLength;

    /// <summary>How many whole bytes have been read - exact for data of plain values alone, which the coder leaves byte
    /// for byte as they are: where a field of such a block starts.</summary>
    public long PlainBytesRead => (bitPosition - 16) / 8;

    /// <summary>Throws when <see cref="IsPastEnd"/>: what was read ran past the data, so it was made up of zeros.</summary>
    public void EnsureWithinData()
    {
        if (IsPastEnd)
        {
            throw new InvalidDataException("The U3D data ends before its values do");
        }
    }

    /// <summary>Throws unless <paramref name="count"/> items of at least <paramref name="bitsEach"/> bits could still be
    /// in the data. A count read from the file is checked so before anything is allocated or looped over for it: a
    /// corrupt count then fails at once instead of taking memory or time it names. A compressed item can take less
    /// than a bit, so a count of those is checked at one bit an item - a looser bound, but a bound by the data.</summary>
    public void CheckCount(ulong count, int bitsEach, string what)
    {
        if (count > 0 && (decimal)count * Math.Max(1, bitsEach) > RemainingBits + SlackBits)
        {
            throw new InvalidDataException($"The U3D data names {count} {what}, more than its remaining bytes can hold");
        }
    }

    /// <summary>An uncompressed U8.</summary>
    public byte ReadU8() => (byte)ReverseBits(DecodeStatic(ByteRange));

    /// <summary>An uncompressed U16: its low byte first.</summary>
    public ushort ReadU16()
    {
        uint lowByte = ReadU8();
        uint highByte = ReadU8();
        return (ushort)(lowByte | (highByte << 8));
    }

    /// <summary>An uncompressed U32: its low U16 first.</summary>
    public uint ReadU32()
    {
        uint lowHalf = ReadU16();
        uint highHalf = ReadU16();
        return lowHalf | (highHalf << 16);
    }

    /// <summary>An uncompressed U64: its low U32 first.</summary>
    public ulong ReadU64()
    {
        ulong lowHalf = ReadU32();
        ulong highHalf = ReadU32();
        return lowHalf | (highHalf << 32);
    }

    /// <summary>An I16, as the U16 of the same bits.</summary>
    public short ReadI16() => unchecked((short)ReadU16());

    /// <summary>An I32, as the U32 of the same bits.</summary>
    public int ReadI32() => unchecked((int)ReadU32());

    /// <summary>An F32, as the U32 of the same bits.</summary>
    public float ReadF32() => BitConverter.UInt32BitsToSingle(ReadU32());

    /// <summary>An F64, as the U64 of the same bits.</summary>
    public double ReadF64() => BitConverter.UInt64BitsToDouble(ReadU64());

    /// <summary>A String: a U16 count of bytes, then the bytes, UTF-8 as the current edition has every file.</summary>
    public string ReadString()
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
    public byte[] ReadBytes(int count)
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
    public byte ReadCompressedU8(U3dContext context)
    {
        if (noCompression)
        {
            return ReadU8();
        }

        if (!context.IsDynamic)
        {
            return context.Range > U3dContext.MaximumStaticRange ? ReadU8() : (byte)DecodeStaticValue(context.Range);
        }

        var histogram = Histogram(context.Name);
        var symbol = DecodeDynamic(histogram);
        if (symbol != 0)
        {
            return (byte)(symbol - 1);
        }

        var value = ReadU8();
        histogram.Add(value + 1u);
        return value;
    }

    /// <summary>A compressed U16 in <paramref name="context"/>.</summary>
    public ushort ReadCompressedU16(U3dContext context)
    {
        if (noCompression)
        {
            return ReadU16();
        }

        if (!context.IsDynamic)
        {
            return context.Range > U3dContext.MaximumStaticRange ? ReadU16() : (ushort)DecodeStaticValue(context.Range);
        }

        var histogram = Histogram(context.Name);
        var symbol = DecodeDynamic(histogram);
        if (symbol != 0)
        {
            return (ushort)(symbol - 1);
        }

        var value = ReadU16();
        histogram.Add(value + 1u);
        return value;
    }

    /// <summary>A compressed U32 in <paramref name="context"/>.</summary>
    public uint ReadCompressedU32(U3dContext context)
    {
        if (noCompression)
        {
            return ReadU32();
        }

        if (!context.IsDynamic)
        {
            return context.Range > U3dContext.MaximumStaticRange ? ReadU32() : DecodeStaticValue(context.Range);
        }

        var histogram = Histogram(context.Name);
        var symbol = DecodeDynamic(histogram);
        if (symbol != 0)
        {
            return symbol - 1;
        }

        var value = ReadU32();
        histogram.Add(unchecked(value + 1u));
        return value;
    }

    private U3dDynamicHistogram Histogram(string name)
    {
        if (!histograms.TryGetValue(name, out var histogram))
        {
            histogram = new U3dDynamicHistogram();
            histograms.Add(name, histogram);
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
        var span = high - low + 1;
        var cumulative = Cumulative(range, span);
        Narrow(span, cumulative, 1, range);
        return cumulative;
    }

    private uint DecodeDynamic(U3dDynamicHistogram histogram)
    {
        var total = histogram.Total;
        var span = high - low + 1;
        var cumulative = Cumulative(total, span);
        var symbol = histogram.SymbolAt(cumulative);
        var frequency = histogram.Frequency(symbol);
        if (frequency == 0)
        {
            throw new InvalidDataException("The U3D compressed data is corrupt");
        }

        Narrow(span, histogram.CumulativeFrequency(symbol), frequency, total);
        histogram.Add(symbol);
        return symbol;
    }

    private uint Cumulative(uint total, uint span)
    {
        if (code < low || code > high)
        {
            throw new InvalidDataException("The U3D compressed data is corrupt");
        }

        var cumulative = (total * (code - low + 1) - 1) / span;
        if (cumulative >= total)
        {
            throw new InvalidDataException("The U3D compressed data is corrupt");
        }

        return cumulative;
    }

    private void Narrow(uint span, uint cumulative, uint frequency, uint total)
    {
        high = low + span * (cumulative + frequency) / total - 1;
        low += span * cumulative / total;
        while (true)
        {
            if ((high & HalfBit) == (low & HalfBit))
            {
                low = (low << 1) & Top;
                high = ((high << 1) & Top) | 1;
                code = ((code << 1) & Top) | NextBit();
            }
            else if ((low & QuarterBits) == FirstQuarter && (high & QuarterBits) == HalfBit)
            {
                low = (low - FirstQuarter) << 1;
                high = ((high - FirstQuarter) << 1) | 1;
                code = ((code - FirstQuarter) << 1) | NextBit();
            }
            else
            {
                return;
            }
        }
    }

    private uint NextBit()
    {
        if (bitPosition >= bitLength + SlackBits)
        {
            throw new InvalidDataException("The U3D data ends before its values do");
        }

        uint bit = 0;
        if (bitPosition < bitLength)
        {
            bit = (uint)(bytes[start + (int)(bitPosition >> 3)] >> (int)(bitPosition & 7)) & 1;
        }

        bitPosition++;
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
