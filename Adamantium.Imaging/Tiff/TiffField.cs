using System;
using System.IO;
using System.Text;

namespace Adamantium.Imaging.Tiff;

/// <summary>One field of a TIFF directory: a tag, the type and count of its values, and the values' bytes in the file's
/// byte order - read from the file when first asked for, so fields nobody reads cost nothing.</summary>
internal sealed class TiffField
{
    private readonly bool _littleEndian;
    private readonly ITiffSource _source;
    private readonly long _offset;
    private byte[] _data;

    /// <summary>A field whose values were in the directory entry itself.</summary>
    public TiffField(ushort tag, TiffFieldType type, ulong count, byte[] data, bool littleEndian)
    {
        Tag = tag;
        Type = type;
        Count = count;
        _data = data;
        _littleEndian = littleEndian;
    }

    /// <summary>A field whose values are at <paramref name="offset"/> of <paramref name="source"/>.</summary>
    public TiffField(ushort tag, TiffFieldType type, ulong count, ITiffSource source, long offset, bool littleEndian)
    {
        Tag = tag;
        Type = type;
        Count = count;
        _source = source;
        _offset = offset;
        _littleEndian = littleEndian;
    }

    /// <summary>The tag's number; <see cref="TiffTag"/> names the ones the reader understands.</summary>
    public ushort Tag { get; }

    /// <summary>The type of the values.</summary>
    public TiffFieldType Type { get; }

    /// <summary>How many values there are.</summary>
    public ulong Count { get; }

    /// <summary>The values' bytes as the file holds them.</summary>
    public ReadOnlySpan<byte> Bytes => Data;

    private byte[] Data
    {
        get
        {
            if (_data == null)
            {
                var data = new byte[(int)Count * SizeOf(Type)];
                _source.Read(_offset, data);
                _data = data;
            }

            return _data;
        }
    }

    /// <summary>The size in bytes of one value of <paramref name="type"/>; 0 for a type the reader does not know.</summary>
    public static int SizeOf(TiffFieldType type) => type switch
    {
        TiffFieldType.Byte or TiffFieldType.Ascii or TiffFieldType.SByte or TiffFieldType.Undefined => 1,
        TiffFieldType.Short or TiffFieldType.SShort => 2,
        TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Float or TiffFieldType.Ifd => 4,
        TiffFieldType.Rational or TiffFieldType.SRational or TiffFieldType.Double or TiffFieldType.Long8
            or TiffFieldType.SLong8 or TiffFieldType.Ifd8 => 8,
        _ => 0
    };

    /// <summary>Value <paramref name="index"/> as an unsigned integer; a negative signed value or a fraction throws
    /// <see cref="InvalidDataException"/>.</summary>
    public ulong GetUnsigned(int index)
    {
        if (Type is TiffFieldType.Byte or TiffFieldType.Short or TiffFieldType.Long or TiffFieldType.Ifd
            or TiffFieldType.Long8 or TiffFieldType.Ifd8 or TiffFieldType.Undefined)
        {
            return GetRawUnsigned(index);
        }

        var value = GetDouble(index);
        if (value < 0 || value != Math.Floor(value))
        {
            throw new InvalidDataException($"TIFF tag {Tag} holds {value} where a whole number not below 0 belongs.");
        }

        return (ulong)value;
    }

    /// <summary>All values as unsigned integers.</summary>
    public ulong[] GetUnsignedValues()
    {
        var values = new ulong[Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = GetUnsigned(i);
        }

        return values;
    }

    /// <summary>Value <paramref name="index"/> as a number, whatever the type; a rational is divided out.</summary>
    public double GetDouble(int index)
    {
        CheckIndex(index);
        var at = Data.AsSpan(index * SizeOf(Type));
        return Type switch
        {
            TiffFieldType.SByte => (sbyte)at[0],
            TiffFieldType.SShort => (short)Read16(at),
            TiffFieldType.SLong => (int)Read32(at),
            TiffFieldType.SLong8 => (long)Read64(at),
            TiffFieldType.Rational => Ratio(Read32(at), Read32(at[4..])),
            TiffFieldType.SRational => Ratio((int)Read32(at), (int)Read32(at[4..])),
            TiffFieldType.Float => BitConverter.Int32BitsToSingle((int)Read32(at)),
            TiffFieldType.Double => BitConverter.Int64BitsToDouble((long)Read64(at)),
            _ => GetRawUnsigned(index)
        };
    }

    /// <summary>All values as numbers.</summary>
    public double[] GetDoubleValues()
    {
        var values = new double[Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = GetDouble(i);
        }

        return values;
    }

    /// <summary>The text of an ASCII field, up to its first zero byte.</summary>
    public string GetString()
    {
        var data = Data;
        var end = Array.IndexOf(data, (byte)0);
        return Encoding.ASCII.GetString(data, 0, end < 0 ? data.Length : end);
    }

    private void CheckIndex(int index)
    {
        if (index < 0 || (ulong)index >= Count)
        {
            throw new InvalidDataException($"TIFF tag {Tag} has {Count} values, not {index + 1}.");
        }
    }

    private ulong GetRawUnsigned(int index)
    {
        CheckIndex(index);
        var at = Data.AsSpan(index * SizeOf(Type));
        return SizeOf(Type) switch
        {
            1 => at[0],
            2 => Read16(at),
            4 => Read32(at),
            _ => Read64(at)
        };
    }

    private static double Ratio(double numerator, double denominator) =>
        denominator == 0 ? 0 : numerator / denominator;

    private ushort Read16(ReadOnlySpan<byte> at) => TiffBytes.Read16(at, _littleEndian);

    private uint Read32(ReadOnlySpan<byte> at) => TiffBytes.Read32(at, _littleEndian);

    private ulong Read64(ReadOnlySpan<byte> at) => TiffBytes.Read64(at, _littleEndian);
}
