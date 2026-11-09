namespace Adamantium.Imaging.Mq;

/// <summary>The MQ arithmetic decoder JBIG2 (ITU-T T.88, annex E) and JPEG 2000 (ITU-T T.800, annex C) share. Past
/// the end of its bytes it reads 0xFF, as both standards say.</summary>
internal sealed class MqDecoder
{
    private static readonly ushort[] Qe =
    [
        0x5601, 0x3401, 0x1801, 0x0AC1, 0x0521, 0x0221, 0x5601, 0x5401, 0x4801, 0x3801, 0x3001, 0x2401, 0x1C01,
        0x1601, 0x5601, 0x5401, 0x5101, 0x4801, 0x3801, 0x3401, 0x3001, 0x2801, 0x2401, 0x2201, 0x1C01, 0x1801,
        0x1601, 0x1401, 0x1201, 0x1101, 0x0AC1, 0x09C1, 0x08A1, 0x0521, 0x0441, 0x02A1, 0x0221, 0x0141, 0x0111,
        0x0085, 0x0049, 0x0025, 0x0015, 0x0009, 0x0005, 0x0001, 0x5601
    ];

    private static readonly byte[] NextMps =
    [
        1, 2, 3, 4, 5, 38, 7, 8, 9, 10, 11, 12, 13, 29, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29,
        30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 45, 46
    ];

    private static readonly byte[] NextLps =
    [
        1, 6, 9, 12, 29, 33, 6, 14, 14, 14, 17, 18, 20, 21, 14, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 23, 24, 25,
        26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 46
    ];

    private static readonly bool[] Switches =
    [
        true, false, false, false, false, false, true, false, false, false, false, false, false, false, true, false,
        false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,
        false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,
        false
    ];

    private readonly byte[] _data;
    private readonly int _end;
    private int _position;
    private uint _c;
    private uint _a;
    private int _ct;
    private long _beyond;

    /// <summary>Starts decoding <paramref name="length"/> bytes of <paramref name="data"/> at
    /// <paramref name="start"/>.</summary>
    public MqDecoder(byte[] data, int start, int length)
    {
        _data = data;
        _position = start;
        _end = start + length;
        _c = (uint)ByteAt(_position) << 16;
        ByteIn();
        _c <<= 7;
        _ct -= 7;
        _a = 0x8000;
    }

    /// <summary>How many bytes past the end the decoder has made up - a bound on hostile data.</summary>
    public long BytesBeyondEnd => _beyond;

    /// <summary>Decodes one decision in the context stored at <paramref name="index"/> of
    /// <paramref name="contexts"/>: its state times 2 plus its more probable symbol.</summary>
    public int Decode(byte[] contexts, int index)
    {
        var context = contexts[index];
        var state = context >> 1;
        var mps = context & 1;
        uint qe = Qe[state];
        _a -= qe;
        int decision;
        if ((_c >> 16) >= qe)
        {
            _c -= qe << 16;
            if ((_a & 0x8000) != 0)
            {
                return mps;
            }

            if (_a < qe)
            {
                decision = 1 - mps;
                contexts[index] = (byte)((NextLps[state] << 1) | (Switches[state] ? 1 - mps : mps));
            }
            else
            {
                decision = mps;
                contexts[index] = (byte)((NextMps[state] << 1) | mps);
            }
        }
        else
        {
            if (_a < qe)
            {
                decision = mps;
                contexts[index] = (byte)((NextMps[state] << 1) | mps);
            }
            else
            {
                decision = 1 - mps;
                contexts[index] = (byte)((NextLps[state] << 1) | (Switches[state] ? 1 - mps : mps));
            }

            _a = qe;
        }

        do
        {
            if (_ct == 0)
            {
                ByteIn();
            }

            _a <<= 1;
            _c <<= 1;
            _ct--;
        }
        while ((_a & 0x8000) == 0);

        return decision;
    }

    /// <summary>Sets a context to the given state and more probable symbol.</summary>
    public static void Reset(byte[] contexts, int index, int state, int mps) =>
        contexts[index] = (byte)((state << 1) | mps);

    private int ByteAt(int position)
    {
        if (position < _end && position < _data.Length)
        {
            return _data[position];
        }

        return 0xFF;
    }

    private void ByteIn()
    {
        if (ByteAt(_position) == 0xFF)
        {
            if (ByteAt(_position + 1) > 0x8F)
            {
                _c += 0xFF00;
                _ct = 8;
                if (_position + 1 >= _end)
                {
                    _beyond++;
                }
            }
            else
            {
                _position++;
                _c += (uint)ByteAt(_position) << 9;
                _ct = 7;
            }
        }
        else
        {
            _position++;
            _c += (uint)ByteAt(_position) << 8;
            _ct = 8;
        }
    }
}
