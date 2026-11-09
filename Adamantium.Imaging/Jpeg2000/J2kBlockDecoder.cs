using System;
using Adamantium.Imaging.Mq;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>Decodes the coefficients of code-blocks (ITU-T T.800, annex D): significance propagation, magnitude
/// refinement and cleanup passes from the most significant bit-plane down, in stripes of four rows. Its buffers are
/// kept from one block to the next.</summary>
internal sealed class J2kBlockDecoder
{
    private const int Significant = 1;
    private const int Negative = 2;
    private const int Visited = 4;
    private const int Refined = 8;

    private const int SignContext = 9;
    private const int RefinementContext = 14;
    private const int RunContext = 17;
    private const int UniformContext = 18;
    private const int Contexts = 19;

    private static readonly byte[] LowHighZero = ZeroTable(false);
    private static readonly byte[] HighLowZero = ZeroTable(true);

    private static readonly byte[] HighHighZero = HighHighTable();

    private readonly byte[] _contexts = new byte[Contexts];
    private byte[] _flags = [];
    private int[] _magnitudes = [];
    private byte[] _known = [];
    private int _width;
    private int _height;
    private int _stride;
    private bool _causal;
    private byte[] _zero;
    private MqDecoder _mq;
    private J2kRawBits _raw;

    /// <summary>Decodes <paramref name="block"/> into its subband's coefficients.</summary>
    public void Decode(J2kCodeBlock block, J2kBand band, J2kComponentStyle style, int roiShift)
    {
        _width = block.Width;
        _height = block.Height;
        if (_width <= 0 || _height <= 0)
        {
            return;
        }

        var planes = band.Planes - block.ZeroPlanes;
        var passes = Math.Min(block.Passes, 3 * planes - 2);
        if (planes <= 0 || passes <= 0 || planes > 31)
        {
            return;
        }

        _stride = _width + 2;
        var size = _stride * (_height + 2);
        if (_flags.Length < size)
        {
            _flags = new byte[size];
        }

        Array.Clear(_flags, 0, size);
        if (_magnitudes.Length < _width * _height)
        {
            _magnitudes = new int[_width * _height];
            _known = new byte[_width * _height];
        }

        Array.Clear(_magnitudes, 0, _width * _height);
        _causal = style.Has(J2kComponentStyle.VerticallyCausalFlag);
        _zero = band.Orientation switch
        {
            J2kBand.HighLow => HighLowZero,
            J2kBand.HighHigh => HighHighZero,
            _ => LowHighZero,
        };
        ResetContexts();
        var pass = 0;
        foreach (var segment in block.Segments)
        {
            var data = segment.Data();
            var raw = J2kCodeBlock.IsRaw(segment.FirstPass, style);
            if (raw)
            {
                _raw = new J2kRawBits(data);
            }
            else
            {
                _mq = new MqDecoder(data, 0, data.Length);
            }

            for (var i = 0; i < segment.Passes && pass < passes; i++, pass++)
            {
                var plane = planes - 1 - (pass + 2) / 3;
                var kind = pass == 0 ? 2 : (pass - 1) % 3;
                switch (kind)
                {
                    case 0:
                        Propagate(plane, raw);
                        break;
                    case 1:
                        Refine(plane, raw);
                        break;
                    default:
                        Cleanup(plane, style.Has(J2kComponentStyle.SegmentationSymbolsFlag));
                        break;
                }

                if (style.Has(J2kComponentStyle.ResetFlag))
                {
                    ResetContexts();
                }
            }
        }

        Write(block, band, style.Reversible, roiShift);
    }

    private void ResetContexts()
    {
        Array.Clear(_contexts);
        MqDecoder.Reset(_contexts, 0, 4, 0);
        MqDecoder.Reset(_contexts, RunContext, 3, 0);
        MqDecoder.Reset(_contexts, UniformContext, 46, 0);
    }

