using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Pdf.CrossReference;
using Adamantium.Pdf.Filters;
using Adamantium.Pdf.Fonts;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Parsing;
using Adamantium.Pdf.Security;

namespace Adamantium.Pdf.Document;

/// <summary>A PDF file opened for reading. Objects are read when first asked for and kept; a file whose
/// cross-reference is broken is repaired by scanning, once, the first time an object cannot be found where it should
/// be. One thread at a time.</summary>
public sealed class PdfDocument : IPdfObjectResolver, IPdfLengthResolver
{
    private const int MaxReferenceChain = 32;
    private const int MaxReadDepth = 64;
    private const string DefaultVersion = "1.0";

    private static readonly string[] TrailerKeys = [PdfKeys.Root, PdfKeys.Info, PdfKeys.Id, PdfKeys.Encrypt];

    private readonly ReadOnlyMemory<byte> _data;
    private readonly PdfReadOptions _options;
    private readonly Dictionary<int, PdfObject> _objects = [];
    private readonly Dictionary<int, PdfObjectStream> _objectStreams = [];
    private readonly Dictionary<PdfObjectId, PdfFont> _fonts = [];
    private readonly HashSet<int> _reading = [];
    private readonly int _headerOffset;
    private PdfCrossReference _crossReference;
    private PdfCrossReference _repaired;
    private PdfStandardSecurityHandler _security;
    private int _encryptNumber = -1;
    private PdfCrossReference _offsetsOf;
    private List<long> _offsets;
    private PdfPageTree _pages;
    private PdfPageLabels _pageLabels;

    private PdfDocument(ReadOnlyMemory<byte> data, PdfReadOptions options)
    {
        _data = data;
        _options = options ?? PdfReadOptions.Default;
        var reader = new PdfCrossReferenceReader(data, _options.DecodeLimits);
        try
        {
            _crossReference = reader.Read();
        }
        catch (PdfException)
        {
            _crossReference = Repair();
        }

        _headerOffset = reader.HeaderOffset;
        Version = ReadHeaderVersion(data.Span);
        OpenSecurity();
        if (IsAuthenticated && Catalog == null && !_crossReference.IsRepaired)
        {
            _crossReference = Repair();
            OpenSecurity();
        }

        if (IsAuthenticated && Catalog == null)
        {
            throw new PdfException("The file has no document catalog.");
        }

        RaiseVersionFromCatalog();
    }

    /// <summary>The version the header states, raised by the catalog's <c>/Version</c> when that is later.</summary>
    public string Version { get; private set; }

    public PdfDictionary Trailer => _crossReference.Trailer;

    public PdfDictionary Catalog => Resolve(Trailer[PdfKeys.Root]) as PdfDictionary;

    /// <summary>The document information dictionary; null until the file is open, its strings being encrypted.
    /// </summary>
    public PdfDictionary Info => IsAuthenticated ? Resolve(Trailer[PdfKeys.Info]) as PdfDictionary : null;

    /// <summary>Whether the file's cross-reference was broken and rebuilt by scanning.</summary>
    public bool IsRepaired => _crossReference.IsRepaired;

    public bool IsEncrypted => _security != null;

    /// <summary>Whether the content can be read: the file is not encrypted, or a password has opened it.</summary>
    public bool IsAuthenticated => _security == null || _security.IsAuthenticated;

    public PdfPermissions Permissions => _security?.Permissions ?? PdfPermissions.All;

    public PdfStandardSecurityHandler Security => _security;

    public PdfDecodeLimits DecodeLimits => _options.DecodeLimits;

    public int PageCount => Pages.Count;

    public IReadOnlyList<PdfPage> Pages
    {
        get
        {
            EnsureAuthenticated();
            return (_pages ??= new PdfPageTree(this)).Pages;
        }
    }

    public PdfPageLabels PageLabels => _pageLabels ??= new PdfPageLabels(this);

    public static PdfDocument Open(byte[] data, PdfReadOptions options = null) =>
        new(data ?? throw new ArgumentNullException(nameof(data)), options);

