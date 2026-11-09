using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>The QCD or QCC marker's quantization (ITU-T T.800, A.6.4 and E.1): guard bits and the exponent and
/// mantissa of each subband's step.</summary>
internal sealed class J2kQuantization
{
    private const int None = 0;
    private const int Derived = 1;

    private int _style;
    private List<(int Exponent, int Mantissa)> _steps;

    public int GuardBits { get; private set; }

    public static J2kQuantization Read(BigEndianReader reader)
    {
        var flags = reader.ReadByte();
        var quantization = new J2kQuantization
        {
            _style = flags & 31,
            GuardBits = flags >> 5,
            _steps = [],
        };
        if (quantization._style > 2)
        {
            throw new InvalidDataException("A JPEG 2000 quantization has no style " + quantization._style + ".");
        }

        while (reader.Remaining > 0)
        {
            if (quantization._style == None)
            {
                quantization._steps.Add((reader.ReadByte() >> 3, 0));
            }
            else
            {
                var value = reader.ReadUInt16();
                quantization._steps.Add((value >> 11, value & 0x7FF));
            }

            if (quantization._style == Derived)
            {
                break;
            }
        }

        if (quantization._steps.Count == 0)
        {
            throw new InvalidDataException("A JPEG 2000 quantization has no steps.");
        }

        return quantization;
    }

    /// <summary>The step of a subband: its index in the order LL, then HL, LH, HH from the lowest resolution up;
    /// <paramref name="level"/> is the decomposition level the subband is at.</summary>
    public (int Exponent, int Mantissa) Step(int band, int level, int levels)
    {
        if (_style == Derived)
        {
            var (exponent, mantissa) = _steps[0];
            return (Math.Max(0, exponent - levels + level), mantissa);
        }

        return _steps[Math.Min(band, _steps.Count - 1)];
    }
}
