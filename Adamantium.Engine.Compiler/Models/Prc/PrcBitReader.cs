using System;
using System.IO;
using System.Text;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads the compressed types of a PRC section (PRC 8137, "Basic Compressed types") from its bits, the highest
/// of each byte first; reading past the end throws, naming the section and the bit.</summary>
internal sealed class PrcBitReader
{
    /// <summary>The fewest bits a Double takes: the code of zero.</summary>
    internal const int ShortestDouble = 2;

    private const int ExponentShift = 52;
    private const int MaximumUnsignedBytes = 4;
    private const int DoubleBytes = 8;
    private const int FirstMantissaByte = 5;
    private const int RepeatToTheEnd = 0;
    private const int RepeatThenLastByte = 6;

    private readonly byte[] _bytes;
    private readonly long _bitLength;
    private readonly string _section;
    private long _bitPosition;

    /// <summary>A reader of all of <paramref name="data"/>, the inflated bytes of the section
    /// <paramref name="section"/> names, which errors are told by.</summary>
    internal PrcBitReader(byte[] data, string section)
    {
        ArgumentNullException.ThrowIfNull(data);
        _bytes = data;
        _bitLength = (long)data.Length * 8;
        _section = section ?? string.Empty;
    }

    /// <summary>The section the data is, as errors name it.</summary>
    internal string Section => _section;

    /// <summary>The bit the next value starts at, from the start of the section.</summary>
    internal long BitPosition => _bitPosition;

    /// <summary>How many bits are not read yet.</summary>
    internal long RemainingBits => _bitLength - _bitPosition;