    public static PdfDocument Open(string path, PdfReadOptions options = null) => Open(File.ReadAllBytes(path), options);

    public static PdfDocument Open(Stream stream, PdfReadOptions options = null)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Open(copy.ToArray(), options);
    }

    /// <summary>Tries a password on an encrypted file; true when the file is open after it.</summary>
    public bool Authenticate(string password)
    {
        if (_security == null)
        {
            return true;
        }

        if (!_security.Authenticate(password))
        {
            return false;
        }

        var encrypt = _encryptNumber >= 0 ? _objects.GetValueOrDefault(_encryptNumber) : null;
        ForgetReadObjects();
        if (encrypt != null)
        {
            _objects[_encryptNumber] = encrypt;
        }

        if (_crossReference.IsRepaired)
        {
            AddObjectStreams(_crossReference);
        }

        RaiseVersionFromCatalog();
        return true;
    }

    public PdfObject Resolve(PdfObject value)
    {
        for (var hops = 0; value is PdfReference reference; hops++)
        {
            if (hops == MaxReferenceChain)
            {
                return PdfNull.Value;
            }

            value = GetObject(reference.Id.Number);
        }

        return value;
    }

    /// <summary>The font read before from the object <paramref name="id"/>, or null.</summary>
    internal PdfFont CachedFont(PdfObjectId id) => _fonts.GetValueOrDefault(id);

    internal void CacheFont(PdfObjectId id, PdfFont font) => _fonts[id] = font;

    /// <summary>The predefined CMaps the document's fonts have named, by name; null for a name that is none.</summary>
    internal Dictionary<string, PdfCMap> PredefinedCMaps { get; } = [];

    /// <summary>The indirect object with <paramref name="number"/>, or <see cref="PdfNull.Value"/>.</summary>
    public PdfObject GetObject(int number)
    {
        if (_objects.TryGetValue(number, out var known))
        {
            return known;
        }

        if (_reading.Count >= MaxReadDepth || !_reading.Add(number))
        {
            return PdfNull.Value;
        }

        try
        {
            var value = Load(number) ?? PdfNull.Value;
            _objects[number] = value;
            return value;
        }
        catch (Exception exception) when (exception is PdfException or InvalidDataException or
                                              System.Security.Cryptography.CryptographicException)
        {
            _objects[number] = PdfNull.Value;
            return PdfNull.Value;
        }
        finally
        {
            _reading.Remove(number);
        }
    }

    /// <summary>A stream's bytes decrypted and through its general filters.</summary>
    /// <exception cref="PdfException">The stream cannot be decrypted or decoded.</exception>
    public PdfDecodedStream Decode(PdfStream stream)
    {
        try
        {
            var data = Decrypt(stream);
            return PdfStreamDecoder.Decode(stream.Dictionary, data, this, _options.DecodeLimits);
        }
        catch (Exception exception) when (exception is InvalidDataException or
                                              System.Security.Cryptography.CryptographicException)
        {
            throw new PdfException("A stream cannot be decoded: " + exception.Message, exception);
        }
    }

    /// <summary>The text of a string in the info dictionary, or null.</summary>
    public string GetInfoText(string key) => (Resolve(Info?[key]) as PdfString)?.ToText();

    /// <summary>Reads the length object on its own - a header and one integer - so that a chain of lengths never
    /// nests one read inside another.</summary>
    long? IPdfLengthResolver.ResolveLength(PdfReference reference)
    {
        var number = reference.Id.Number;
        if (_objects.TryGetValue(number, out var known))
        {
            return (known as PdfInteger)?.Value;
        }

        if (!_crossReference.TryGet(number, out var entry))
        {
            return null;
        }

        if (entry.Kind == PdfCrossReferenceEntryKind.InObjectStream)
        {
            return _objectStreams.TryGetValue(entry.StreamNumber, out var objectStream)
                ? (objectStream?.Read(number, entry.Index) as PdfInteger)?.Value
                : null;
        }

        return entry.Kind == PdfCrossReferenceEntryKind.InFile
            ? IntegerObjectAt(number, entry.Offset) ?? IntegerObjectAt(number, entry.Offset + _headerOffset)
            : null;
    }

    private long? IntegerObjectAt(int number, long offset)
    {
        if (offset < 0 || offset >= _data.Length)
        {
            return null;
        }

        var lexer = new PdfLexer(_data) { Position = (int)offset };
        var header = lexer.Next();
        var generation = lexer.Next();
        var keyword = lexer.Next();
        var value = lexer.Next();
        return header.Kind == PdfTokenKind.Integer && header.Integer == number &&
               generation.Kind == PdfTokenKind.Integer && keyword.IsKeyword(PdfKeywords.Obj) &&
               value.Kind == PdfTokenKind.Integer
            ? value.Integer
            : null;
    }

    internal void EnsureAuthenticated()
    {
        if (!IsAuthenticated)
        {
            throw new PdfException("The file is encrypted and no password has opened it.");
        }
    }

    private PdfObject Load(int number)
    {
        if (_crossReference.TryGet(number, out var entry))
        {
            var value = LoadEntry(number, entry, _crossReference);
            if (value != null)
            {
                return value;
            }
        }

        if (_repaired == null && !_crossReference.IsRepaired)
        {
            _repaired = Repair();
        }

        return _repaired != null && _repaired.TryGet(number, out var found) ? LoadEntry(number, found, _repaired) : null;
    }

    private PdfObject LoadEntry(int number, PdfCrossReferenceEntry entry, PdfCrossReference crossReference)
    {
        switch (entry.Kind)
        {
            case PdfCrossReferenceEntryKind.InFile:
                return LoadFromFile(number, crossReference, entry.Offset, 0) ??
                       (crossReference.IsRepaired || _headerOffset == 0
                           ? null
                           : LoadFromFile(number, crossReference, entry.Offset, _headerOffset));
            case PdfCrossReferenceEntryKind.InObjectStream:
                return ObjectStream(entry.StreamNumber)?.Read(number, entry.Index);
            default:
                return null;
        }
    }

    private PdfObject LoadFromFile(int number, PdfCrossReference crossReference, long offset, long shift)
    {
        var indirect = ReadIndirectAt(crossReference, offset, shift, this);
        if (indirect == null || indirect.Value.Id.Number != number)
        {
            return null;
        }

        var value = indirect.Value.Value;
        if (_security != null && number != _encryptNumber)
        {
            DecryptStrings(value, indirect.Value.Id);
        }

        if (value is PdfStream stream)
        {
            stream.Id = indirect.Value.Id;
        }

        return value;
    }

    private PdfObjectStream ObjectStream(int number)
    {
        if (_objectStreams.TryGetValue(number, out var known))
        {
            return known;
        }

        PdfObjectStream objectStream = null;
        if (GetObject(number) is PdfStream stream &&
            stream.Dictionary[PdfKeys.Type] is PdfName { Value: PdfObjectTypes.ObjStm })
        {
            var count = Resolve(stream.Dictionary[PdfKeys.N]) as PdfInteger;
            var first = Resolve(stream.Dictionary[PdfKeys.First]) as PdfInteger;
            if (count != null && first != null && count.Value >= 0 && first.Value >= 0)
            {
                var data = Decode(stream).Data;
                objectStream = new PdfObjectStream(data, (int)Math.Min(count.Value, data.Length),
                    (int)Math.Min(first.Value, data.Length));
            }
        }

        _objectStreams[number] = objectStream;
        return objectStream;
    }

    private ReadOnlyMemory<byte> Decrypt(PdfStream stream)
    {
        if (_security == null || stream.Id.Number == _encryptNumber)
        {
            return stream.Data;
        }

        var dictionary = stream.Dictionary;
        if (dictionary[PdfKeys.Type] is PdfName { Value: PdfObjectTypes.XRef })
        {
            return stream.Data;
        }

        if (!_security.EncryptsMetadata && dictionary[PdfKeys.Type] is PdfName { Value: PdfObjectTypes.Metadata })
        {
            return stream.Data;
        }

        EnsureAuthenticated();
        var method = _security.StreamMethod;
        if (FirstFilter(dictionary) == PdfFilterNames.Crypt)
        {
            var parameters = Resolve(dictionary[PdfKeys.DecodeParms]);
            var options = parameters is PdfArray array && array.Count > 0 ? Resolve(array[0]) : parameters;
            var name = (Resolve((options as PdfDictionary)?[PdfKeys.Name]) as PdfName)?.Value ??
                       PdfCryptNames.Identity;
            method = _security.MethodOf(name);
        }

        return _security.Decrypt(stream.Id, stream.Data.Span, method);
    }

    private string FirstFilter(PdfDictionary dictionary)
    {
        return Resolve(dictionary[PdfKeys.Filter]) switch
        {
            PdfName name => name.Value,
            PdfArray { Count: > 0 } array => (Resolve(array[0]) as PdfName)?.Value,
            _ => null,
        };
    }

    private void DecryptStrings(PdfObject root, PdfObjectId id)
    {
        if (!_security.IsAuthenticated || _security.StringMethod == PdfCryptMethod.None)
        {
            return;
        }

        var pending = new Stack<PdfObject>();
        pending.Push(root is PdfStream stream ? stream.Dictionary : root);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case PdfString text:
                    text.Bytes = _security.Decrypt(id, text.Bytes, _security.StringMethod);
                    break;
                case PdfArray array:
                    foreach (var item in array)
                    {
                        pending.Push(item);
                    }

                    break;
                case PdfDictionary dictionary:
                    foreach (var (_, value) in dictionary)
                    {
                        pending.Push(value);
                    }

                    break;
            }
        }
    }

    private void OpenSecurity()
    {
        var encrypt = Trailer[PdfKeys.Encrypt];
        if (encrypt == null)
        {
            return;
        }

        if (encrypt is PdfReference reference)
        {
            _encryptNumber = reference.Id.Number;
        }

        if (Resolve(encrypt) is not PdfDictionary dictionary)
        {
            return;
        }

        var id = Resolve(Trailer[PdfKeys.Id]) as PdfArray;
        var firstId = id is { Count: > 0 } ? (Resolve(id[0]) as PdfString)?.Bytes : null;
        _security = new PdfStandardSecurityHandler(dictionary, firstId);
        if (_options.Password == null || !_security.Authenticate(_options.Password))
        {
            _security.Authenticate(string.Empty);
        }

        ForgetReadObjects();
        if (_encryptNumber >= 0)
        {
            _objects[_encryptNumber] = dictionary;
        }

        if (_crossReference.IsRepaired && _security.IsAuthenticated)
        {
            AddObjectStreams(_crossReference);
        }
    }

    /// <summary>Drops what was read before the key was known: its strings and object streams were still encrypted.
    /// </summary>
    private void ForgetReadObjects()
    {
        _objects.Clear();
        _objectStreams.Clear();
        _fonts.Clear();
        _pages = null;
        _pageLabels = null;
    }

    private void RaiseVersionFromCatalog()
    {
        if (!IsAuthenticated || Resolve(Catalog?[PdfKeys.Version]) is not PdfName name)
        {
            return;
        }

        if (System.Version.TryParse(name.Value, out var catalog) && System.Version.TryParse(Version, out var header) &&
            catalog > header)
        {
            Version = name.Value;
        }
    }

    private PdfCrossReference Repair()
    {
        var repaired = PdfFileRepair.Scan(_data);
        AddObjectStreams(repaired);
        return repaired;
    }

    /// <summary>A repaired file's objects inside object streams, and its catalog when no trailer names one.</summary>
    private void AddObjectStreams(PdfCrossReference repaired)
    {
        var numbers = new List<int>(repaired.Numbers);
        foreach (var number in numbers)
        {
            if (!repaired.TryGet(number, out var entry) || entry.Kind != PdfCrossReferenceEntryKind.InFile)
            {
                continue;
            }

            PdfIndirectObject? indirect;
            try
            {
                indirect = ReadIndirectAt(repaired, entry.Offset, 0, null);
            }
            catch (PdfException)
            {
                continue;
            }

            var value = indirect?.Value;
            var dictionary = value as PdfDictionary ?? (value as PdfStream)?.Dictionary;
            var type = (dictionary?[PdfKeys.Type] as PdfName)?.Value;
            if (repaired.Trailer[PdfKeys.Root] == null && type == PdfObjectTypes.Catalog)
            {
                repaired.Trailer.Set(PdfKeys.Root, new PdfReference(number, entry.Generation));
            }

            if (type == PdfObjectTypes.XRef)
            {
                foreach (var key in TrailerKeys)
                {
                    if (repaired.Trailer[key] == null && dictionary[key] != null)
                    {
                        repaired.Trailer.Set(key, dictionary[key]);
                    }
                }
            }

            if (type != PdfObjectTypes.ObjStm || value is not PdfStream)
            {
                continue;
            }

            var objectStream = ObjectStreamForRepair((PdfStream)value);
            for (var i = 0; i < (objectStream?.Count ?? 0); i++)
            {
                var inside = objectStream.NumberAt(i);
                if (!repaired.TryGet(inside, out _))
                {
                    repaired.Set(inside, new PdfCrossReferenceEntry(PdfCrossReferenceEntryKind.InObjectStream, 0, 0,
                        number, i));
                }
            }
        }
    }

    /// <summary>Reads the object at <paramref name="offset"/> no further than where the cross-reference puts the next
    /// one: a broken object - a string never closed - costs its own bytes, not the rest of the file.</summary>
    private PdfIndirectObject? ReadIndirectAt(PdfCrossReference crossReference, long offset, long shift,
        IPdfLengthResolver lengths)
    {
        var start = offset + shift;
        if (start < 0 || start >= _data.Length)
        {
            return null;
        }

        if (!ReferenceEquals(_offsetsOf, crossReference))
        {
            var offsets = new List<long>();
            foreach (var number in crossReference.Numbers)
            {
                if (crossReference.TryGet(number, out var entry) && entry.Kind == PdfCrossReferenceEntryKind.InFile)
                {
                    offsets.Add(entry.Offset);
                }
            }

            offsets.Sort();
            _offsets = offsets;
            _offsetsOf = crossReference;
        }

        var index = _offsets.BinarySearch(offset + 1);
        if (index < 0)
        {
            index = ~index;
        }

        var end = index < _offsets.Count ? Math.Clamp(_offsets[index] + shift, start, _data.Length) : _data.Length;
        var parser = new PdfObjectParser(new PdfLexer(_data[(int)start..(int)end]), lengths);
        return parser.ReadIndirectObject(0);
    }

    private PdfObjectStream ObjectStreamForRepair(PdfStream stream)
    {
        if (stream.Dictionary[PdfKeys.N] is not PdfInteger count ||
            stream.Dictionary[PdfKeys.First] is not PdfInteger first)
        {
            return null;
        }

        try
        {
            var encrypted = _security is { IsAuthenticated: true } ? Decrypt(stream) : stream.Data;
            var data = PdfStreamDecoder.Decode(stream.Dictionary, encrypted, PdfDirectResolver.Instance,
                _options.DecodeLimits).Data;
            return new PdfObjectStream(data, (int)Math.Clamp(count.Value, 0, data.Length),
                (int)Math.Clamp(first.Value, 0, data.Length));
        }
        catch (Exception exception) when (exception is PdfException or InvalidDataException or
                                              System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private string ReadHeaderVersion(ReadOnlySpan<byte> data)
    {
        var header = data.Slice(_headerOffset, Math.Min(16, data.Length - _headerOffset));
        var version = DefaultVersion;
        if (header.StartsWith(PdfKeywords.FileHeaderBytes))
        {
            var start = PdfKeywords.FileHeaderBytes.Length;
            var end = start;
            while (end < header.Length && (PdfCharacters.IsDigit(header[end]) || header[end] == '.'))
            {
                end++;
            }

            version = System.Text.Encoding.ASCII.GetString(header[start..end]);
        }

        return version;
    }
}
