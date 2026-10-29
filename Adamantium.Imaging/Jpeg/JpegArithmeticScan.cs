using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Adamantium.Imaging.Jpeg;

/// <summary>One arithmetic-coded scan of a JPEG frame (ITU-T T.81): its units decoded by the statistical models of F.1.4
/// - sequential, a block's DC difference conditioned on the previous block's and its AC coefficients by their zig-zag
/// index - and, for a progressive frame, the first and later successive approximation scans of G.1.3. The statistics
/// start afresh at the scan and at each restart interval.</summary>
[SkipLocalsInit]
internal sealed class JpegArithmeticScan
{
    private const int DcBins = 64;
    private const int AcBins = 256;

    private const int DcMagnitude = 20;
    private const int AcMagnitudeLow = 189;
    private const int AcMagnitudeHigh = 217;

    private readonly JpegStream _stream;
    private readonly JpegComponent[] _scan;
    private readonly int _start;
    private readonly int _end;
    private readonly int _high;
    private readonly int _low;
    private readonly byte[][] _dc = new byte[4][];
    private readonly byte[][] _ac = new byte[4][];
    private readonly byte[] _dcBounds;
    private readonly byte[] _acThresholds;
    private readonly int[] _predictors;
    private readonly int[] _contexts;

    /// <summary>The scan of <paramref name="scan"/>'s components, coefficients <paramref name="start"/> to
    /// <paramref name="end"/> at successive approximation bits <paramref name="high"/> and <paramref name="low"/>; DC
    /// conditioning by the bounds L and U of each table (L in the low nibble of <paramref name="dcBounds"/>, U in the
    /// high), AC conditioning by each table's Kx (<paramref name="acThresholds"/>).</summary>
    public JpegArithmeticScan(JpegStream stream, JpegComponent[] scan, int start, int end, int high, int low,
        byte[] dcBounds, byte[] acThresholds)
    {
        _stream = stream;
        _scan = scan;
        (_start, _end, _high, _low) = (start, end, high, low);
        _dcBounds = dcBounds;
        _acThresholds = acThresholds;
        _predictors = new int[scan.Length];
        _contexts = new int[scan.Length];
        for (var i = 0; i < 4; i++)
        {
            _dc[i] = new byte[DcBins];
            _ac[i] = new byte[AcBins];
        }
    }

    /// <summary>Decodes the scan's <paramref name="units"/> from <paramref name="at"/>; where its data ends.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int Decode(ReadOnlySpan<byte> data, int at, int units)
    {
        var decoder = new JpegArithmeticDecoder(data, at);
        var interval = _stream.RestartInterval;
        Reset();
        for (var unit = 0; unit < units; unit++)
        {
            if (interval > 0 && unit > 0 && unit % interval == 0)
            {
                decoder.Restart();
                Reset();
            }

            if (_scan.Length == 1)
            {
                var component = _scan[0];
                Block(ref decoder, 0, component, unit % component.BlocksAcross, unit / component.BlocksAcross);
                continue;
            }

            var (across, down) = (unit % _stream.McusAcross, unit / _stream.McusAcross);
            for (var c = 0; c < _scan.Length; c++)
            {
                var component = _scan[c];
                for (var v = 0; v < component.Vertical; v++)
                {
                    for (var h = 0; h < component.Horizontal; h++)
                    {
                        Block(ref decoder, c, component, across * component.Horizontal + h,
                            down * component.Vertical + v);
                    }
                }
            }
        }

        return decoder.Position;
    }

    private void Reset()
    {
        foreach (var bins in _dc)
        {
            Array.Clear(bins);
        }

        foreach (var bins in _ac)
        {
            Array.Clear(bins);
        }

        Array.Clear(_predictors);
        Array.Clear(_contexts);
    }

    private void Block(ref JpegArithmeticDecoder decoder, int c, JpegComponent component, int across, int down)
    {
        if (!_stream.Progressive)
        {
            Span<short> natural = stackalloc short[64];
            natural.Clear();
            var difference = DecodeDifference(ref decoder, c, component);
            _predictors[c] += difference;
            natural[0] = (short)Math.Clamp(_predictors[c], short.MinValue, short.MaxValue);
            var rows = DecodeAc(ref decoder, component, natural, 1, 63, 0, true);
            _stream.Store(component, natural, rows, across, down);
            return;
        }

        var kept = _stream.Kept;
        var coefficients = component.Coefficients.AsSpan((down * component.StoredAcross + across) * kept, kept);
        if (_start == 0)
        {
            if (_high == 0)
            {
                _predictors[c] += DecodeDifference(ref decoder, c, component);
                coefficients[0] = (short)(_predictors[c] << _low);
            }
            else if (decoder.DecodeHalf() != 0)
            {
                coefficients[0] |= (short)(1 << _low);
            }

            return;
        }

        if (_high == 0)
        {
            DecodeAc(ref decoder, component, coefficients, _start, _end, _low, false);
        }
        else
        {
            Refine(ref decoder, component, coefficients);
        }
    }

