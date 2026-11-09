using Adamantium.Imaging.Mq;

namespace Adamantium.Imaging.Jbig2;

/// <summary>One of the arithmetic integer decoders of JBIG2 - IADH, IADW, IAEX and the rest (ITU-T T.88, A.2) -
/// with its own 512 contexts.</summary>
internal sealed class Jbig2IntegerDecoder
{
    private readonly byte[] _contexts = new byte[512];

    /// <summary>The next integer, or null for out-of-band.</summary>
    public int? Decode(MqDecoder decoder)
    {
        var previous = 1;
        var sign = Bit(decoder, ref previous);
        int bits, offset;
        if (Bit(decoder, ref previous) == 0)
        {
            (bits, offset) = (2, 0);
        }
        else if (Bit(decoder, ref previous) == 0)
        {
            (bits, offset) = (4, 4);
        }
        else if (Bit(decoder, ref previous) == 0)
        {
            (bits, offset) = (6, 20);
        }
        else if (Bit(decoder, ref previous) == 0)
        {
            (bits, offset) = (8, 84);
        }
        else if (Bit(decoder, ref previous) == 0)
        {
            (bits, offset) = (12, 340);
        }
        else
        {
            (bits, offset) = (32, 4436);
        }

        long value = 0;
        for (var i = 0; i < bits; i++)
        {
            value = (value << 1) | (uint)Bit(decoder, ref previous);
        }

        value += offset;
        if (sign == 1 && value == 0)
        {
            return null;
        }

        value = sign == 1 ? -value : value;
        return value is >= int.MinValue and <= int.MaxValue ? (int)value : null;
    }

    private int Bit(MqDecoder decoder, ref int previous)
    {
        var bit = decoder.Decode(_contexts, previous);
        previous = previous < 256 ? (previous << 1) | bit : (((previous << 1) | bit) & 511) | 256;
        return bit;
    }
}
