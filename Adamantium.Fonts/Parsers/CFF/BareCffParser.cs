using System;
using System.IO;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Tables.CFF;

namespace Adamantium.Fonts.Parsers.CFF;

/// <summary>Reads a CFF font program on its own, without the sfnt wrapper of an OpenType file: one font, its units per
/// em from the font matrix, its built-in encoding, and CID keys when it is CID-keyed.</summary>
internal sealed class BareCffParser : IFontParser
{
    private readonly byte[] _data;

    public BareCffParser(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        Typeface = new Typeface { Parser = this };
    }

    public Typeface Typeface { get; }

    public void Parse()
    {
        if (_data.Length < 4 || _data[0] != 1)
        {
            throw new InvalidDataException("The data is not a CFF font: its major version is not 1.");
        }

        var font = new Font(Typeface);
        Typeface.AddFont(font);
        Typeface.SetDefaultFont();
        CFFFont cff;
        try
        {
            var reader = new FontStreamReader(_data);
            cff = ((ICFFParser)new CFFParser(0, reader)).Parse();
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The CFF font is malformed: " + exception.Message, exception);
        }

        if (cff == null)
        {
            throw new InvalidDataException("The CFF data holds no font.");
        }

        var matrix = cff.GetTopDictOperatorValue(DictOperatorsType.FontMatrix).AsList();
        font.UnitsPerEm = matrix is { Count: > 0 } && matrix[0] > 0 && 1 / matrix[0] <= ushort.MaxValue
            ? (ushort)Math.Round(1 / matrix[0])
            : (ushort)1000;
        font.FullName = cff.FullName ?? cff.Name;
        Typeface.SetGlyphs(cff.Glyphs);
        Typeface.OutlineSource = cff.OutlineSource;
        Typeface.BuiltInEncoding = cff.Encoding;
        Typeface.IsCidKeyed = cff.IsCIDFont;
    }

    public void ReadFontName()
    {
    }

    public byte[] GetFontBytes() => _data;
}
