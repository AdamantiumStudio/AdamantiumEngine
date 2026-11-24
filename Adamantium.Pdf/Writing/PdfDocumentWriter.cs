using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Navigation;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Writing;

/// <summary>Writes a new document page by page (ISO 32000-2, 7.5): each page and what it draws go to the stream as
/// they are added, so a long document is never held whole; <see cref="Finish"/> writes the page tree, the catalog, the
/// document's information and the cross-reference table. A stream drawn on several pages - one picture, one font - is
/// written once.</summary>
public sealed class PdfDocumentWriter
{
    private const int CatalogNumber = 1;
    private const int PagesNumber = 2;

    private readonly CountingStream _output;
    private readonly PdfObjectWriter _writer;
    private readonly List<long> _offsets = [-1, -1, -1];
    private readonly List<PdfReference> _pages = [];
    private readonly Dictionary<PdfStream, PdfReference> _streams = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<PdfStream> _lifting = new(ReferenceEqualityComparer.Instance);
    private bool _finished;

    /// <summary>A writer of a new document into <paramref name="output"/>, which it does not close.</summary>
    public PdfDocumentWriter(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = new CountingStream(output);
        _writer = new PdfObjectWriter(_output, null);
        _writer.Write("%PDF-1.7\n");
        _output.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
    }

    public string Title { get; set; }

    public string Author { get; set; }

    public string Subject { get; set; }

    /// <summary>The application the document was made in.</summary>
    public string Creator { get; set; }

    /// <summary>What turned it into PDF.</summary>
    public string Producer { get; set; } = "Adamantium";

    public DateTimeOffset? Created { get; set; }

    public int PageCount => _pages.Count;

    /// <summary>Writes <paramref name="value"/> as a new indirect object, and the streams inside it as their own; a
    /// stream written before is not written again. Streams read from a file are refused: their references point into
    /// that file.</summary>
    public PdfReference Add(PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckOpen();
        if (value is PdfStream stream)
        {
            return AddStream(stream);
        }

        return Write(PdfIndirection.Lift(value, AddStream));
    }

    /// <summary>Adds a page of <paramref name="mediaBox"/>, in points, drawn by <paramref name="content"/>.</summary>
    public void AddPage(PdfRectangle mediaBox, PdfContentBuilder content)
    {
        ArgumentNullException.ThrowIfNull(content);
        CheckOpen();
        if (!(mediaBox.Width > 0) || !(mediaBox.Height > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(mediaBox), "A page has a width and a height.");
        }

        var contents = Add(content.ToContentStream());
        var page = new PdfDictionary();
        page.Set(PdfKeys.Type, new PdfName("Page"));
        page.Set("Parent", new PdfReference(new PdfObjectId(PagesNumber, 0)));
        page.Set(PdfKeys.MediaBox, Box(mediaBox));
        page.Set(PdfKeys.Resources, content.ToResources());
        page.Set(PdfKeys.Contents, contents);
        _pages.Add(Add(page));
    }

    /// <summary>Writes the page tree, the catalog, the information and the cross-reference table; nothing can be added
    /// after it.</summary>
    public void Finish()
    {
        CheckOpen();
        var kids = new PdfArray();
        foreach (var page in _pages)
        {
            kids.Add(page);
        }

        var pages = new PdfDictionary();
        pages.Set(PdfKeys.Type, new PdfName(PdfKeys.Pages));
        pages.Set(PdfKeys.Kids, kids);
        pages.Set(PdfKeys.Count, new PdfInteger(_pages.Count));
        WriteAt(PagesNumber, pages);

        var catalog = new PdfDictionary();
        catalog.Set(PdfKeys.Type, new PdfName("Catalog"));
        catalog.Set(PdfKeys.Pages, new PdfReference(new PdfObjectId(PagesNumber, 0)));
        WriteAt(CatalogNumber, catalog);

        var info = Info();
        var at = _output.Position;
        _writer.Write($"xref\n0 {_offsets.Count}\n0000000000 65535 f\r\n");
        for (var number = 1; number < _offsets.Count; number++)
        {
            _writer.Write($"{_offsets[number]:D10} 00000 n\r\n");
        }

        var trailer = new PdfDictionary();
        trailer.Set(PdfKeys.Size, new PdfInteger(_offsets.Count));
        trailer.Set(PdfKeys.Root, new PdfReference(new PdfObjectId(CatalogNumber, 0)));
        if (info != null)
        {
            trailer.Set(PdfKeys.Info, info);
        }

        var id = new PdfString(Guid.NewGuid().ToByteArray(), true);
        trailer.Set(PdfKeys.Id, new PdfArray([id, id]));
        _writer.Write("trailer\n");
        _writer.WritePlain(trailer);
        _writer.Write($"\nstartxref\n{at}\n%%EOF\n");
        _output.Flush();
        _finished = true;
    }

    private PdfReference Info()
    {
        var info = new PdfDictionary();
        Text(info, "Title", Title);
        Text(info, "Author", Author);
        Text(info, "Subject", Subject);
        Text(info, "Creator", Creator);
        Text(info, "Producer", Producer);
        if (Created is { } created)
        {
            info.Set("CreationDate", PdfString.FromText(PdfDate.Format(created)));
        }

        return info.Count > 0 ? Write(info) : null;
    }

    private static void Text(PdfDictionary info, string key, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            info.Set(key, PdfString.FromText(value));
        }
    }

    private PdfReference AddStream(PdfStream stream)
    {
        if (stream.Id != default)
        {
            throw new PdfException("A stream read from a file cannot be written into a new one: what it refers to, " +
                                   "and how its bytes are encrypted, are that file's.");
        }

        return PdfIndirection.Once(stream, _streams, _lifting, own => Write(PdfIndirection.Lift(own, AddStream)));
    }

    private PdfReference Write(PdfObject value)
    {
        var number = _offsets.Count;
        _offsets.Add(-1);
        WriteAt(number, value);
        return new PdfReference(new PdfObjectId(number, 0));
    }

    private void WriteAt(int number, PdfObject value)
    {
        _offsets[number] = _output.Position;
        _writer.WriteIndirect(new PdfObjectId(number, 0), value, true);
    }

    private void CheckOpen()
    {
        if (_finished)
        {
            throw new InvalidOperationException("The document is finished; nothing can be added to it.");
        }
    }

    private static PdfArray Box(PdfRectangle box) =>
        new([new PdfReal(box.Left), new PdfReal(box.Bottom), new PdfReal(box.Right), new PdfReal(box.Top)]);

    /// <summary>The output, counting what goes into it: offsets of objects without asking a stream that may not seek.
    /// </summary>
    private sealed class CountingStream(Stream inner) : Stream
    {
        private long _position;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _position;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            inner.Write(buffer, offset, count);
            _position += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            _position += buffer.Length;
        }

        public override void WriteByte(byte value)
        {
            inner.WriteByte(value);
            _position++;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
