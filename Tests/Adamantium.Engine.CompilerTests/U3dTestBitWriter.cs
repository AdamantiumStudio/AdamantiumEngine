using System;
using System.Collections.Generic;
using System.Text;
using Adamantium.Engine.Compiler.Models.U3d;

namespace Adamantium.Engine.CompilerTests;

/// <summary>The bit encoding of ECMA-363 clause 10 as its text gives it, for writing U3D test files: a value is coded
/// as a symbol in the probability range of its context, a plain value as bytes with their bits turned round in a
/// static context of 256, a value a dynamic context has not seen as its escape and the plain value after it, and a
/// block with any compressed value in it ends with a plain 0. The reader's decoder is a different piece of code; the
/// two meet only in the bytes. A dynamic histogram is halved when it holds 0x1FFF occurrences, before the next is
/// counted, and never counts a symbol from 0xFFFF up - as the clause's own implementation (Annex A) does.</summary>
internal sealed class U3dTestBitWriter
{
    private const uint Top = 0x0000FFFF;
    private const uint HalfBit = 0x00008000;
    private const uint QuarterBit = 0x00004000;

    private readonly bool noCompression;
    private readonly List<byte> bytes = [];
    private readonly Dictionary<string, U3dTestHistogram> histograms = new(StringComparer.Ordinal);
    private long bitCount;
    private uint low;
    private uint high = Top;
    private uint underflow;
    private bool compressed;

    /// <summary>A writer of one block's data; with <paramref name="noCompression"/> every compressed value is written
    /// plain, as in a file of the no compression profile.</summary>
    public U3dTestBitWriter(bool noCompression = false)
    {
        this.noCompression = noCompression;
    }

    public void WriteU8(byte value)
    {
        var reversed = 0u;
        for (var bit = 0; bit < 8; bit++)
        {
            reversed = (reversed << 1) | (((uint)value >> bit) & 1);
        }

        Encode(reversed, 1, 256);
    }

    public void WriteU16(ushort value)
    {
        WriteU8((byte)(value & 0xFF));
        WriteU8((byte)(value >> 8));
    }

    public void WriteU32(uint value)
    {
        WriteU16((ushort)(value & 0xFFFF));
        WriteU16((ushort)(value >> 16));
    }

    public void WriteU64(ulong value)
    {
        WriteU32((uint)(value & 0xFFFFFFFF));
        WriteU32((uint)(value >> 32));
    }

    public void WriteI16(short value) => WriteU16(unchecked((ushort)value));

    public void WriteI32(int value) => WriteU32(unchecked((uint)value));

    public void WriteF32(float value) => WriteU32(BitConverter.SingleToUInt32Bits(value));

    public void WriteF64(double value) => WriteU64(BitConverter.DoubleToUInt64Bits(value));

    public void WriteString(string value)
    {
        var text = Encoding.UTF8.GetBytes(value);
        WriteU16((ushort)text.Length);
        foreach (var character in text)
        {
            WriteU8(character);
        }
    }

    public void WriteCompressedU8(U3dContext context, byte value)
    {
        if (!WriteCompressed(context, value, 1))
        {
            WriteU8(value);
        }
    }

    public void WriteCompressedU16(U3dContext context, ushort value)
    {
        if (!WriteCompressed(context, value, 2))
        {
            WriteU16(value);
        }
    }

    public void WriteCompressedU32(U3dContext context, uint value)
    {
        if (!WriteCompressed(context, value, 4))
        {
            WriteU32(value);
        }
    }

    /// <summary>The data written, the coder flushed when anything was compressed.</summary>
    public byte[] ToArray()
    {
        if (compressed)
        {
            compressed = false;
            WriteU32(0);
        }

        return bytes.ToArray();
    }

    private bool WriteCompressed(U3dContext context, uint value, int size)
    {
        if (noCompression)
        {
            return false;
        }

        compressed = true;
        if (!context.IsDynamic)
        {
            if (context.Range > U3dContext.MaximumStaticRange)
            {
                return false;
            }

            Encode(value, 1, context.Range);
            return true;
        }

        if (!histograms.TryGetValue(context.Name, out var histogram))
        {
            histogram = new U3dTestHistogram();
            histograms.Add(context.Name, histogram);
        }

        var symbol = value + 1;
        var total = histogram.Total;
        var frequency = histogram.Frequency(symbol);
        var escape = frequency == 0;
        if (escape)
        {
            symbol = 0;
            frequency = histogram.Frequency(0);
        }

        Encode(histogram.Cumulative(symbol), frequency, total);
        histogram.Add(symbol);
        if (!escape)
        {
            return true;
        }

        WritePlain(value, size);
        histogram.Add(value + 1);
        return true;
    }

    private void WritePlain(uint value, int size)
    {
        switch (size)
        {
            case 1:
                WriteU8((byte)value);
                break;
            case 2:
                WriteU16((ushort)value);
                break;
            default:
                WriteU32(value);
                break;
        }
    }

    private void Encode(uint cumulative, uint frequency, uint total)
    {
        var range = high - low + 1;
        high = low + range * (cumulative + frequency) / total - 1;
        low = low + range * cumulative / total;
        while ((high & HalfBit) == (low & HalfBit))
        {
            var bit = (high & HalfBit) >> 15;
            WriteBit(bit);
            while (underflow > 0)
            {
                WriteBit(bit ^ 1);
                underflow--;
            }

            high = ((high & ~HalfBit) << 1) | 1;
            low = (low & ~HalfBit) << 1;
        }

        while ((low & QuarterBit) != 0 && (high & QuarterBit) == 0)
        {
            high = (((high & ~HalfBit) << 1) | 1 | HalfBit) & Top;
            low = ((low & ~HalfBit) << 1) & ~HalfBit & Top;
            underflow++;
        }
    }

    private void WriteBit(uint bit)
    {
        var offset = (int)(bitCount & 7);
        if (offset == 0)
        {
            bytes.Add(0);
        }

        bytes[^1] |= (byte)(bit << offset);
        bitCount++;
    }
}

/// <summary>A dynamic context's frequencies as clause 10 defines them: the escape counted once from the start, a value
/// the symbol above it.</summary>
internal sealed class U3dTestHistogram
{
    private const uint RescaleTotal = 0x1FFF;
    private const uint FirstUncountedSymbol = 0xFFFF;

    private readonly List<uint> counts = [1];

    public uint Total { get; private set; } = 1;

    public uint Frequency(uint symbol) => symbol < counts.Count ? counts[(int)symbol] : 0;

    public uint Cumulative(uint symbol)
    {
        var sum = 0u;
        for (var index = 0; index < symbol && index < counts.Count; index++)
        {
            sum += counts[index];
        }

        return sum;
    }

    public void Add(uint symbol)
    {
        if (symbol >= FirstUncountedSymbol)
        {
            return;
        }

        while (counts.Count <= symbol)
        {
            counts.Add(0);
        }

        if (Total >= RescaleTotal)
        {
            Total = 0;
            for (var index = 0; index < counts.Count; index++)
            {
                counts[index] /= 2;
            }

            counts[0]++;
            foreach (var count in counts)
            {
                Total += count;
            }
        }

        counts[(int)symbol]++;
        Total++;
    }
}
