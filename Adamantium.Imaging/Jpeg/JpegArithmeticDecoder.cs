using System;
using System.Runtime.CompilerServices;

namespace Adamantium.Imaging.Jpeg;

/// <summary>The adaptive binary arithmetic decoder of ITU-T T.81, Annex D: one decision at a time, each by its own
/// context - a byte holding the context's index in Table D.3 in its low seven bits and its more probable symbol in the
/// high one, starting at 0. A marker ends the entropy-coded segment: past it the decoder reads zeros.</summary>
internal ref struct JpegArithmeticDecoder
{
    private static ReadOnlySpan<ushort> Qe =>
    [
        0x5A1D, 0x2586, 0x1114, 0x080B, 0x03D8, 0x01DA, 0x00E5, 0x006F, 0x0036, 0x001A, 0x000D, 0x0006, 0x0003, 0x0001,
        0x5A7F, 0x3F25, 0x2CF2, 0x207C, 0x17B9, 0x1182, 0x0CEF, 0x09A1, 0x072F, 0x055C, 0x0406, 0x0303, 0x0240, 0x01B1,
        0x0144, 0x00F5, 0x00B7, 0x008A, 0x0068, 0x004E, 0x003B, 0x002C, 0x5AE1, 0x484C, 0x3A0D, 0x2EF1, 0x261F, 0x1F33,
        0x19A8, 0x1518, 0x1177, 0x0E74, 0x0BFB, 0x09F8, 0x0861, 0x0706, 0x05CD, 0x04DE, 0x040F, 0x0363, 0x02D4, 0x025C,
        0x01F8, 0x01A4, 0x0160, 0x0125, 0x00F6, 0x00CB, 0x00AB, 0x008F, 0x5B12, 0x4D04, 0x412C, 0x37D8, 0x2FE8, 0x293C,
        0x2379, 0x1EDF, 0x1AA9, 0x174E, 0x1424, 0x119C, 0x0F6B, 0x0D51, 0x0BB6, 0x0A40, 0x5832, 0x4D1C, 0x438E, 0x3BDD,
        0x34EE, 0x2EAE, 0x299A, 0x2516, 0x5570, 0x4CA9, 0x44D9, 0x3E22, 0x3824, 0x32B4, 0x2E17, 0x56A8, 0x4F46, 0x47E5,
        0x41CF, 0x3C3D, 0x375E, 0x5231, 0x4C0F, 0x4639, 0x415E, 0x5627, 0x50E7, 0x4B85, 0x5597, 0x504F, 0x5A10, 0x5522,
        0x59EB
    ];

    private static ReadOnlySpan<byte> NextLps =>
    [
        1, 14, 16, 18, 20, 23, 25, 28, 30, 33, 35, 9, 10, 12, 15, 36, 38, 39, 40, 42, 43, 45, 46, 48, 49, 51, 52, 54, 56,
        57, 59, 60, 62, 63, 32, 33, 37, 64, 65, 67, 68, 69, 70, 72, 73, 74, 75, 77, 78, 79, 48, 50, 50, 51, 52, 53, 54,
        55, 56, 57, 58, 59, 61, 61, 65, 80, 81, 82, 83, 84, 86, 87, 87, 72, 72, 74, 74, 75, 77, 77, 80, 88, 89, 90, 91,
        92, 93, 86, 88, 95, 96, 97, 99, 99, 93, 95, 101, 102, 103, 104, 99, 105, 106, 107, 103, 105, 108, 109, 110, 111,
        110, 112, 112
    ];

    private static ReadOnlySpan<byte> NextMps =>
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 13, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31,
        32, 33, 34, 35, 9, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59,
        60, 61, 62, 63, 32, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 48, 81, 82, 83, 84, 85, 86, 87,
        71, 89, 90, 91, 92, 93, 94, 86, 96, 97, 98, 99, 100, 93, 102, 103, 104, 99, 106, 107, 103, 109, 107, 111, 109,
        111
    ];

    private static bool Switches(int index) => index is 0 or 14 or 36 or 64 or 80 or 88 or 95 or 105 or 110 or 112;

    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private uint _code;
    private int _interval;
    private int _bits;

    /// <summary>A decoder of the entropy-coded segment starting at <paramref name="position"/> (Initdec, D.2.7).</summary>
    public JpegArithmeticDecoder(ReadOnlySpan<byte> data, int position)
    {
        _data = data;
        _position = position;
        Start();
    }

    /// <summary>Where the next unread byte is: at the marker that ended the segment, once one did.</summary>
    public readonly int Position => _position;

    /// <summary>The decision coded by <paramref name="context"/>, whose estimate it then updates (D.2.4, D.2.5).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(ref byte context)
    {
        var index = context & 0x7F;
        var more = context >> 7;
        int qe = Qe[index];
        _interval -= qe;
        int decision;
        if ((int)(_code >> 16) < _interval)
        {
            if (_interval >= 0x8000)
            {
                return more;
            }

            if (_interval < qe)
            {
                decision = 1 - more;
                context = AfterLess(index, more);
            }
            else
            {
                decision = more;
                context = AfterMore(index, more);
            }
        }
        else
        {
            _code -= (uint)_interval << 16;
            if (_interval < qe)
            {
                decision = more;
                context = AfterMore(index, more);
            }
            else
            {
                decision = 1 - more;
                context = AfterLess(index, more);
            }

            _interval = qe;
        }

        Renormalize();
        return decision;
    }

    /// <summary>A decision coded with the fixed estimate of one half (Qe = X'5A1D', MPS = 0) - the signs of AC
    /// coefficients and the bits of later successive approximation scans.</summary>
    public int DecodeHalf()
    {
        byte context = 0;
        return Decode(ref context);
    }

    /// <summary>Passes the restart marker that should come next and starts decoding again after it (F.2.4.4); a marker
    /// missing - broken data - starts at the next marker, or reads zeros.</summary>
    public void Restart()
    {
        while (_position + 1 < _data.Length)
        {
            if (_data[_position] != 0xFF || _data[_position + 1] is 0x00 or 0xFF)
            {
                _position++;
                continue;
            }

            if (_data[_position + 1] is >= 0xD0 and <= 0xD7)
            {
                _position += 2;
            }

            break;
        }

        Start();
    }

    private static byte AfterMore(int index, int more) => (byte)(NextMps[index] | (more << 7));

    private static byte AfterLess(int index, int more) =>
        (byte)(NextLps[index] | ((Switches(index) ? 1 - more : more) << 7));

    private void Start()
    {
        _interval = 0x10000;
        _code = 0;
        ReadByte();
        _code <<= 8;
        ReadByte();
        _code <<= 8;
        _bits = 0;
    }

    private void Renormalize()
    {
        do
        {
            if (_bits == 0)
            {
                ReadByte();
                _bits = 8;
            }

            _interval <<= 1;
            _code <<= 1;
            _bits--;
        }
        while (_interval < 0x8000);
    }

    private void ReadByte()
    {
        if (_position >= _data.Length)
        {
            return;
        }

        var value = _data[_position];
        if (value != 0xFF)
        {
            _code += (uint)value << 8;
            _position++;
            return;
        }

        if (_position + 1 < _data.Length && _data[_position + 1] == 0)
        {
            _code |= 0xFF00;
            _position += 2;
        }
    }
}
