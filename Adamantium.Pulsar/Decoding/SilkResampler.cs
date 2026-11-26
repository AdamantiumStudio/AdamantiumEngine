namespace Adamantium.Pulsar.Decoding;

/// <summary>SILK's resampler from its rate to 48 kHz (RFC 6716, 4.2.9): up by two with an all-pass IIR, then a
/// 12-phase FIR to the rate. Not normative but for its delay, which this keeps; the reference's, so that what it gives
/// is the reference decoder's.</summary>
internal sealed class SilkResampler
{
    private const int FirOrder = 8;
    private const int OutputRate = 48;
    private const int BatchMilliseconds = 10;

    private readonly int[] _iir = new int[6];
    private readonly short[] _fir = new short[FirOrder];
    private readonly short[] _delay = new short[48];
    private readonly short[] _buffer = new short[2 * BatchMilliseconds * 16 + FirOrder];
    private int _inputRate;
    private int _delayLength;
    private int _batch;
    private int _step;

    /// <summary>From <paramref name="rate"/>, 8000, 12000 or 16000, all it had forgotten.</summary>
    public void Reset(int rate)
    {
        Array.Clear(_iir);
        Array.Clear(_fir);
        Array.Clear(_delay);
        _inputRate = rate / 1000;
        // delay_matrix_dec, for 48 kHz out.
        _delayLength = _inputRate switch { 8 => 0, 12 => 4, _ => 7 };
        _batch = _inputRate * BatchMilliseconds;
        _step = ((rate << 15) / (OutputRate * 1000)) << 2;
        while (SilkMath.MulWW(_step, OutputRate * 1000) < rate << 1)
        {
            _step++;
        }
    }

    public void CopyFrom(SilkResampler other)
    {
        other._iir.CopyTo(_iir, 0);
        other._fir.CopyTo(_fir, 0);
        other._delay.CopyTo(_delay, 0);
        _inputRate = other._inputRate;
        _delayLength = other._delayLength;
        _batch = other._batch;
        _step = other._step;
    }

    /// <summary>Of <paramref name="input"/>, the samples at 48 kHz into <paramref name="output"/>.</summary>
    public void Resample(Span<short> output, ReadOnlySpan<short> input)
    {
        var first = _inputRate - _delayLength;
        input[..first].CopyTo(_delay.AsSpan(_delayLength));
        var written = Interpolate(output, _delay.AsSpan(0, _inputRate));
        // The rest but the delay's samples, which wait for the next call.
        Interpolate(output[written..], input.Slice(first, input.Length - _inputRate));
        input[^_delayLength..].CopyTo(_delay);
    }

    // silk_resampler_private_IIR_FIR.
    private int Interpolate(Span<short> output, ReadOnlySpan<short> input)
    {
        var buffer = _buffer.AsSpan();
        _fir.CopyTo(buffer);
        var written = 0;
        var count = 0;
        var left = input.Length;
        var at = 0;
        while (true)
        {
            count = Math.Min(left, _batch);
            UpByTwo(buffer[FirOrder..], input.Slice(at, count));
            var end = count << 17;
            for (var index = 0; index < end; index += _step)
            {
                var phase = SilkMath.MulWB(index & 0xFFFF, 12);
                var samples = buffer[(index >> 16)..];
                var high = SilkTables.Interpolation12.Slice(4 * phase, 4);
                var low = SilkTables.Interpolation12.Slice(4 * (11 - phase), 4);
                var sum = samples[0] * high[0] + samples[1] * high[1] + samples[2] * high[2] + samples[3] * high[3] +
                    samples[4] * low[3] + samples[5] * low[2] + samples[6] * low[1] + samples[7] * low[0];
                output[written++] = SilkMath.Saturate16(SilkMath.ShiftRound(sum, 15));
            }

            at += count;
            left -= count;
            if (left > 0)
            {
                buffer.Slice(count << 1, FirOrder).CopyTo(buffer);
            }
            else
            {
                break;
            }
        }

        buffer.Slice(count << 1, FirOrder).CopyTo(_fir);
        return written;
    }

    // silk_resampler_private_up2_HQ: two all-pass sections of three, one for even samples, one for odd.
    private void UpByTwo(Span<short> output, ReadOnlySpan<short> input)
    {
        var first = SilkTables.Up2First;
        var second = SilkTables.Up2Second;
        var state = _iir;
        for (var at = 0; at < input.Length; at++)
        {
            var value = input[at] << 10;
            output[2 * at] = Section(state, 0, value, first);
            output[2 * at + 1] = Section(state, 3, value, second);
        }
    }

    private static short Section(int[] state, int at, int value, ReadOnlySpan<short> coefficients)
    {
        var y = value - state[at];
        var x = SilkMath.MulWB(y, coefficients[0]);
        var out1 = state[at] + x;
        state[at] = value + x;
        y = out1 - state[at + 1];
        x = SilkMath.MulWB(y, coefficients[1]);
        var out2 = state[at + 1] + x;
        state[at + 1] = out1 + x;
        y = out2 - state[at + 2];
        x = SilkMath.MulAddWB(y, y, coefficients[2]);
        out1 = state[at + 2] + x;
        state[at + 2] = out2 + x;
        return SilkMath.Saturate16(SilkMath.ShiftRound(out1, 10));
    }
}
