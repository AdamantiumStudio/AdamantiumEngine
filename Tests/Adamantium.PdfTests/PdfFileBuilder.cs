using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Adamantium.PdfTests;

/// <summary>Writes small PDF files for the tests, by the format's rules: objects at the offsets the cross-reference
/// gives, as a table or as a stream, optionally packed in an object stream.</summary>
internal sealed class PdfFileBuilder
{
    private readonly SortedDictionary<int, byte[]> _objects = [];

    public string Header { get; set; } = "%PDF-1.7\n%âãÏÓ\n";

    public byte[] Prefix { get; set; } = [];

    public int Add(string body) => Add(Latin1(body));

    public int Add(byte[] body)
    {
        var number = _objects.Count == 0 ? 1 : LastNumber() + 1;
        _objects[number] = body;
        return number;
    }

    public void Set(int number, string body) => _objects[number] = Latin1(body);

    public void Set(int number, byte[] body) => _objects[number] = body;

    public int Next => (_objects.Count == 0 ? 0 : LastNumber()) + 1;

    public static byte[] Stream(string dictionary, byte[] data)
    {
        var head = Latin1("<< " + dictionary + " /Length " + data.Length.ToString(CultureInfo.InvariantCulture) +
                          " >>\nstream\n");
        var tail = Latin1("\nendstream");
        return Concat(head, data, tail);
    }

