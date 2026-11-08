using System;
using System.Collections.Generic;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Parsing;

namespace Adamantium.Pdf.Document;

/// <summary>An object stream decoded once (ISO 32000-2, 7.5.7): its header of object numbers and offsets, and the
/// objects read out of it on demand.</summary>
internal sealed class PdfObjectStream
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _first;
    private readonly List<(int Number, int Offset)> _header = [];
    private readonly Dictionary<int, int> _indexOfNumber = [];

    public PdfObjectStream(ReadOnlyMemory<byte> data, int count, int first)
    {
        _data = data;
        _first = first;
        var lexer = new PdfLexer(data);
        for (var i = 0; i < count; i++)
        {
            var number = lexer.Next();
            var offset = lexer.Next();
            if (number.Kind != PdfTokenKind.Integer || offset.Kind != PdfTokenKind.Integer || number.Integer < 0 ||
                number.Integer > int.MaxValue || offset.Integer < 0 || first + offset.Integer >= data.Length)
            {
                break;
            }

            _header.Add(((int)number.Integer, (int)offset.Integer));
            _indexOfNumber.TryAdd((int)number.Integer, _header.Count - 1);
        }
    }

    public int Count => _header.Count;

    public int NumberAt(int index) => _header[index].Number;

    /// <summary>The object with <paramref name="number"/>, looked for at <paramref name="index"/> first. Null when
    /// the stream does not hold it.</summary>
    public PdfObject Read(int number, int index)
    {
        if (index < 0 || index >= _header.Count || _header[index].Number != number)
        {
            if (!_indexOfNumber.TryGetValue(number, out index))
            {
                return null;
            }
        }

        var lexer = new PdfLexer(_data) { Position = Math.Min(_data.Length, _first + _header[index].Offset) };
        return new PdfObjectParser(lexer).ReadObject() ?? PdfNull.Value;
    }
}