    /// <summary>Whether all that is left is the zero padding a section ends with.</summary>
    internal bool IsAtPadding
    {
        get
        {
            if (RemainingBits >= 16)
            {
                return false;
            }

            for (var bit = _bitPosition; bit < _bitLength; bit++)
            {
                if (((_bytes[bit >> 3] >> (7 - (int)(bit & 7))) & 1) != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Creates the error for corrupt data at the current bit.</summary>
    internal InvalidDataException CreateCorruptException(string message) =>
        new($"The PRC {_section} is corrupt at bit {_bitPosition} (byte {_bitPosition / 8}): {message}");

    /// <summary>Throws unless <paramref name="count"/> items of at least <paramref name="bitsEach"/> bits could still
    /// be in the section: a count read from the file is checked so before anything is allocated or looped over for it.</summary>
    internal void CheckCount(ulong count, int bitsEach, string what)
    {
        if (count > 0 && (decimal)count * Math.Max(1, bitsEach) > RemainingBits)
        {
            throw CreateCorruptException($"it names {count} {what}, more than its remaining {RemainingBits} bits can hold");
        }
    }

    /// <summary>A count of items of at least <paramref name="bitsEach"/> bits, checked against the data left.</summary>
    internal int ReadCount(int bitsEach, string what)
    {
        var count = ReadUnsignedInteger();
        CheckCount(count, bitsEach, what);
        return (int)count;
    }

    /// <summary>One bit.</summary>
    internal bool ReadBoolean() => ReadBit() != 0;

    /// <summary><paramref name="count"/> bits, up to 32, the first read the highest.</summary>
    internal uint ReadBits(int count)
    {
        if (count < 0 || count > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count > RemainingBits)
        {
            throw CreateEndOfDataException();
        }

        var value = 0u;
        for (var bit = 0; bit < count; bit++)
        {
            value = (value << 1) | ReadBit();
        }

        return value;
    }

    /// <summary>A Character: eight bits.</summary>
    internal byte ReadCharacter() => (byte)ReadBits(8);

    /// <summary>An UnsignedInteger: while a 1 bit comes, eight more bits, the lowest byte first.</summary>
    internal uint ReadUnsignedInteger()
    {
        var value = 0u;
        for (var group = 0; ReadBit() != 0; group++)
        {
            if (group == MaximumUnsignedBytes)
            {
                throw CreateCorruptException("an UnsignedInteger runs past 32 bits");
            }

            value |= ReadBits(8) << (group * 8);
        }

        return value;
    }

    /// <summary>An Integer: 0 alone for zero, else bytes behind 1 bits, lowest first, ended by a 0 bit; the highest bit
    /// of the last byte is the sign.</summary>
    internal int ReadInteger()
    {
        if (ReadBit() == 0)
        {
            return 0;
        }

        var value = 0u;
        var shift = 0;
        uint last;
        do
        {
            if (shift == MaximumUnsignedBytes * 8)
            {
                throw CreateCorruptException("an Integer runs past 32 bits");
            }

            last = ReadBits(8);
            value |= last << shift;
            shift += 8;
        }
        while (ReadBit() != 0);

        if (shift < 32 && (last & 0x80) != 0)
        {
            value |= uint.MaxValue << shift;
        }

        return unchecked((int)value);
    }

    /// <summary>A Double: a frequent value's code and its sign, or an exponent's code, the sign and the mantissa bytes,
    /// each read or repeated from a higher one.</summary>
    internal double ReadDouble()
    {
        var entry = ReadDoubleCode();
        if (!entry.IsExponent)
        {
            if (entry.Value == 0)
            {
                return 0;
            }

            return ReadBit() != 0 ? -entry.Value : entry.Value;
        }

        var negative = ReadBit() != 0;
        var bits = ((ulong)entry.Exponent << ExponentShift) | (negative ? 1ul << 63 : 0);
        if (ReadBit() == 0)
        {
            return BitConverter.UInt64BitsToDouble(bits);
        }

        var value = new byte[DoubleBytes];
        value[7] = (byte)(bits >> 56);
        value[6] = (byte)((bits >> 48) | ReadBits(4));
        for (var position = FirstMantissaByte; position >= 0; position--)
        {
            if (ReadBit() != 0)
            {
                value[position] = ReadCharacter();
                continue;
            }

            var offset = (int)ReadBits(3);
            if (offset == RepeatToTheEnd)
            {
                Repeat(value, position, 0);
                break;
            }

            if (offset == RepeatThenLastByte)
            {
                Repeat(value, position, 1);
                value[0] = ReadCharacter();
                break;
            }

            if (position + offset > FirstMantissaByte)
            {
                throw CreateCorruptException($"a Double refers to a byte {offset} above byte {position} of its mantissa");
            }

            value[position] = value[position + offset];
        }

        return BitConverter.ToDouble(value, 0);
    }

    /// <summary>A String: a 1 bit, a count and that many UTF-8 Characters; null after a 0 bit.</summary>
    internal string ReadString()
    {
        if (ReadBit() == 0)
        {
            return null;
        }

        var length = ReadCount(8, "characters of a string");
        var text = new byte[length];
        for (var index = 0; index < length; index++)
        {
            text[index] = ReadCharacter();
        }

        return Encoding.UTF8.GetString(text);
    }

    /// <summary>An unsigned integer of <paramref name="bitNumber"/> bits, the highest first.</summary>
    internal uint ReadUnsignedIntegerWithVariableBitNumber(int bitNumber) => ReadBits(bitNumber);

    /// <summary>An integer of <paramref name="bitNumber"/> bits: a 1 bit for a negative one, then its magnitude in the
    /// remaining bits.</summary>
    internal int ReadIntegerWithVariableBitNumber(int bitNumber)
    {
        if (bitNumber < 1)
        {
            throw CreateCorruptException($"an integer is stored in {bitNumber} bits");
        }

        var negative = ReadBit() != 0;
        var magnitude = (int)ReadBits(bitNumber - 1);
        return negative ? -magnitude : magnitude;
    }

    /// <summary>A double of <paramref name="bitNumber"/> bits: a 1 bit for a negative one, then how many times
    /// <paramref name="tolerance"/> its magnitude is, in the remaining bits.</summary>
    internal double ReadDoubleWithVariableBitNumber(double tolerance, int bitNumber)
    {
        if (bitNumber < 1 || bitNumber > 31)
        {
            throw CreateCorruptException($"a double is stored in {bitNumber} bits");
        }

        var negative = ReadBit() != 0;
        var magnitude = ReadBits(bitNumber - 1) * tolerance;
        return negative ? -magnitude : magnitude;
    }

    /// <summary>Five bits giving a number of bits, then an unsigned integer of that many bits.</summary>
    internal uint ReadNumberOfBitsThenUnsignedInteger() => ReadBits((int)ReadBits(5));

    /// <summary>A float as four Characters, its lowest byte first.</summary>
    internal float ReadFloatAsBytes()
    {
        var value = 0u;
        for (var index = 0; index < 4; index++)
        {
            value |= (uint)ReadCharacter() << (index * 8);
        }

        return BitConverter.UInt32BitsToSingle(value);
    }

    /// <summary><paramref name="count"/> Booleans, one bit each.</summary>
    internal bool[] ReadBooleans(int count, string what)
    {
        if (count < 0)
        {
            throw CreateCorruptException($"it names {count} {what}");
        }

        CheckCount((ulong)count, 1, what);
        var values = new bool[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = ReadBit() != 0;
        }

        return values;
    }

    /// <summary>A CharacterArray (WriteCharacterArray): a flag saying whether it is Huffman coded, then the
    /// characters - each of <paramref name="bitNumber"/> bits when coded, of eight when plain. A
    /// <paramref name="signed"/> array holds them in two's complement in that width.</summary>
    internal int[] ReadCharacterArray(int bitNumber, bool signed, string what) =>
        ReadCharacterArray(bitNumber, signed, ReadBit() != 0, what);

    /// <summary>A CharacterArray whose flag is not stored: <paramref name="compressed"/> says whether it is Huffman
    /// coded.</summary>
    internal int[] ReadCharacterArray(int bitNumber, bool signed, bool compressed, string what)
    {
        if (compressed)
        {
            return PrcHuffmanDecoder.Decode(ReadHuffmanPayload(what), bitNumber, signed, this, what);
        }

        var count = ReadCount(8, what);
        var values = new int[count];
        for (var index = 0; index < count; index++)
        {
            var character = ReadCharacter();
            values[index] = signed ? (sbyte)character : character;
        }

        return values;
    }

    /// <summary>A ShortArray (WriteShortArray): a flag saying whether it is Huffman coded, then the values - each of
    /// <paramref name="bitNumber"/> bits when coded, else two Characters, the lowest byte first.</summary>
    internal int[] ReadShortArray(int bitNumber, string what)
    {
        if (ReadBit() != 0)
        {
            return PrcHuffmanDecoder.Decode(ReadHuffmanPayload(what), bitNumber, false, this, what);
        }

        var count = ReadCount(16, what);
        var values = new int[count];
        for (var index = 0; index < count; index++)
        {
            var low = ReadCharacter();
            values[index] = low | (ReadCharacter() << 8);
        }

        return values;
    }

    /// <summary>A CompressedIntegerArray (WriteCompressedIntegerArray): a CharacterArray of six-bit widths, then
    /// each integer as an IntegerWithVariableBitNumber of its width.</summary>
    internal int[] ReadCompressedIntegerArray(string what)
    {
        var widths = ReadCharacterArray(6, false, what);
        var values = new int[widths.Length];
        for (var index = 0; index < widths.Length; index++)
        {
            values[index] = ReadIntegerWithVariableBitNumber(widths[index]);
        }

        return values;
    }

    /// <summary>A CompressedIndiceArray (WriteCompressedIndiceArray) whose compression flag is stored.</summary>
    internal int[] ReadCompressedIndiceArray(string what) => ReadCompressedIndiceArray(ReadBit() != 0, what);

    /// <summary>A CompressedIndiceArray: a signed CharacterArray of six-bit steps - the first index's width, then by
    /// how much each width differs from the one before - then the first index, and each difference to the next, as
    /// an IntegerWithVariableBitNumber of its width.</summary>
    internal int[] ReadCompressedIndiceArray(bool compressed, string what)
    {
        var steps = ReadCharacterArray(6, true, compressed, what);
        var values = new int[steps.Length];
        var width = 0;
        var previous = 0;
        for (var index = 0; index < steps.Length; index++)
        {
            width = index == 0 ? steps[0] : width + steps[index];
            var read = ReadIntegerWithVariableBitNumber(width);
            previous = index == 0 ? read : previous + read;
            values[index] = previous;
        }

        return values;
    }

    /// <summary>Skips <paramref name="count"/> bits.</summary>
    internal void Skip(long count)
    {
        if (count < 0 || count > RemainingBits)
        {
            throw CreateEndOfDataException();
        }

        _bitPosition += count;
    }

    private PrcHuffmanPayload ReadHuffmanPayload(string what)
    {
        var words = ReadCount(32, what);
        if (words == 0)
        {
            return new PrcHuffmanPayload([], 0);
        }

        var payload = new byte[words * 4];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = ReadCharacter();
        }

        var usedInLastWord = ReadUnsignedInteger();
        if (usedInLastWord > 32)
        {
            throw CreateCorruptException($"the Huffman coded {what} uses {usedInLastWord} bits of its last 32-bit word");
        }

        return new PrcHuffmanPayload(payload, (words - 1L) * 32 + usedInLastWord);
    }

    private PrcDoubleCode ReadDoubleCode()
    {
        if (!PrcDoubleCodes.TryDecode(Peek(), out var entry))
        {
            // Within the bits of the longest code, the data ended.
            throw RemainingBits < PrcDoubleCodes.LongestCode
                ? CreateEndOfDataException()
                : CreateCorruptException("a Double starts with no code of the table of frequent doubles and exponents");
        }

        if (entry.Length > RemainingBits)
        {
            throw CreateEndOfDataException();
        }

        _bitPosition += entry.Length;
        return entry;
    }

    private static void Repeat(byte[] value, int from, int downTo)
    {
        for (var position = from; position >= downTo; position--)
        {
            value[position] = value[position + 1];
        }
    }

    // The next 64 bits, not read, the first the highest; zeros past the end.
    private ulong Peek()
    {
        var at = _bitPosition >> 3;
        ulong bits = 0;
        for (var index = 0; index < 8; index++)
        {
            bits = bits << 8 | ByteAt(at + index);
        }

        var shift = (int)(_bitPosition & 7);
        return shift == 0 ? bits : bits << shift | ByteAt(at + 8) >> (8 - shift);
    }

    private ulong ByteAt(long at) => at < _bytes.Length ? _bytes[at] : 0u;

    private uint ReadBit()
    {
        if (_bitPosition >= _bitLength)
        {
            throw CreateEndOfDataException();
        }

        var bit = (uint)(_bytes[_bitPosition >> 3] >> (7 - (int)(_bitPosition & 7))) & 1;
        _bitPosition++;
        return bit;
    }

    private InvalidDataException CreateEndOfDataException() =>
        new($"The PRC {_section} ends at bit {_bitPosition} (byte {_bitPosition / 8}) before its data does");
}