    private int DecodeDifference(ref JpegArithmeticDecoder decoder, int c, JpegComponent component)
    {
        var bins = _dc[component.DcSelector];
        var context = _contexts[c];
        if (decoder.Decode(ref bins[context]) == 0)
        {
            _contexts[c] = 0;
            return 0;
        }

        var negative = decoder.Decode(ref bins[context + 1]);
        var magnitude = DecodeMagnitude(ref decoder, bins, context + 2 + negative, DcMagnitude, DcMagnitude + 1) + 1;
        var bounds = _dcBounds[component.DcSelector];
        int lower = (1 << (bounds & 15)) >> 1, upper = 1 << (bounds >> 4);
        _contexts[c] = magnitude <= lower ? 0 : magnitude <= upper ? 4 + 4 * negative : 12 + 4 * negative;
        return negative != 0 ? -magnitude : magnitude;
    }

    private int DecodeAc(ref JpegArithmeticDecoder decoder, JpegComponent component, scoped Span<short> coefficients,
        int start, int end, int low, bool natural)
    {
        var bins = _ac[component.AcSelector];
        var threshold = _acThresholds[component.AcSelector];
        var rows = 0;
        for (var k = start; k <= end; k++)
        {
            if (decoder.Decode(ref bins[3 * (k - 1)]) != 0)
            {
                break;
            }

            while (decoder.Decode(ref bins[3 * (k - 1) + 1]) == 0)
            {
                if (++k > end)
                {
                    throw new InvalidDataException("A JPEG block has more coefficients than its band.");
                }
            }

            var negative = decoder.DecodeHalf();
            var first = 3 * (k - 1) + 2;
            var magnitude = DecodeMagnitude(ref decoder, bins, first, first,
                k <= threshold ? AcMagnitudeLow : AcMagnitudeHigh) + 1;
            var value = (short)((negative != 0 ? -magnitude : magnitude) * (1 << low));
            if (natural)
            {
                var at = JpegStream.ZigZag[k];
                coefficients[at] = value;
                rows |= JpegIdct.Mask(at);
            }
            else
            {
                coefficients[k] = value;
            }
        }

        return rows;
    }

    private static int DecodeMagnitude(ref JpegArithmeticDecoder decoder, byte[] bins, int first, int second,
        int third)
    {
        if (decoder.Decode(ref bins[first]) == 0)
        {
            return 0;
        }

        var bound = 2;
        var bin = second;
        if (decoder.Decode(ref bins[bin]) != 0)
        {
            bound = 4;
            bin = third;
            while (decoder.Decode(ref bins[bin]) != 0)
            {
                bound <<= 1;
                bin++;
                if (bound > 0x8000)
                {
                    throw new InvalidDataException("A JPEG coefficient beyond 16 bits.");
                }
            }
        }

        bound >>= 1;
        var magnitude = bound;
        bin += 14;
        while ((bound >>= 1) != 0)
        {
            if (decoder.Decode(ref bins[bin]) != 0)
            {
                magnitude |= bound;
            }
        }

        return magnitude;
    }

    private void Refine(ref JpegArithmeticDecoder decoder, JpegComponent component, Span<short> coefficients)
    {
        var bins = _ac[component.AcSelector];
        int plus = 1 << _low, minus = -1 << _low;
        var last = _end;
        while (last >= _start && coefficients[last] == 0)
        {
            last--;
        }

        for (var k = _start; k <= _end; k++)
        {
            if (k > last && decoder.Decode(ref bins[3 * (k - 1)]) != 0)
            {
                break;
            }

            while (true)
            {
                ref var coefficient = ref coefficients[k];
                if (coefficient != 0)
                {
                    if (decoder.Decode(ref bins[3 * (k - 1) + 2]) != 0 && (coefficient & plus) == 0)
                    {
                        coefficient += (short)(coefficient >= 0 ? plus : minus);
                    }

                    break;
                }

                if (decoder.Decode(ref bins[3 * (k - 1) + 1]) != 0)
                {
                    coefficient = (short)(decoder.DecodeHalf() != 0 ? minus : plus);
                    break;
                }

                if (++k > _end)
                {
                    throw new InvalidDataException("A JPEG block has more coefficients than its band.");
                }
            }
        }
    }
}
