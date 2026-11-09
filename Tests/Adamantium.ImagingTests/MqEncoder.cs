using System.Collections.Generic;

namespace Adamantium.ImagingTests;

/// <summary>The MQ arithmetic encoder of ITU-T T.88, E.2, for writing test data the decoder reads back.</summary>
internal sealed class MqEncoder
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

    private readonly List<byte> _bytes = [];
    private uint _a = 0x8000;
    private uint _c;
    private int _ct = 12;
    private int _b = -1;

    public void Encode(byte[] contexts, int index, int decision)
    {
        var state = contexts[index] >> 1;
        var mps = contexts[index] & 1;
        uint qe = Qe[state];
        _a -= qe;
        if (decision == mps)
        {
            if ((_a & 0x8000) != 0)
            {
                _c += qe;
                return;
            }

            if (_a < qe)
            {
                _a = qe;
            }
            else
            {
                _c += qe;
            }

            contexts[index] = (byte)((NextMps[state] << 1) | mps);
        }
        else
        {
            if (_a < qe)
            {
                _c += qe;
            }
            else
            {
                _a = qe;
            }

            var switches = state is 0 or 6 or 14;
            contexts[index] = (byte)((NextLps[state] << 1) | (switches ? 1 - mps : mps));
        }

        do
        {
            _a <<= 1;
            _c <<= 1;
            _ct--;
            if (_ct == 0)
            {
                ByteOut();
            }
        }
        while ((_a & 0x8000) == 0);
    }

    /// <summary>Ends the code with its marker and returns the bytes.</summary>
    public byte[] Finish()
    {
        var temp = _c + _a;
        _c |= 0xFFFF;
        if (_c >= temp)
        {
            _c -= 0x8000;
        }

        _c <<= _ct;
        ByteOut();
        _c <<= _ct;
        ByteOut();
        if (_b != 0xFF)
        {
            Emit(0xFF);
        }

        Emit(0xAC);
        Flush();
        return _bytes.ToArray();
    }

    private void ByteOut()
    {
        if (_b == 0xFF)
        {
            Emit((int)(_c >> 20));
            _c &= 0xFFFFF;
            _ct = 7;
        }
        else if (_c < 0x8000000)
        {
            Emit((int)(_c >> 19));
            _c &= 0x7FFFF;
            _ct = 8;
        }
        else
        {
            _b++;
            if (_b == 0xFF)
            {
                _c &= 0x7FFFFFF;
                Emit((int)(_c >> 20));
                _c &= 0xFFFFF;
                _ct = 7;
            }
            else
            {
                Emit((int)(_c >> 19));
                _c &= 0x7FFFF;
                _ct = 8;
            }
        }
    }

    private void Emit(int value)
    {
        Flush();
        _b = value & 0xFF;
    }

    private void Flush()
    {
        if (_b >= 0)
        {
            _bytes.Add((byte)_b);
            _b = -1;
        }
    }
}