    private void Propagate(int plane, bool raw)
    {
        for (var stripe = 0; stripe < _height; stripe += 4)
        {
            var end = Math.Min(stripe + 4, _height);
            for (var x = 0; x < _width; x++)
            {
                for (var y = stripe; y < end; y++)
                {
                    var at = (y + 1) * _stride + x + 1;
                    if ((_flags[at] & Significant) != 0)
                    {
                        continue;
                    }

                    var context = ZeroContext(at, y);
                    if (context == 0)
                    {
                        continue;
                    }

                    var bit = raw ? _raw.ReadBit() : _mq.Decode(_contexts, context);
                    _flags[at] |= Visited;
                    if (bit == 1)
                    {
                        BecomeSignificant(at, y, x, plane, raw);
                    }
                }
            }
        }
    }

    private void Refine(int plane, bool raw)
    {
        for (var stripe = 0; stripe < _height; stripe += 4)
        {
            var end = Math.Min(stripe + 4, _height);
            for (var x = 0; x < _width; x++)
            {
                for (var y = stripe; y < end; y++)
                {
                    var at = (y + 1) * _stride + x + 1;
                    var flags = _flags[at];
                    if ((flags & (Significant | Visited)) != Significant)
                    {
                        continue;
                    }

                    int bit;
                    if (raw)
                    {
                        bit = _raw.ReadBit();
                    }
                    else
                    {
                        var context = (flags & Refined) != 0 ? RefinementContext + 2
                            : HasNeighbors(at, y) ? RefinementContext + 1
                            : RefinementContext;
                        bit = _mq.Decode(_contexts, context);
                    }

                    _magnitudes[y * _width + x] |= bit << plane;
                    _known[y * _width + x] = (byte)plane;
                    _flags[at] |= Refined;
                }
            }
        }
    }

    private void Cleanup(int plane, bool segmentationSymbols)
    {
        for (var stripe = 0; stripe < _height; stripe += 4)
        {
            var end = Math.Min(stripe + 4, _height);
            for (var x = 0; x < _width; x++)
            {
                var y = stripe;
                if (end - stripe == 4 && RunAllowed(x, stripe))
                {
                    if (_mq.Decode(_contexts, RunContext) == 0)
                    {
                        continue;
                    }

                    var run = _mq.Decode(_contexts, UniformContext) << 1;
                    run |= _mq.Decode(_contexts, UniformContext);
                    y = stripe + run;
                    BecomeSignificant((y + 1) * _stride + x + 1, y, x, plane, false);
                    y++;
                }

                for (; y < end; y++)
                {
                    var at = (y + 1) * _stride + x + 1;
                    if ((_flags[at] & (Significant | Visited)) != 0)
                    {
                        continue;
                    }

                    if (_mq.Decode(_contexts, ZeroContext(at, y)) == 1)
                    {
                        BecomeSignificant(at, y, x, plane, false);
                    }
                }
            }
        }

        for (var y = 0; y < _height; y++)
        {
            var row = (y + 1) * _stride + 1;
            for (var x = 0; x < _width; x++)
            {
                _flags[row + x] &= unchecked((byte)~Visited);
            }
        }

        if (segmentationSymbols)
        {
            for (var i = 0; i < 4; i++)
            {
                _mq.Decode(_contexts, UniformContext);
            }
        }
    }

    private bool RunAllowed(int x, int stripe)
    {
        for (var y = stripe; y < stripe + 4; y++)
        {
            var at = (y + 1) * _stride + x + 1;
            if ((_flags[at] & (Significant | Visited)) != 0 || HasNeighbors(at, y))
            {
                return false;
            }
        }

        return true;
    }

    private void BecomeSignificant(int at, int y, int x, int plane, bool raw)
    {
        int negative;
        if (raw)
        {
            negative = _raw.ReadBit();
        }
        else
        {
            var (context, flip) = SignContextOf(at, y);
            negative = _mq.Decode(_contexts, context) ^ flip;
        }

        _flags[at] |= (byte)(Significant | (negative == 1 ? Negative : 0));
        _magnitudes[y * _width + x] |= 1 << plane;
        _known[y * _width + x] = (byte)plane;
    }

    private int Sig(int at) => _flags[at] & Significant;

    private bool BelowCounts(int y) => !_causal || (y & 3) != 3;

