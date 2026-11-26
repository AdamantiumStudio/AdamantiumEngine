using System.Numerics;
using System.Runtime.InteropServices;

namespace Adamantium.Pulsar.Decoding;

/// <summary>Decodes Layer III frames into samples: ISO/IEC 11172-3 2.4.3.4 and its annex, with the lower rates of
/// 13818-3. It keeps what a frame leaves for the next - the overlap of its transform and the synthesis filter's history -
/// and allocates nothing once made.</summary>
internal sealed class Mp3Layer3
{
    public const int Lines = 576;
    private const int Subbands = 32;
    private const int SubbandLines = 18;

    private readonly Mp3Granule[] _granules = new Mp3Granule[4];
    private readonly bool[] _scfsi = new bool[8];
    private readonly int[][] _longScalefactors = [new int[22], new int[22]];
    // By band and window: band * 3 + window.
    private readonly int[][] _shortScalefactors = [new int[13 * 3], new int[13 * 3]];
    // Of the lower rates' right channel: the intensity position that says none, by band.
    private readonly int[] _longIllegal = new int[22];
    private readonly int[] _shortIllegal = new int[13 * 3];
    private readonly int[][] _quantized = [new int[Lines], new int[Lines]];
    private readonly float[][] _lines = [new float[Lines], new float[Lines]];
    // After the last line that is not 0, by channel.
    private readonly int[] _nonzero = new int[2];
    private readonly bool[] _isIntensity = new bool[Lines];
    private readonly float[] _reordered = new float[Lines];
    private readonly float[] _transformed = new float[36];
    private readonly float[][] _overlap = [new float[Lines], new float[Lines]];
    // By time and subband: time * 32 + subband.
    private readonly float[] _hybrid = new float[Lines];
    private readonly float[][] _history = [new float[2048], new float[2048]];
    private readonly int[] _historyAt = new int[2];
    private readonly int[] _slen = new int[4];

    /// <summary>Forgets what the frames before left: for a frame after a seek.</summary>
    public void Reset()
    {
        foreach (var overlap in _overlap)
        {
            Array.Clear(overlap);
        }

        foreach (var history in _history)
        {
            Array.Clear(history);
        }
    }

    /// <summary>After a frame of one channel in a stream of two: the second goes on from where the first, heard in both,
    /// got to, not from where it was before.</summary>
    public void ShareFirstChannel()
    {
        _overlap[0].CopyTo(_overlap[1], 0);
        _history[0].CopyTo(_history[1], 0);
        _historyAt[1] = _historyAt[0];
    }

    /// <summary>Reads the side information; false where it can not be.</summary>
    public bool ReadSideInfo(Mp3FrameHeader header, ReadOnlySpan<byte> sideInfo, out int mainDataBegin)
    {
        var bits = new Mp3Bits(sideInfo);
        var channels = header.Channels;
        if (header.IsFirst)
        {
            mainDataBegin = bits.Read(9);
            bits.Read(channels == 1 ? 5 : 3);
            for (var channel = 0; channel < channels; channel++)
            {
                for (var band = 0; band < 4; band++)
                {
                    _scfsi[channel * 4 + band] = bits.ReadBit() != 0;
                }
            }
        }
        else
        {
            mainDataBegin = bits.Read(8);
            bits.Read(channels == 1 ? 1 : 2);
        }

        for (var granule = 0; granule < header.Granules; granule++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                ref var info = ref _granules[granule * 2 + channel];
                info.Part23Length = bits.Read(12);
                info.BigValues = bits.Read(9);
                info.GlobalGain = bits.Read(8);
                info.ScalefacCompress = bits.Read(header.IsFirst ? 4 : 9);
                info.WindowSwitching = bits.ReadBit() != 0;
                if (info.WindowSwitching)
                {
                    info.BlockType = bits.Read(2);
                    info.Mixed = bits.ReadBit() != 0;
                    info.Table0 = bits.Read(5);
                    info.Table1 = bits.Read(5);
                    info.Table2 = 0;
                    info.SubblockGain0 = bits.Read(3);
                    info.SubblockGain1 = bits.Read(3);
                    info.SubblockGain2 = bits.Read(3);
                    // Block type 0 can not switch windows.
                    if (info.BlockType == 0 || info.BigValues > Lines / 2)
                    {
                        return false;
                    }
                }
                else
                {
                    info.BlockType = 0;
                    info.Mixed = false;
                    info.Table0 = bits.Read(5);
                    info.Table1 = bits.Read(5);
                    info.Table2 = bits.Read(5);
                    info.Region0Count = bits.Read(4);
                    info.Region1Count = bits.Read(3);
                    if (info.BigValues > Lines / 2)
                    {
                        return false;
                    }
                }

                info.Preflag = header.IsFirst ? bits.ReadBit() != 0 : info.ScalefacCompress >= 500;
                info.ScalefacScale = bits.ReadBit() != 0;
                info.Count1TableB = bits.ReadBit() != 0;
                if (Mp3Huffman.IsMissing(info.Table0) || Mp3Huffman.IsMissing(info.Table1) ||
                    Mp3Huffman.IsMissing(info.Table2))
                {
                    return false;
                }
            }
        }

