namespace Adamantium.Pulsar.Decoding;

/// <summary>The four bytes a Layer III frame begins by: ISO/IEC 11172-3 2.4.2.3, with the lower rates of 13818-3 and
/// those of MPEG 2.5.</summary>
internal readonly struct Mp3FrameHeader
{
    public const int Length = 4;

    // Kilobits a second, by bitrate_index; 0 is free format, which is not read.
    private static readonly int[] FirstBitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] LowerBitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    private static readonly int[] FirstRates = [44100, 48000, 32000];

    private Mp3FrameHeader(uint bits)
    {
        Bits = bits;
    }

    public uint Bits { get; }

    /// <summary>MPEG-1; otherwise the lower rates' (MPEG-2 or 2.5), with one granule a frame.</summary>
    public bool IsFirst => (Bits >> 19 & 3) == 3;

    public bool IsLowest => (Bits >> 19 & 3) == 0;

    public bool HasChecksum => (Bits >> 16 & 1) == 0;

    public int BitrateIndex => (int)(Bits >> 12 & 15);

    public int RateIndex => (int)(Bits >> 10 & 3);

    public bool IsPadded => (Bits >> 9 & 1) != 0;

    /// <summary>0 stereo, 1 joint stereo, 2 dual channel, 3 one channel.</summary>
    public int Mode => (int)(Bits >> 6 & 3);

    public int ModeExtension => (int)(Bits >> 4 & 3);

    public int Channels => Mode == 3 ? 1 : 2;

    public bool IsMidSide => Mode == 1 && (ModeExtension & 2) != 0;

    public bool IsIntensity => Mode == 1 && (ModeExtension & 1) != 0;

    public int Granules => IsFirst ? 2 : 1;

    public int Samples => Granules * 576;

    public int SampleRate => FirstRates[RateIndex] >> (IsFirst ? 0 : IsLowest ? 2 : 1);

    public int Bitrate => (IsFirst ? FirstBitrates : LowerBitrates)[BitrateIndex] * 1000;

    /// <summary>The whole frame's, its header with it.</summary>
    public int FrameLength => Samples / 8 * Bitrate / SampleRate + (IsPadded ? 1 : 0);

    public int SideInfoLength => IsFirst ? (Channels == 1 ? 17 : 32) : (Channels == 1 ? 9 : 17);

    /// <summary>Where the side information begins, after the header and its checksum.</summary>
    public int SideInfoAt => Length + (HasChecksum ? 2 : 0);

    /// <summary>Whether these are the bits of a Layer III frame header that can be read: not free format, no reserved
    /// value.</summary>
    public static bool TryRead(ReadOnlySpan<byte> bytes, out Mp3FrameHeader header)
    {
        header = default;
        if (bytes.Length < Length)
        {
            return false;
        }

        var bits = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        // Sync, a version that is not reserved, Layer III, a bitrate that is not free or bad, a rate, an emphasis.
        if ((bits & 0xFFE00000) != 0xFFE00000 || (bits >> 19 & 3) == 1 || (bits >> 17 & 3) != 1 ||
            (bits >> 12 & 15) is 0 or 15 || (bits >> 10 & 3) == 3 || (bits & 3) == 2)
        {
            return false;
        }

        header = new Mp3FrameHeader(bits);
        return true;
    }

    /// <summary>Whether a frame of <paramref name="other"/> belongs to the same stream: its version, layer and rate the
    /// same. The mode may change, one channel to two.</summary>
    public bool IsLike(Mp3FrameHeader other) => (Bits & 0xFFFE0C00) == (other.Bits & 0xFFFE0C00);
}
