using System;
using System.IO;
using System.Text;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads the compressed types of a PRC section (PRC Format Specification 8137, "Basic Compressed types"): the
/// section's inflated bytes are a stream of bits, the first bit the highest of its byte, and a value may start at any
/// bit. A Boolean is one bit, a Character eight, an UnsignedInteger groups of eight bits each behind a 1 and ended by a
/// 0, an Integer the same with the sign of its last byte, a Double a prefix code from the table of frequent doubles and
/// exponents followed by what the code leaves out, a String a flag, a length and its UTF-8 bytes. The arrays of
/// compressed tessellation - CharacterArray, ShortArray, CompressedIntegerArray, CompressedIndiceArray - are plain or
/// Huffman coded. Reading past the end of the section throws, naming the section and the bit.</summary>
public sealed class PrcBitReader
{
    /// <summary>The fewest bits a Double takes: the code of zero.</summary>
    public const int ShortestDouble = 2;

    private const int ExponentShift = 52;
    private const int MaximumUnsignedBytes = 4;
    private const int DoubleBytes = 8;
    private const int FirstMantissaByte = 5;
    private const int RepeatToTheEnd = 0;
    private const int RepeatThenLastByte = 6;

    private readonly byte[] bytes;
    private readonly long bitLength;
    private readonly string section;
    private long bitPosition;

    /// <summary>A reader of all of <paramref name="data"/>, the inflated bytes of the section
    /// <paramref name="section"/> names, which errors are told by.</summary>
    public PrcBitReader(byte[] data, string section)
    {
        ArgumentNullException.ThrowIfNull(data);
        bytes = data;
        bitLength = (long)data.Length * 8;
        this.section = section ?? string.Empty;
    }

    /// <summary>The section the data is, as errors name it.</summary>
    public string Section => section;

    /// <summary>The bit the next value starts at, from the start of the section.</summary>
    public long BitPosition => bitPosition;

    /// <summary>How many bits are not read yet.</summary>
    public long RemainingBits => bitLength - bitPosition;

    /// <summary>The error for corrupt data at the current bit.</summary>
    public InvalidDataException Corrupt(string message) =>
        new($"The PRC {section} is corrupt at bit {bitPosition} (byte {bitPosition / 8}): {message}");

    /// <summary>Throws unless <paramref name="count"/> items of at least <paramref name="bitsEach"/> bits could still
    /// be in the section: a count read from the file is checked so before anything is allocated or looped over for it.</summary>
    public void CheckCount(ulong count, int bitsEach, string what)
    {
        if (count > 0 && (decimal)count * Math.Max(1, bitsEach) > RemainingBits)
        {
            throw Corrupt($"it names {count} {what}, more than its remaining {RemainingBits} bits can hold");
        }
    }

    /// <summary>A count of items of at least <paramref name="bitsEach"/> bits, checked against the data left.</summary>
    public int ReadCount(int bitsEach, string what)
    {
        var count = ReadUnsignedInteger();
        CheckCount(count, bitsEach, what);
        return (int)count;
    }

    /// <summary>One bit.</summary>
    public bool ReadBoolean() => ReadBit() != 0;

    /// <summary><paramref name="count"/> bits, up to 32, the first read the highest.</summary>
    public uint ReadBits(int count)
    {
        if (count < 0 || count > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count > RemainingBits)
        {
            throw EndOfData();
        }

        var value = 0u;
        for (var bit = 0; bit < count; bit++)
        {
            value = (value << 1) | ReadBit();
        }

        return value;
    }

    /// <summary>A Character: eight bits.</summary>
    public byte ReadCharacter() => (byte)ReadBits(8);

    /// <summary>An UnsignedInteger: while a 1 bit comes, eight more bits, the lowest byte first.</summary>
    public uint ReadUnsignedInteger()
    {
        var value = 0u;
        for (var group = 0; ReadBit() != 0; group++)
        {
            if (group == MaximumUnsignedBytes)
            {
                throw Corrupt("an UnsignedInteger runs past 32 bits");
            }

            value |= ReadBits(8) << (group * 8);
        }

        return value;
    }

    /// <summary>An Integer: 0 alone for zero, else bytes behind 1 bits, lowest first, ended by a 0 bit; the highest bit
    /// of the last byte is the sign.</summary>
    public int ReadInteger()
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
                throw Corrupt("an Integer runs past 32 bits");
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

