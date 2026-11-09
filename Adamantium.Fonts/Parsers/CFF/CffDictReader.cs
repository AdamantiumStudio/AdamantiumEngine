using System.IO;

namespace Adamantium.Fonts.Parsers.CFF;

/// <summary>Reads the bytes of a CFF DICT in order, each once.</summary>
internal sealed class CffDictReader
{
    private readonly byte[] _data;

    public CffDictReader(byte[] data)
    {
        _data = data ?? [];
    }

    public int Position { get; private set; }

    public bool AtEnd => Position >= _data.Length;

    /// <exception cref="InvalidDataException">The DICT ends before the byte.</exception>
    public byte Peek(int ahead = 0)
    {
        if (ahead < 0 || ahead >= _data.Length - Position)
        {
            throw new InvalidDataException("A CFF DICT ends inside an operand or operator.");
        }

        return _data[Position + ahead];
    }

    /// <exception cref="InvalidDataException">The DICT ends before the byte.</exception>
    public byte Read()
    {
        var b = Peek();
        Position++;
        return b;
    }
}