        return !bits.IsPastEnd;
    }

    /// <summary>Decodes a frame whose side information was read, from its main data, into <paramref name="samples"/>:
    /// <see cref="Mp3FrameHeader.Samples"/> frames of its channels. Damage within the main data is heard as silence in
    /// its granule, not refused: it is what a decoder of a stream does.</summary>
    public void Decode(Mp3FrameHeader header, ReadOnlySpan<byte> mainData, Span<float> samples)
    {
        var channels = header.Channels;
        var bands = Mp3Tables.BandsOf(header);
        var bits = new Mp3Bits(mainData);
        for (var granule = 0; granule < header.Granules; granule++)
        {
            var damaged = false;
            for (var channel = 0; channel < channels; channel++)
            {
                ref var info = ref _granules[granule * 2 + channel];
                var start = bits.Position;
                if (header.IsFirst)
                {
                    ReadScalefactors(ref bits, info, channel, granule);
                }
                else
                {
                    var isIntensity = header.IsIntensity && channel == 1;
                    // The intensity positions' scalefactors have no pretab.
                    info.Preflag = !isIntensity && info.ScalefacCompress >= 500;
                    ReadLowerScalefactors(ref bits, info, channel, isIntensity);
                }

                var end = start + info.Part23Length;
                damaged |= bits.Position > end || end > bits.Length ||
                    !ReadLines(ref bits, info, bands, end, _quantized[channel], out _nonzero[channel]);
                bits.Position = end;
                Requantize(info, bands, channel);
            }

            if (damaged)
            {
                Array.Clear(_lines[0]);
                Array.Clear(_lines[1]);
            }
            else if (channels == 2 && header.Mode == 1)
            {
                JoinStereo(header, _granules[granule * 2 + 1], bands);
            }

            for (var channel = 0; channel < channels; channel++)
            {
                ref var info = ref _granules[granule * 2 + channel];
                Reorder(info, bands, _lines[channel]);
                AntiAlias(info, bands, _lines[channel]);
                Transform(info, bands, _lines[channel], _overlap[channel]);
                Synthesize(channel, samples[(granule * Lines * channels)..], channels);
            }
        }
    }

    private void ReadScalefactors(ref Mp3Bits bits, in Mp3Granule info, int channel, int granule)
    {
        var slen1 = Mp3Tables.Slen1[info.ScalefacCompress];
        var slen2 = Mp3Tables.Slen2[info.ScalefacCompress];
        var longs = _longScalefactors[channel];
        var shorts = _shortScalefactors[channel];
        if (info.IsShort)
        {
            var band = 0;
            if (info.Mixed)
            {
                for (; band < 8; band++)
                {
                    longs[band] = bits.Read(slen1);
                }

                band = 3;
            }

            for (; band < 12; band++)
            {
                var length = band < 6 ? slen1 : slen2;
                for (var window = 0; window < 3; window++)
                {
                    shorts[band * 3 + window] = bits.Read(length);
                }
            }

            shorts[36] = shorts[37] = shorts[38] = 0;
            return;
        }

        // The four groups of bands: kept from the first granule where its scfsi says.
        ReadGroup(ref bits, longs, 0, 6, slen1, granule == 1 && _scfsi[channel * 4]);
        ReadGroup(ref bits, longs, 6, 11, slen1, granule == 1 && _scfsi[channel * 4 + 1]);
        ReadGroup(ref bits, longs, 11, 16, slen2, granule == 1 && _scfsi[channel * 4 + 2]);
        ReadGroup(ref bits, longs, 16, 21, slen2, granule == 1 && _scfsi[channel * 4 + 3]);
        longs[21] = 0;
    }

    private static void ReadGroup(ref Mp3Bits bits, int[] scalefactors, int from, int to, int length, bool kept)
    {
        if (kept)
        {
            return;
        }

        for (var band = from; band < to; band++)
        {
            scalefactors[band] = bits.Read(length);
        }
    }

    // 13818-3 2.4.3.2: four parts of bands, each of its own length.
    private void ReadLowerScalefactors(ref Mp3Bits bits, in Mp3Granule info, int channel, bool isIntensity)
    {
        int table;
        var compress = info.ScalefacCompress;
        if (!isIntensity)
        {
            if (compress < 400)
            {
                SetSlen((compress >> 4) / 5, (compress >> 4) % 5, (compress & 15) >> 2, compress & 3);
                table = 0;
            }
            else if (compress < 500)
            {
                compress -= 400;
                SetSlen((compress >> 2) / 5, (compress >> 2) % 5, compress & 3, 0);
                table = 1;
            }
            else
            {
                compress -= 500;
                SetSlen(compress / 3, compress % 3, 0, 0);
                table = 2;
            }
        }
        else
        {
            compress >>= 1;
            if (compress < 180)
            {
                SetSlen(compress / 36, compress % 36 / 6, compress % 36 % 6, 0);
                table = 3;
            }
            else if (compress < 244)
            {
                compress -= 180;
                SetSlen((compress & 63) >> 4, (compress & 15) >> 2, compress & 3, 0);
                table = 4;
            }
            else
            {
                compress -= 244;
                SetSlen(compress / 3, compress % 3, 0, 0);
                table = 5;
            }
        }

        var kind = info.IsShort ? info.Mixed ? 2 : 1 : 0;
        var longs = _longScalefactors[channel];
        var shorts = _shortScalefactors[channel];
        Array.Clear(longs);
        Array.Clear(shorts);
        var index = 0;
        for (var part = 0; part < 4; part++)
        {
            var count = Mp3Tables.LowerPartitions[table, kind, part];
            var length = _slen[part];
            for (var one = 0; one < count; one++, index++)
            {
                var value = bits.Read(length);
                // All ones of its length; of no length, 0 is a position.
                var illegal = length == 0 ? -1 : (1 << length) - 1;
                if (kind == 0 || kind == 2 && index < 6)
                {
                    longs[index] = value;
                    _longIllegal[index] = illegal;
                }
                else
                {
                    var at = kind == 2 ? 9 + index - 6 : index;
                    shorts[at] = value;
                    _shortIllegal[at] = illegal;
                }
            }
        }

        void SetSlen(int first, int second, int third, int fourth)
        {
            _slen[0] = first;
            _slen[1] = second;
            _slen[2] = third;
            _slen[3] = fourth;
        }
    }

    // The big values in pairs by three regions' tables, then quadruples; false where the bits run out.
    private static bool ReadLines(ref Mp3Bits bits, in Mp3Granule info, Mp3Bands bands, int end, int[] lines,
        out int nonzero)
    {
        Array.Clear(lines);
        nonzero = 0;
        var bigEnd = info.BigValues * 2;
        int region1;
        int region2;
        if (info.WindowSwitching)
        {
            region1 = info.IsShort && !info.Mixed ? bands.Short[3] * 3 : bands.Long[8];
            region2 = Lines;
        }
        else
        {
            region1 = bands.Long[Math.Min(info.Region0Count + 1, 22)];
            region2 = bands.Long[Math.Min(info.Region0Count + info.Region1Count + 2, 22)];
        }

        var line = 0;
        while (line < bigEnd)
        {
            var region = line < region1 ? 0 : line < region2 ? 1 : 2;
            var table = info.TableOf(region);
            var regionEnd = Math.Min(bigEnd, region == 0 ? region1 : region == 1 ? region2 : Lines);
            if (table == 0)
            {
                line = Math.Max(line, regionEnd);
                continue;
            }

            var linbits = Mp3Huffman.LinbitsOf(table);
            for (; line < regionEnd; line += 2)
            {
                var pair = Mp3Huffman.DecodePair(ref bits, table);
                if (pair < 0 || bits.Position > end)
                {
                    return false;
                }

                lines[line] = ValueOf(ref bits, pair >> 4, linbits);
                lines[line + 1] = ValueOf(ref bits, pair & 15, linbits);
                if (lines[line] != 0 || lines[line + 1] != 0)
                {
                    nonzero = line + 2;
                }
            }
        }

        // Quadruples until the channel's bits end; one that ends past them was not written.
        while (line + 4 <= Lines && bits.Position < end)
        {
            var at = bits.Position;
            var quadruple = info.Count1TableB ? 15 - bits.Read(4) : Mp3Huffman.DecodeQuadruple(ref bits);
            if (quadruple < 0)
            {
                return false;
            }

            for (var shift = 3; shift >= 0; shift--)
            {
                lines[line + 3 - shift] = (quadruple >> shift & 1) != 0 ? (bits.ReadBit() != 0 ? -1 : 1) : 0;
            }

            if (bits.Position > end)
            {
                lines.AsSpan(line, 4).Clear();
                bits.Position = at;
                break;
            }

            if (quadruple != 0)
            {
                nonzero = line + 4;
            }

            line += 4;
        }

        return true;
    }

    private static int ValueOf(ref Mp3Bits bits, int value, int linbits)
    {
        if (value == 15 && linbits > 0)
        {
            value += bits.Read(linbits);
        }

        return value != 0 && bits.ReadBit() != 0 ? -value : value;
    }

    // 2.4.3.4.7.1: a line's value to the power of 4/3, times its band's and its granule's gains.
    private void Requantize(in Mp3Granule info, Mp3Bands bands, int channel)
    {
        var quantized = _quantized[channel];
        var lines = _lines[channel];
        var longs = _longScalefactors[channel];
        var shorts = _shortScalefactors[channel];
        var multiplier = info.ScalefacScale ? 1f : 0.5f;
        var global = 0.25f * (info.GlobalGain - 210);
        var line = 0;
        if (!info.IsShort || info.Mixed)
        {
            var longEnd = info.IsShort ? MixedLines(bands) : Lines;
            for (var band = 0; band < 22 && line < longEnd; band++)
            {
                var gain = MathF.Pow(2, global - multiplier * (longs[band] + (info.Preflag ? Mp3Tables.Pretab[band] : 0)));
                for (var end = Math.Min(bands.Long[band + 1], longEnd); line < end; line++)
                {
                    lines[line] = Mp3Tables.Power43(quantized[line]) * gain;
                }
            }
        }

        if (info.IsShort)
        {
            for (var band = info.Mixed ? 3 : 0; band < 13; band++)
            {
                var width = bands.Short[band + 1] - bands.Short[band];
                for (var window = 0; window < 3; window++)
                {
                    var gain = MathF.Pow(2, global - 2 * info.SubblockGainOf(window) - multiplier * shorts[band * 3 + window]);
                    for (var end = line + width; line < end; line++)
                    {
                        lines[line] = Mp3Tables.Power43(quantized[line]) * gain;
                    }
                }
            }
        }
    }

    // Middle and side, and intensity: 2.4.3.4.9, 13818-3 2.4.3.2. Lines given by intensity are marked; the rest are
    // middle and side where the frame says.
    private void JoinStereo(Mp3FrameHeader header, in Mp3Granule info, Mp3Bands bands)
    {
        var left = _lines[0];
        var right = _lines[1];
        Array.Clear(_isIntensity);
        if (header.IsIntensity)
        {
            IntensityBands(header, info, bands, left, right);
        }

        if (header.IsMidSide)
        {
            const float half = 0.70710677f;
            for (var line = 0; line < Lines; line++)
            {
                if (!_isIntensity[line])
                {
                    var middle = left[line];
                    var side = right[line];
                    left[line] = (middle + side) * half;
                    right[line] = (middle - side) * half;
                }
            }
        }
    }

    // From where the right channel is silent, its bands are the left's by their intensity positions.
    private void IntensityBands(Mp3FrameHeader header, in Mp3Granule info, Mp3Bands bands, float[] left, float[] right)
    {
        var longs = _longScalefactors[1];
        var shorts = _shortScalefactors[1];
        if (!info.IsShort)
        {
            var band = 0;
            while (band < 22 && bands.Long[band] < _nonzero[1])
            {
                band++;
            }

            for (; band < 22; band++)
            {
                // The top band has no scalefactor: the one below's position is its.
                var source = Math.Min(band, 20);
                var illegal = header.IsFirst ? 7 : _longIllegal[source];
                IntensityOf(header, info, longs[source], illegal, left, right, bands.Long[band], bands.Long[band + 1]);
            }

            return;
        }

        // Short: from above the last band that is not silent - of each window at the lower rates' short blocks
        // (13818-3 2.4.3.2), of any window in MPEG-1 and mixed blocks. A mixed block's long part is not given by
        // intensity: its right channel is not silent below 36 lines in any stream a coder writes for it.
        var lowest = info.Mixed ? 3 : 0;
        var common = 0;
        for (var window = 0; window < 3; window++)
        {
            common = Math.Max(common, FirstSilentBand(right, bands, window, lowest));
        }

        for (var window = 0; window < 3; window++)
        {
            var first = header.IsFirst || info.Mixed ? common : FirstSilentBand(right, bands, window, lowest);
            for (var at = first; at < 13; at++)
            {
                // As the long blocks' top band.
                var source = Math.Min(at, 11) * 3 + window;
                var position = shorts[source];
                var illegal = header.IsFirst ? 7 : _shortIllegal[source];
                var width = bands.Short[at + 1] - bands.Short[at];
                var from = bands.Short[at] * 3 + window * width;
                IntensityOf(header, info, position, illegal, left, right, from, from + width);
            }
        }
    }

    // The lowest band of a window from which it is silent to the top; 13 where the top band is not.
    private static int FirstSilentBand(float[] lines, Mp3Bands bands, int window, int lowest)
    {
        var band = 13;
        for (var at = 12; at >= lowest && IsSilent(lines, bands, at, window); at--)
        {
            band = at;
        }

        return band;
    }

    private static bool IsSilent(float[] lines, Mp3Bands bands, int band, int window)
    {
        var width = bands.Short[band + 1] - bands.Short[band];
        var from = bands.Short[band] * 3 + window * width;
        for (var line = from; line < from + width; line++)
        {
            if (lines[line] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private void IntensityOf(Mp3FrameHeader header, in Mp3Granule info, int position, int illegal, float[] left,
        float[] right, int from, int to)
    {
        // As the bands below: middle and side where the frame says. MPEG-1's positions past 6 are none: of a coder's
        // only 7, of damage any up to 15, whose tangent would be no number.
        if (header.IsFirst ? position >= illegal : position == illegal)
        {
            return;
        }

        float leftGain;
        float rightGain;
        if (header.IsFirst)
        {
            var ratio = Mp3Tables.IntensityRatio(position);
            leftGain = ratio / (1 + ratio);
            rightGain = 1 / (1 + ratio);
            if (position == 6)
            {
                leftGain = 1;
                rightGain = 0;
            }
        }
        else
        {
            var scale = (info.ScalefacCompress & 1) != 0 ? 0.70710677f : 0.84089642f;
            leftGain = (position & 1) != 0 ? MathF.Pow(scale, (position + 1) >> 1) : 1;
            rightGain = (position & 1) != 0 ? 1 : MathF.Pow(scale, position >> 1);
        }

        for (var line = from; line < to; line++)
        {
            var value = left[line];
            left[line] = value * leftGain;
            right[line] = value * rightGain;
            _isIntensity[line] = true;
        }
    }

    // The short blocks' lines, written band by band and window by window, made window by window within each
    // subband: what the transform reads.
    private void Reorder(in Mp3Granule info, Mp3Bands bands, float[] lines)
    {
        if (!info.IsShort)
        {
            return;
        }

        var first = info.Mixed ? 3 : 0;
        var start = info.Mixed ? MixedLines(bands) : 0;
        for (var band = first; band < 13; band++)
        {
            var bandStart = bands.Short[band];
            var width = bands.Short[band + 1] - bandStart;
            for (var window = 0; window < 3; window++)
            {
                for (var at = 0; at < width; at++)
                {
                    var frequency = bandStart + at;
                    _reordered[frequency / 6 * SubbandLines + window * 6 + frequency % 6] =
                        lines[bandStart * 3 + window * width + at];
                }
            }
        }

        _reordered.AsSpan(start).CopyTo(lines.AsSpan(start));
    }

    // Where a mixed block's short part begins, its long part's lines before: 36, but for MPEG 2.5 at 8 kHz 72.
    private static int MixedLines(Mp3Bands bands) => bands.Short[3] * 3;

    // 2.4.3.4.10: between the subbands of long blocks.
    private static void AntiAlias(in Mp3Granule info, Mp3Bands bands, float[] lines)
    {
        var subbands = info.IsShort ? info.Mixed ? MixedLines(bands) / SubbandLines : 0 : Subbands;
        for (var subband = 1; subband < subbands; subband++)
        {
            for (var at = 0; at < 8; at++)
            {
                var lower = subband * SubbandLines - 1 - at;
                var upper = subband * SubbandLines + at;
                var down = lines[lower];
                var up = lines[upper];
                lines[lower] = down * Mp3Tables.AliasCs[at] - up * Mp3Tables.AliasCa[at];
                lines[upper] = up * Mp3Tables.AliasCs[at] + down * Mp3Tables.AliasCa[at];
            }
        }
    }

    // 2.4.3.4.10.2: each subband's lines to 36 samples, windowed by its block's type, half added to the last
    // granule's half; every other sample of the odd subbands turned over.
    private void Transform(in Mp3Granule info, Mp3Bands bands, float[] lines, float[] overlap)
    {
        var longSubbands = MixedLines(bands) / SubbandLines;
        for (var subband = 0; subband < Subbands; subband++)
        {
            // A mixed block's long subbands, and those of the start and stop blocks around it, are normal ones.
            var blockType = info.WindowSwitching && info.Mixed && subband < longSubbands ? 0 : info.BlockType;
            var source = lines.AsSpan(subband * SubbandLines, SubbandLines);
            if (blockType == 2)
            {
                Mp3Tables.TransformShort(source, _transformed);
            }
            else
            {
                Mp3Tables.TransformLong(source, _transformed, blockType);
            }

            var previous = overlap.AsSpan(subband * SubbandLines, SubbandLines);
            for (var time = 0; time < SubbandLines; time++)
            {
                var value = _transformed[time] + previous[time];
                previous[time] = _transformed[time + SubbandLines];
                _hybrid[time * Subbands + subband] = (subband & time & 1) != 0 ? -value : value;
            }
        }
    }

    // 2.4.3.4.10.3, the polyphase synthesis filter: each time's 32 subbands to 32 samples. Its 1024 values are kept
    // twice over, so that those it reads are one run, however the newest lie.
    private void Synthesize(int channel, Span<float> samples, int channels)
    {
        var history = _history[channel];
        var window = Mp3Tables.SynthesisWindow;
        Span<float> sums = stackalloc float[32];
        for (var time = 0; time < SubbandLines; time++)
        {
            var at = _historyAt[channel] = (_historyAt[channel] - 64) & 1023;
            var values = history.AsSpan(at, 64);
            Mp3Tables.Matrix(_hybrid.AsSpan(time * Subbands, Subbands), values);
            values.CopyTo(history.AsSpan(at + 1024, 64));
            sums.Clear();
            for (var part = 0; part < 8; part++)
            {
                AddProducts(window.AsSpan(part * 64, 32), history.AsSpan(at + part * 128, 32), sums);
                AddProducts(window.AsSpan(part * 64 + 32, 32), history.AsSpan(at + part * 128 + 96, 32), sums);
            }

            for (var sample = 0; sample < 32; sample++)
            {
                samples[(time * 32 + sample) * channels + channel] = sums[sample];
            }
        }
    }

    private static void AddProducts(ReadOnlySpan<float> first, ReadOnlySpan<float> second, Span<float> sums)
    {
        var firsts = MemoryMarshal.Cast<float, Vector<float>>(first);
        var seconds = MemoryMarshal.Cast<float, Vector<float>>(second);
        var totals = MemoryMarshal.Cast<float, Vector<float>>(sums);
        for (var index = 0; index < totals.Length; index++)
        {
            totals[index] += firsts[index] * seconds[index];
        }
    }
}