    public static byte[] Flate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflater = new ZLibStream(output, CompressionLevel.Optimal, true))
        {
            deflater.Write(data);
        }

        return output.ToArray();
    }

    public static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);

    public static byte[] Concat(params byte[][] parts)
    {
        using var output = new MemoryStream();
        foreach (var part in parts)
        {
            output.Write(part);
        }

        return output.ToArray();
    }

    /// <summary>The file with a classic cross-reference table.</summary>
    public byte[] Build(string trailer)
    {
        using var output = new MemoryStream();
        output.Write(Prefix);
        var start = output.Position;
        output.Write(Latin1(Header));
        var offsets = WriteObjects(output, start);
        var xref = output.Position - start;
        var size = LastNumber() + 1;
        var table = new StringBuilder();
        table.Append("xref\n0 ").Append(size).Append('\n');
        table.Append("0000000000 65535 f\r\n");
        for (var number = 1; number < size; number++)
        {
            table.Append(offsets.TryGetValue(number, out var offset)
                ? offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n\r\n"
                : "0000000000 00001 f\r\n");
        }

        table.Append("trailer\n<< /Size ").Append(size).Append(' ').Append(trailer).Append(" >>\n");
        table.Append("startxref\n").Append(xref).Append("\n%%EOF\n");
        output.Write(Latin1(table.ToString()));
        return output.ToArray();
    }

    /// <summary>The file with a cross-reference stream; the objects listed in <paramref name="packed"/> go into one
    /// object stream.</summary>
    public byte[] BuildWithStream(string trailer, ISet<int> packed = null, bool predictor = true)
    {
        packed ??= new HashSet<int>();
        using var output = new MemoryStream();
        output.Write(Latin1(Header));
        var offsets = new Dictionary<int, long>();
        var inStream = new Dictionary<int, int>();
        var packedNumbers = new List<int>();
        foreach (var (number, body) in _objects)
        {
            if (packed.Contains(number))
            {
                packedNumbers.Add(number);
                continue;
            }

            offsets[number] = output.Position;
            output.Write(Latin1(number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n"));
            output.Write(body);
            output.Write(Latin1("\nendobj\n"));
        }

        var objectStream = 0;
        if (packedNumbers.Count > 0)
        {
            objectStream = LastNumber() + 1;
            var header = new StringBuilder();
            using var bodies = new MemoryStream();
            for (var i = 0; i < packedNumbers.Count; i++)
            {
                header.Append(packedNumbers[i]).Append(' ').Append(bodies.Position).Append(' ');
                bodies.Write(_objects[packedNumbers[i]]);
                bodies.Write(Latin1("\n"));
                inStream[packedNumbers[i]] = i;
            }

            var headerBytes = Latin1(header.ToString());
            var data = Flate(Concat(headerBytes, bodies.ToArray()));
            offsets[objectStream] = output.Position;
            output.Write(Latin1(objectStream + " 0 obj\n"));
            output.Write(Stream("/Type /ObjStm /N " + packedNumbers.Count + " /First " + headerBytes.Length +
                                " /Filter /FlateDecode", data));
            output.Write(Latin1("\nendobj\n"));
        }

        var xrefNumber = Math.Max(LastNumber(), objectStream) + 1;
        var size = xrefNumber + 1;
        offsets[xrefNumber] = output.Position;
        var rows = new byte[size * 7];
        for (var number = 0; number < size; number++)
        {
            var row = rows.AsSpan(number * 7, 7);
            if (inStream.TryGetValue(number, out var index))
            {
                row[0] = 2;
                WriteBigEndian(row.Slice(1, 4), objectStream);
                WriteBigEndian(row.Slice(5, 2), index);
            }
            else if (offsets.TryGetValue(number, out var offset))
            {
                row[0] = 1;
                WriteBigEndian(row.Slice(1, 4), offset);
            }
        }

        var encoded = predictor ? Flate(PngUp(rows, 7)) : Flate(rows);
        var parameters = predictor ? " /DecodeParms << /Predictor 12 /Columns 7 >>" : string.Empty;
        output.Write(Latin1(xrefNumber + " 0 obj\n"));
        output.Write(Stream("/Type /XRef /Size " + size + " /W [1 4 2] /Filter /FlateDecode" + parameters + " " +
                            trailer, encoded));
        output.Write(Latin1("\nendobj\nstartxref\n" + offsets[xrefNumber] + "\n%%EOF\n"));
        return output.ToArray();
    }

    /// <summary>An incremental update after <paramref name="original"/>: the objects set since, a table for them and
    /// a trailer pointing back with <c>/Prev</c>.</summary>
    public static byte[] Update(byte[] original, IDictionary<int, string> objects, string trailer, long previous)
    {
        using var output = new MemoryStream();
        output.Write(original);
        var offsets = new SortedDictionary<int, long>();
        foreach (var (number, body) in objects)
        {
            offsets[number] = output.Position;
            output.Write(Latin1(number + " 0 obj\n" + body + "\nendobj\n"));
        }

        var xref = output.Position;
        var table = new StringBuilder("xref\n");
        foreach (var (number, offset) in offsets)
        {
            table.Append(number).Append(" 1\n").Append(offset.ToString("D10", CultureInfo.InvariantCulture))
                .Append(" 00000 n\r\n");
        }

        table.Append("trailer\n<< ").Append(trailer).Append(" /Prev ").Append(previous).Append(" >>\nstartxref\n")
            .Append(xref).Append("\n%%EOF\n");
        output.Write(Latin1(table.ToString()));
        return output.ToArray();
    }

    /// <summary>Where the last <c>startxref</c> of a file points.</summary>
    public static long StartXref(byte[] file)
    {
        var text = Encoding.Latin1.GetString(file);
        var at = text.LastIndexOf("startxref", StringComparison.Ordinal) + "startxref".Length;
        var end = at;
        while (end < text.Length && (char.IsWhiteSpace(text[end]) || char.IsDigit(text[end])))
        {
            end++;
        }

        return long.Parse(text[at..end].Trim(), CultureInfo.InvariantCulture);
    }

    private Dictionary<int, long> WriteObjects(MemoryStream output, long start)
    {
        var offsets = new Dictionary<int, long>();
        foreach (var (number, body) in _objects)
        {
            offsets[number] = output.Position - start;
            output.Write(Latin1(number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n"));
            output.Write(body);
            output.Write(Latin1("\nendobj\n"));
        }

        return offsets;
    }

    private int LastNumber()
    {
        var last = 0;
        foreach (var number in _objects.Keys)
        {
            last = number;
        }

        return last;
    }

    private static byte[] PngUp(byte[] data, int columns)
    {
        var rows = data.Length / columns;
        var output = new byte[rows * (columns + 1)];
        for (var row = 0; row < rows; row++)
        {
            output[row * (columns + 1)] = 2;
            for (var i = 0; i < columns; i++)
            {
                var above = row == 0 ? 0 : data[(row - 1) * columns + i];
                output[row * (columns + 1) + 1 + i] = (byte)(data[row * columns + i] - above);
            }
        }

        return output;
    }

    private static void WriteBigEndian(Span<byte> target, long value)
    {
        for (var i = target.Length - 1; i >= 0; i--)
        {
            target[i] = (byte)value;
            value >>= 8;
        }
    }
}
