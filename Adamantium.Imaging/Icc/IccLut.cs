using System;
using System.IO;

namespace Adamantium.Imaging.Icc;

/// <summary>A device-to-PCS table of a profile: lut8Type and lut16Type (ICC.1, 10.8 and 10.9) - input curves, a lookup
/// table, output curves - or lutAToBType (10.10) - A curves, a lookup table, M curves, a matrix, B curves, each part
/// there or not. Values go in 0..1 and come out 0..1 in the table's <see cref="Encoding"/>.</summary>
internal sealed class IccLut
{
    private const uint Lut8 = 0x6D667431;
    private const uint Lut16 = 0x6D667432;
    private const uint AToB = 0x6D414220;
    private const uint BToA = 0x6D424120;

    private bool _fromConnection;
    private double[] _inputMatrix;
    private IccCurve[] _a;
    private IccClut _clut;
    private IccCurve[] _m;
    private double[] _matrix;
    private IccCurve[] _b;

    public int Inputs { get; private init; }

    public int Outputs { get; private init; }

    public IccPcsEncoding Encoding { get; private init; }

    /// <summary>Reads the table element of <paramref name="tag"/>; <paramref name="xyzInput"/> when its input is the
    /// PCS in XYZ, which a lut8 or lut16 matrix then applies to (ICC.1, 10.8).</summary>
    public static IccLut Read(BigEndianReader tag, bool xyzInput = false)
    {
        var start = tag.Position;
        var type = tag.ReadUInt32();
        tag.Skip(4);
        return type switch
        {
            Lut8 => ReadLegacy(tag, false, xyzInput),
            Lut16 => ReadLegacy(tag, true, xyzInput),
            AToB => ReadAToB(tag, start, false),
            BToA => ReadAToB(tag, start, true),
            _ => throw new InvalidDataException("An ICC table has no table type it can have."),
        };
    }

    public void Evaluate(ReadOnlySpan<float> input, Span<float> output)
    {
        if (_fromConnection)
        {
            EvaluateFromConnection(input, output);
            return;
        }

        Span<float> values = stackalloc float[Math.Max(Inputs, Math.Max(Outputs, 3))];
        input[..Inputs].CopyTo(values);
        if (_inputMatrix != null)
        {
            var (x, y, z) = (values[0], values[1], values[2]);
            values[0] = Math.Clamp((float)(_inputMatrix[0] * x + _inputMatrix[1] * y + _inputMatrix[2] * z), 0, 1);
            values[1] = Math.Clamp((float)(_inputMatrix[3] * x + _inputMatrix[4] * y + _inputMatrix[5] * z), 0, 1);
            values[2] = Math.Clamp((float)(_inputMatrix[6] * x + _inputMatrix[7] * y + _inputMatrix[8] * z), 0, 1);
        }

        for (var i = 0; i < Inputs; i++)
        {
            values[i] = _a != null ? _a[i].Evaluate(values[i]) : values[i];
        }

        if (_clut != null)
        {
            Span<float> looked = stackalloc float[_clut.Outputs];
            _clut.Evaluate(values[..Inputs], looked);
            looked.CopyTo(values);
        }

        if (_m != null)
        {
            for (var o = 0; o < Outputs; o++)
            {
                values[o] = _m[o].Evaluate(values[o]);
            }
        }

        if (_matrix != null && Outputs == 3)
        {
            var (x, y, z) = (values[0], values[1], values[2]);
            values[0] = (float)(_matrix[0] * x + _matrix[1] * y + _matrix[2] * z + _matrix[9]);
            values[1] = (float)(_matrix[3] * x + _matrix[4] * y + _matrix[5] * z + _matrix[10]);
            values[2] = (float)(_matrix[6] * x + _matrix[7] * y + _matrix[8] * z + _matrix[11]);
        }

        for (var o = 0; o < Outputs; o++)
        {
            output[o] = _b != null ? _b[o].Evaluate(values[o]) : Math.Clamp(values[o], 0, 1);
        }
    }

    private void EvaluateFromConnection(ReadOnlySpan<float> input, Span<float> output)
    {
        Span<float> values = stackalloc float[Math.Max(Inputs, Math.Max(Outputs, 3))];
        for (var i = 0; i < Inputs; i++)
        {
            values[i] = _b != null ? _b[i].Evaluate(input[i]) : input[i];
        }

        if (_matrix != null && Inputs == 3)
        {
            var (x, y, z) = (values[0], values[1], values[2]);
            values[0] = Math.Clamp((float)(_matrix[0] * x + _matrix[1] * y + _matrix[2] * z + _matrix[9]), 0, 1);
            values[1] = Math.Clamp((float)(_matrix[3] * x + _matrix[4] * y + _matrix[5] * z + _matrix[10]), 0, 1);
            values[2] = Math.Clamp((float)(_matrix[6] * x + _matrix[7] * y + _matrix[8] * z + _matrix[11]), 0, 1);
        }

        if (_m != null)
        {
            for (var i = 0; i < Inputs; i++)
            {
                values[i] = _m[i].Evaluate(values[i]);
            }
        }

        if (_clut != null)
        {
            Span<float> looked = stackalloc float[_clut.Outputs];
            _clut.Evaluate(values[..Inputs], looked);
            looked.CopyTo(values);
        }

        for (var o = 0; o < Outputs; o++)
        {
            output[o] = _a != null ? _a[o].Evaluate(values[o]) : Math.Clamp(values[o], 0, 1);
        }
    }

