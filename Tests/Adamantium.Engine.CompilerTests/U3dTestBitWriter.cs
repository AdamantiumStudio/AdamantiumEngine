using System;
using System.Collections.Generic;
using System.Text;
using Adamantium.Engine.Compiler.Models.U3d;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Writes the arithmetic coding of ECMA-363 clause 10, for U3D test files; it shares no code with the reader's
/// decoder.</summary>
internal sealed class U3dTestBitWriter
{
    private const uint Top = 0x0000FFFF;
    private const uint HalfBit = 0x00008000;
    private const uint QuarterBit = 0x00004000;

    private readonly bool _noCompression;
    private readonly List<byte> _bytes = [];
    private readonly Dictionary<string, U3dTestHistogram> _histograms = new(StringComparer.Ordinal);
    private long _bitCount;
    private uint _low;
    private uint _high = Top;
    private uint _underflow;
    private bool _compressed;

    /// <summary>A writer of one block's data; with <paramref name="noCompression"/> every compressed value is written
    /// plain, as in a file of the no compression profile.</summary>
    public U3dTestBitWriter(bool noCompression = false)
    {
        _noCompression = noCompression;
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
        if (_compressed)
        {
            _compressed = false;
            WriteU32(0);
        }

        return _bytes.ToArray();
    }

    private bool WriteCompressed(U3dContext context, uint value, int size)
    {
        if (_noCompression)
        {
            return false;
        }

        _compressed = true;
        if (!context.IsDynamic)
        {
            if (context.Range > U3dContext.MaximumStaticRange)
            {
                return false;
            }

            Encode(value, 1, context.Range);
            return true;
        }

        if (!_histograms.TryGetValue(context.Name, out var histogram))
        {
            histogram = new U3dTestHistogram();
            _histograms.Add(context.Name, histogram);
        }

        var symbol = value + 1;
        var total = histogram.Total;
        var frequency = histogram.GetFrequency(symbol);
        var escape = frequency == 0;
        if (escape)
        {
            symbol = 0;
            frequency = histogram.GetFrequency(0);
        }

        Encode(histogram.GetCumulative(symbol), frequency, total);
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
        var range = _high - _low + 1;
        _high = _low + range * (cumulative + frequency) / total - 1;
        _low = _low + range * cumulative / total;
        while ((_high & HalfBit) == (_low & HalfBit))
        {
            var bit = (_high & HalfBit) >> 15;
            WriteBit(bit);
            while (_underflow > 0)
            {
                WriteBit(bit ^ 1);
                _underflow--;
            }

            _high = ((_high & ~HalfBit) << 1) | 1;
            _low = (_low & ~HalfBit) << 1;
        }

        while ((_low & QuarterBit) != 0 && (_high & QuarterBit) == 0)
        {
            _high = (((_high & ~HalfBit) << 1) | 1 | HalfBit) & Top;
            _low = ((_low & ~HalfBit) << 1) & ~HalfBit & Top;
            _underflow++;
        }
    }

    private void WriteBit(uint bit)
    {
        var offset = (int)(_bitCount & 7);
        if (offset == 0)
        {
            _bytes.Add(0);
        }

        _bytes[^1] |= (byte)(bit << offset);
        _bitCount++;
    }
}
