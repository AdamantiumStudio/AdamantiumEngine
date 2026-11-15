using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Adamantium.Engine.Compiler.Models.Prc;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Writes the compressed types of PRC by the specification's writing procedures, for test files; only the
/// table of double codes is shared with the reader.</summary>
internal sealed class PrcTestBitWriter
{
    private readonly List<bool> _bits = [];

    public int BitCount => _bits.Count;

    public void WriteBit(bool value) => _bits.Add(value);

    public void WriteBoolean(bool value) => _bits.Add(value);

    public void WriteBits(uint value, int count)
    {
        for (var bit = count - 1; bit >= 0; bit--)
        {
            _bits.Add(((value >> bit) & 1) != 0);
        }
    }

    public void WriteCharacter(byte value) => WriteBits(value, 8);

    public void WriteUnsignedInteger(uint value)
    {
        while (true)
        {
            if (value == 0)
            {
                WriteBit(false);
                return;
            }

            WriteBit(true);
            WriteBits(value & 0xFF, 8);
            value >>= 8;
        }
    }

    public void WriteInteger(int value)
    {
        if (value == 0)
        {
            WriteBit(false);
            return;
        }

        while (true)
        {
            var low = value & 0xFF;
            WriteBit(true);
            WriteBits((uint)low, 8);
            value >>= 8;
            if ((value == 0 && (low & 0x80) == 0) || (value == -1 && (low & 0x80) != 0))
            {
                WriteBit(false);
                return;
            }
        }
    }

    public void WriteString(string value)
    {
        if (value == null)
        {
            WriteBit(false);
            return;
        }

        var text = Encoding.UTF8.GetBytes(value);
        WriteBit(true);
        WriteUnsignedInteger((uint)text.Length);
        foreach (var character in text)
        {
            WriteCharacter(character);
        }
    }

    public void WriteDouble(double value)
    {
        var raw = BitConverter.DoubleToUInt64Bits(value);
        var exponent = (int)((raw >> 52) & 0x7FF);
        var magnitude = Math.Abs(value);
        var codes = PrcDoubleCodes.All;
        var frequent = codes.Where(code => !code.IsExponent && code.Value == magnitude &&
                                           (int)((BitConverter.DoubleToUInt64Bits(code.Value) >> 52) & 0x7FF) == exponent)
            .Cast<PrcDoubleCode?>().FirstOrDefault();
        var entry = frequent ?? codes.Single(code => code.IsExponent && code.Exponent == exponent);
        WriteBits(entry.Code, entry.Length);
        if ((raw & ~(1ul << 63)) == 0)
        {
            return;
        }

        WriteBit((raw >> 63) != 0);
        if (!entry.IsExponent)
        {
            return;
        }

        if ((raw & ((1ul << 52) - 1)) == 0)
        {
            WriteBit(false);
            return;
        }

        WriteBit(true);
        var bytes = BitConverter.GetBytes(value);
        WriteBits(bytes[6] & 0x0Fu, 4);
        var saveAtEnd = bytes[0] != bytes[1];
        var stop = 1;
        while (stop < 7 && bytes[stop] == bytes[stop + 1])
        {
            stop++;
        }

        for (var position = 5; position >= stop; position--)
        {
            WriteMantissaByte(bytes, position);
        }

        if (stop > 1)
        {
            WriteBit(false);
            if (saveAtEnd)
            {
                WriteBits(6, 3);
                WriteCharacter(bytes[0]);
            }
            else
            {
                WriteBits(0, 3);
            }
        }
        else
        {
            WriteMantissaByte(bytes, 0);
        }
    }

    public void WriteFloatAsBytes(float value)
    {
        foreach (var part in BitConverter.GetBytes(value))
        {
            WriteCharacter(part);
        }
    }

    /// <summary>The bits written, the last byte filled out with zero bits.</summary>
    public byte[] ToArray()
    {
        var bytes = new byte[(_bits.Count + 7) / 8];
        for (var bit = 0; bit < _bits.Count; bit++)
        {
            if (_bits[bit])
            {
                bytes[bit >> 3] |= (byte)(0x80 >> (bit & 7));
            }
        }

        return bytes;
    }

    private void WriteMantissaByte(byte[] bytes, int position)
    {
        if (position != 5)
        {
            for (var above = position + 1; above <= 5; above++)
            {
                if (bytes[above] == bytes[position])
                {
                    WriteBit(false);
                    WriteBits((uint)(above - position), 3);
                    return;
                }
            }
        }

        WriteBit(true);
        WriteCharacter(bytes[position]);
    }
}