    private static IccLut ReadLegacy(BigEndianReader tag, bool wide, bool xyzInput)
    {
        var inputs = tag.ReadByte();
        var outputs = tag.ReadByte();
        var points = tag.ReadByte();
        tag.Skip(1);
        var matrix = new double[9];
        var identity = true;
        for (var i = 0; i < matrix.Length; i++)
        {
            matrix[i] = tag.ReadInt32() / 65536.0;
            identity &= matrix[i] == (i % 4 == 0 ? 1 : 0);
        }

        Check(inputs, outputs);
        int inputEntries = 256, outputEntries = 256;
        if (wide)
        {
            inputEntries = tag.ReadUInt16();
            outputEntries = tag.ReadUInt16();
            if (inputEntries is < 2 or > 4096 || outputEntries is < 2 or > 4096)
            {
                throw new InvalidDataException("An ICC lut16 has curve tables of a length it cannot have.");
            }
        }

        var unit = wide ? 65535f : 255f;
        float Next() => (wide ? tag.ReadUInt16() : tag.ReadByte()) / unit;
        var a = new IccCurve[inputs];
        for (var i = 0; i < inputs; i++)
        {
            a[i] = Table(inputEntries, Next);
        }

        var grid = new int[inputs];
        Array.Fill(grid, (int)points);
        var values = Values(tag, grid, outputs, wide ? 2 : 1);
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = Next();
        }

        var b = new IccCurve[outputs];
        for (var o = 0; o < outputs; o++)
        {
            b[o] = Table(outputEntries, Next);
        }

        return new IccLut
        {
            Inputs = inputs,
            Outputs = outputs,
            Encoding = wide ? IccPcsEncoding.Legacy16 : IccPcsEncoding.Byte,
            _inputMatrix = xyzInput && inputs == 3 && !identity ? matrix : null,
            _a = a,
            _clut = new IccClut(grid, outputs, values),
            _b = b,
        };
    }

    private static IccCurve Table(int count, Func<float> next)
    {
        var table = new float[count];
        for (var i = 0; i < count; i++)
        {
            table[i] = next();
        }

        return IccCurve.FromTable(table);
    }

    private static IccLut ReadAToB(BigEndianReader tag, int start, bool fromConnection)
    {
        var inputs = tag.ReadByte();
        var outputs = tag.ReadByte();
        tag.Skip(2);
        Check(inputs, outputs);
        var b = tag.ReadUInt32();
        var matrix = tag.ReadUInt32();
        var m = tag.ReadUInt32();
        var clut = tag.ReadUInt32();
        var a = tag.ReadUInt32();
        var (device, connection) = fromConnection ? (outputs, inputs) : (inputs, outputs);
        return new IccLut
        {
            Inputs = inputs,
            Outputs = outputs,
            Encoding = IccPcsEncoding.Version4,
            _fromConnection = fromConnection,
            _a = a == 0 ? null : Curves(tag, start, a, device),
            _clut = clut == 0 ? null : ReadClut(At(tag, start, clut), inputs, outputs),
            _m = m == 0 ? null : Curves(tag, start, m, connection),
            _matrix = matrix == 0 ? null : ReadMatrix(At(tag, start, matrix)),
            _b = b == 0 ? null : Curves(tag, start, b, connection),
        };
    }

    private static IccCurve[] Curves(BigEndianReader tag, int start, uint offset, int count)
    {
        var reader = At(tag, start, offset);
        var curves = new IccCurve[count];
        for (var i = 0; i < count; i++)
        {
            curves[i] = IccCurve.Read(reader);
            reader.Position = (reader.Position + 3) & ~3;
        }

        return curves;
    }

    private static IccClut ReadClut(BigEndianReader reader, int inputs, int outputs)
    {
        var grid = new int[inputs];
        for (var i = 0; i < 16; i++)
        {
            var points = reader.ReadByte();
            if (i < inputs)
            {
                grid[i] = points;
            }
        }

        var precision = reader.ReadByte();
        reader.Skip(3);
        if (precision is not (1 or 2))
        {
            throw new InvalidDataException("An ICC lookup table has a precision it cannot have.");
        }

        var values = Values(reader, grid, outputs, precision);
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = precision == 2 ? reader.ReadUInt16() / 65535f : reader.ReadByte() / 255f;
        }

        return new IccClut(grid, outputs, values);
    }

    private static float[] Values(BigEndianReader reader, int[] grid, int outputs, int bytes)
    {
        var count = IccClut.NodeCount(grid, outputs);
        if ((long)count * bytes > reader.Remaining)
        {
            throw new InvalidDataException("An ICC lookup table has more nodes than its tag holds.");
        }

        return new float[count];
    }

    private static double[] ReadMatrix(BigEndianReader reader)
    {
        var matrix = new double[12];
        for (var i = 0; i < matrix.Length; i++)
        {
            matrix[i] = reader.ReadInt32() / 65536.0;
        }

        return matrix;
    }

    private static BigEndianReader At(BigEndianReader tag, int start, uint offset)
    {
        if (offset >= tag.End - start)
        {
            throw new InvalidDataException("An ICC table has a part outside its tag.");
        }

        return new BigEndianReader(tag.Data, start + (int)offset, tag.End);
    }

    private static void Check(int inputs, int outputs)
    {
        if (inputs is < 1 or > 15 || outputs is < 1 or > 15)
        {
            throw new InvalidDataException("An ICC table has a channel count it cannot have.");
        }
    }
}