    /// <summary>A Double: the code of a frequent value and its sign, or the code of an exponent, the sign, and the
    /// mantissa - none when a 0 bit says it is zero, else its top four bits and its six lower bytes from the highest,
    /// each a byte or a reference to one of the higher bytes already read, until a mark repeats the last byte to the
    /// end.</summary>
    public double ReadDouble()
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
                throw Corrupt($"a Double refers to a byte {offset} above byte {position} of its mantissa");
            }

            value[position] = value[position + offset];
        }

        return BitConverter.ToDouble(value, 0);
    }

    /// <summary>A String: a 1 bit, a count and that many UTF-8 Characters; null after a 0 bit.</summary>
    public string ReadString()
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
    public uint ReadUnsignedIntegerWithVariableBitNumber(int bitNumber) => ReadBits(bitNumber);

    /// <summary>An integer of <paramref name="bitNumber"/> bits: a 1 bit for a negative one, then its magnitude in the
    /// remaining bits.</summary>
    public int ReadIntegerWithVariableBitNumber(int bitNumber)
    {
        if (bitNumber < 1)
        {
            throw Corrupt($"an integer is stored in {bitNumber} bits");
        }

        var negative = ReadBit() != 0;
        var magnitude = (int)ReadBits(bitNumber - 1);
        return negative ? -magnitude : magnitude;
    }

    /// <summary>A double of <paramref name="bitNumber"/> bits: a 1 bit for a negative one, then how many times
    /// <paramref name="tolerance"/> its magnitude is, in the remaining bits.</summary>
    public double ReadDoubleWithVariableBitNumber(double tolerance, int bitNumber)
    {
        if (bitNumber < 1 || bitNumber > 31)
        {
            throw Corrupt($"a double is stored in {bitNumber} bits");
        }

        var negative = ReadBit() != 0;
        var magnitude = ReadBits(bitNumber - 1) * tolerance;
        return negative ? -magnitude : magnitude;
    }

    /// <summary>Five bits giving a number of bits, then an unsigned integer of that many bits.</summary>
    public uint ReadNumberOfBitsThenUnsignedInteger() => ReadBits((int)ReadBits(5));

    /// <summary>A float as four Characters, its lowest byte first.</summary>
    public float ReadFloatAsBytes()
    {
        var value = 0u;
        for (var index = 0; index < 4; index++)
        {
            value |= (uint)ReadCharacter() << (index * 8);
        }

        return BitConverter.UInt32BitsToSingle(value);
    }

    /// <summary><paramref name="count"/> Booleans, one bit each.</summary>
    public bool[] ReadBooleans(int count, string what)
    {
        if (count < 0)
        {
            throw Corrupt($"it names {count} {what}");
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
    public int[] ReadCharacterArray(int bitNumber, bool signed, string what) =>
        ReadCharacterArray(bitNumber, signed, ReadBit() != 0, what);

    /// <summary>A CharacterArray whose flag is not stored: <paramref name="compressed"/> says whether it is Huffman
    /// coded.</summary>
    public int[] ReadCharacterArray(int bitNumber, bool signed, bool compressed, string what)
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
    public int[] ReadShortArray(int bitNumber, string what)
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
    public int[] ReadCompressedIntegerArray(string what)
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
    public int[] ReadCompressedIndiceArray(string what) => ReadCompressedIndiceArray(ReadBit() != 0, what);

    /// <summary>A CompressedIndiceArray: a signed CharacterArray of six-bit steps - the first index's width, then by
    /// how much each width differs from the one before - then the first index, and each difference to the next, as
    /// an IntegerWithVariableBitNumber of its width.</summary>
    public int[] ReadCompressedIndiceArray(bool compressed, string what)
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

    /// <summary>Whether all that is left is the padding a section ends with: zero bits filling its last byte, and at
    /// most one zero byte more.</summary>
    public bool IsAtPadding
    {
        get
        {
            if (RemainingBits >= 16)
            {
                return false;
            }

            for (var bit = bitPosition; bit < bitLength; bit++)
            {
                if (((bytes[bit >> 3] >> (7 - (int)(bit & 7))) & 1) != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Skips <paramref name="count"/> bits.</summary>
    public void Skip(long count)
    {
        if (count < 0 || count > RemainingBits)
        {
            throw EndOfData();
        }

        bitPosition += count;
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
            throw Corrupt($"the Huffman coded {what} uses {usedInLastWord} bits of its last 32-bit word");
        }

        return new PrcHuffmanPayload(payload, (words - 1L) * 32 + usedInLastWord);
    }

    private PrcDoubleCode ReadDoubleCode()
    {
        var code = 0u;
        for (var length = 1; length <= PrcDoubleCodes.LongestCode; length++)
        {
            code = (code << 1) | ReadBit();
            if (PrcDoubleCodes.TryFind(length, code, out var entry))
            {
                return entry;
            }
        }

        throw Corrupt("a Double starts with no code of the table of frequent doubles and exponents");
    }

    private static void Repeat(byte[] value, int from, int downTo)
    {
        for (var position = from; position >= downTo; position--)
        {
            value[position] = value[position + 1];
        }
    }

    private uint ReadBit()
    {
        if (bitPosition >= bitLength)
        {
            throw EndOfData();
        }

        var bit = (uint)(bytes[bitPosition >> 3] >> (7 - (int)(bitPosition & 7))) & 1;
        bitPosition++;
        return bit;
    }

    private InvalidDataException EndOfData() =>
        new($"The PRC {section} ends at bit {bitPosition} (byte {bitPosition / 8}) before its data does");
}