    private bool HasNeighbors(int at, int y)
    {
        var s = _stride;
        var any = Sig(at - 1) | Sig(at + 1) | Sig(at - s) | Sig(at - s - 1) | Sig(at - s + 1);
        if (BelowCounts(y))
        {
            any |= Sig(at + s) | Sig(at + s - 1) | Sig(at + s + 1);
        }

        return any != 0;
    }

    private int ZeroContext(int at, int y)
    {
        var s = _stride;
        var horizontal = Sig(at - 1) + Sig(at + 1);
        var vertical = Sig(at - s);
        var diagonal = Sig(at - s - 1) + Sig(at - s + 1);
        if (BelowCounts(y))
        {
            vertical += Sig(at + s);
            diagonal += Sig(at + s - 1) + Sig(at + s + 1);
        }

        return _zero[(horizontal * 3 + vertical) * 5 + diagonal];
    }

    private (int Context, int Flip) SignContextOf(int at, int y)
    {
        var horizontal = Math.Clamp(Contribution(at - 1) + Contribution(at + 1), -1, 1);
        var below = BelowCounts(y) ? Contribution(at + _stride) : 0;
        var vertical = Math.Clamp(Contribution(at - _stride) + below, -1, 1);
        if (horizontal < 0 || (horizontal == 0 && vertical < 0))
        {
            return (SignOf(-horizontal, -vertical), 1);
        }

        return (SignOf(horizontal, vertical), 0);
    }

    private static int SignOf(int horizontal, int vertical) =>
        horizontal == 1 ? SignContext + 3 + vertical : SignContext + vertical;

    private int Contribution(int at) =>
        (_flags[at] & Significant) == 0 ? 0 : (_flags[at] & Negative) != 0 ? -1 : 1;

    private void Write(J2kCodeBlock block, J2kBand band, bool reversible, int roiShift)
    {
        var roi = roiShift > 0 && roiShift < 31 ? 1 << roiShift : int.MaxValue;
        for (var y = 0; y < _height; y++)
        {
            var target = (block.Y0 - band.Y0 + y) * band.Width + block.X0 - band.X0;
            for (var x = 0; x < _width; x++)
            {
                var magnitude = _magnitudes[y * _width + x];
                if (magnitude == 0)
                {
                    continue;
                }

                var known = _known[y * _width + x];
                if (magnitude >= roi)
                {
                    magnitude >>= roiShift;
                    known = (byte)Math.Max(0, known - roiShift);
                }

                var negative = (_flags[(y + 1) * _stride + x + 1] & Negative) != 0;
                if (reversible)
                {
                    magnitude += (1 << known) >> 1;
                    band.Integers[target + x] = negative ? -magnitude : magnitude;
                }
                else
                {
                    var value = (magnitude + 0.5f * (1 << known)) * band.Step;
                    band.Values[target + x] = negative ? -value : value;
                }
            }
        }
    }

    private static byte[] ZeroTable(bool swap)
    {
        var table = new byte[3 * 3 * 5];
        for (var h = 0; h < 3; h++)
        {
            for (var v = 0; v < 3; v++)
            {
                for (var d = 0; d < 5; d++)
                {
                    var (a, b) = swap ? (v, h) : (h, v);
                    byte context = a switch
                    {
                        2 => 8,
                        1 => b >= 1 ? (byte)7 : d >= 1 ? (byte)6 : (byte)5,
                        _ => b switch
                        {
                            2 => 4,
                            1 => 3,
                            _ => d >= 2 ? (byte)2 : (byte)d,
                        },
                    };
                    table[(h * 3 + v) * 5 + d] = context;
                }
            }
        }

        return table;
    }

    private static byte[] HighHighTable()
    {
        var table = new byte[3 * 3 * 5];
        for (var h = 0; h < 3; h++)
        {
            for (var v = 0; v < 3; v++)
            {
                for (var d = 0; d < 5; d++)
                {
                    var hv = h + v;
                    byte context = d switch
                    {
                        >= 3 => 8,
                        2 => hv >= 1 ? (byte)7 : (byte)6,
                        1 => hv >= 2 ? (byte)5 : hv == 1 ? (byte)4 : (byte)3,
                        _ => hv >= 2 ? (byte)2 : (byte)hv,
                    };
                    table[(h * 3 + v) * 5 + d] = context;
                }
            }
        }

        return table;
    }
}
