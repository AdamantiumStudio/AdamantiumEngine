using System;
using System.IO;

namespace Adamantium.Imaging.Icc;

/// <summary>A one-dimensional curve of a profile (ICC.1, 10.6 curveType and 10.18 parametricCurveType): the identity,
/// a gamma, a table, or one of the five parametric functions. It maps 0..1 to 0..1.</summary>
internal sealed class IccCurve
{
    private const uint Curve = 0x63757276;
    private const uint Parametric = 0x70617261;
    private const int MaxTable = 1 << 16;

    private readonly float[] _table;
    private readonly int _function = -1;
    private readonly double[] _parameters;

    private IccCurve(float[] table, int function, double[] parameters)
    {
        _table = table;
        _function = function;
        _parameters = parameters;
    }

    public static IccCurve Identity { get; } = new(null, 0, [1.0]);

    /// <summary>A curve made from a table of values 0..1, evenly spaced over the input.</summary>
    public static IccCurve FromTable(float[] table) => new(table, -1, null);

    /// <summary>Reads a curv or para element at the reader's place; the reader ends after it.</summary>
    public static IccCurve Read(BigEndianReader reader)
    {
        var type = reader.ReadUInt32();
        reader.Skip(4);
        if (type == Curve)
        {
            var count = reader.ReadUInt32();
            if (count == 0)
            {
                return Identity;
            }

            if (count == 1)
            {
                return new IccCurve(null, 0, [reader.ReadUInt16() / 256.0]);
            }

            if (count > MaxTable)
            {
                throw new InvalidDataException("An ICC curve has more entries than any needs.");
            }

            if (count * 2L > reader.Remaining)
            {
                throw new InvalidDataException("An ICC curve has more entries than its tag holds.");
            }

            var table = new float[count];
            for (var i = 0; i < table.Length; i++)
            {
                table[i] = reader.ReadUInt16() / 65535f;
            }

            return new IccCurve(table, -1, null);
        }

        if (type != Parametric)
        {
            throw new InvalidDataException("An ICC curve has no curve type it can have.");
        }

        var function = reader.ReadUInt16();
        reader.Skip(2);
        var counts = new[] { 1, 3, 4, 5, 7 };
        if (function >= counts.Length)
        {
            throw new InvalidDataException("An ICC parametric curve has no function " + function + ".");
        }

        var parameters = new double[counts[function]];
        for (var i = 0; i < parameters.Length; i++)
        {
            parameters[i] = reader.ReadInt32() / 65536.0;
        }

        return new IccCurve(null, function, parameters);
    }

    public float Evaluate(float x)
    {
        x = Math.Clamp(x, 0, 1);
        if (_table != null)
        {
            var at = x * (_table.Length - 1);
            var i = Math.Min((int)at, _table.Length - 2);
            var t = at - i;
            return _table[i] + (_table[i + 1] - _table[i]) * t;
        }

        var p = _parameters;
        double y = _function switch
        {
            0 => Power(x, p[0]),
            1 => x >= -p[2] / p[1] ? Power(p[1] * x + p[2], p[0]) : 0,
            2 => x >= -p[2] / p[1] ? Power(p[1] * x + p[2], p[0]) + p[3] : p[3],
            3 => x >= p[4] ? Power(p[1] * x + p[2], p[0]) : p[3] * x,
            _ => x >= p[4] ? Power(p[1] * x + p[2], p[0]) + p[5] : p[3] * x + p[6],
        };
        return (float)Math.Clamp(y, 0, 1);
    }

    private static double Power(double value, double exponent) => value <= 0 ? 0 : Math.Pow(value, exponent);
}
