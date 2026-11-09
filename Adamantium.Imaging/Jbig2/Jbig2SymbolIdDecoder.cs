using Adamantium.Imaging.Mq;

namespace Adamantium.Imaging.Jbig2;

/// <summary>The arithmetic decoder of symbol numbers, IAID (ITU-T T.88, A.3): a fixed number of bits, each in the
/// context of the bits before it.</summary>
internal sealed class Jbig2SymbolIdDecoder
{
    private readonly int _length;
    private readonly byte[] _contexts;

    public Jbig2SymbolIdDecoder(int length)
    {
        if (length > Jbig2Limits.MaxSymbolCodeLength)
        {
            throw new System.IO.InvalidDataException("A JBIG2 region refers to too many symbols.");
        }

        _length = length;
        _contexts = new byte[1 << (length + 1)];
    }

    public int Decode(MqDecoder decoder)
    {
        var previous = 1;
        for (var i = 0; i < _length; i++)
        {
            previous = (previous << 1) | decoder.Decode(_contexts, previous);
        }

        return previous - (1 << _length);
    }
}
