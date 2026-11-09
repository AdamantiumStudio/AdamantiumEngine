using System;
using System.Collections.Generic;
using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Parsing;

namespace Adamantium.Pdf.Fonts;

/// <summary>Reads CMaps: the predefined ones by name (ISO 32000-1, table 118), from Adobe's CMap resources the
/// assembly carries, embedded ones from their streams (ISO 32000-2, 9.7.5), ToUnicode ones too (9.10.3). A chain of
/// <c>usecmap</c> is followed no deeper than <see cref="MaxChain"/>.</summary>
public static class PdfCMapReader
{
    public const int MaxChain = 8;

    /// <summary>The CMap a font names or embeds; null for a name that is no predefined CMap, or a stream that cannot be
    /// read. A predefined CMap is read once per document.</summary>
    public static PdfCMap Read(PdfObject value, PdfDocument document)
    {
        var chain = new List<PdfCMap>();
        var current = document.Resolve(value);
        while (current != null && chain.Count < MaxChain)
        {
            if (current is PdfName name)
            {
                var named = Predefined(name.Value, document.PredefinedCMaps);
                return chain.Count == 0 ? named : Assemble(chain, named);
            }

            if (current is not PdfStream stream)
            {
                break;
            }

            byte[] data;
            try
            {
                data = document.Decode(stream).Data.ToArray();
            }
            catch (PdfException)
            {
                break;
            }

            var map = Parse(data, out var usedName);
            chain.Add(map);
            current = document.Resolve(stream.Dictionary[PdfFontKeys.UseCMap]) ??
                      (usedName != null ? new PdfName(usedName) : null);
        }

        return chain.Count == 0 ? null : Assemble(chain, null);
    }

    /// <summary>A CMap's text, as a ToUnicode or embedded encoding stream holds it.</summary>
    public static PdfCMap Parse(ReadOnlyMemory<byte> data) => Assemble([Parse(data, out _)], null);

    private static PdfCMap Assemble(List<PdfCMap> chain, PdfCMap root)
    {
        if (chain.Count == 0)
        {
            return root;
        }

        var result = chain[^1];
        if (root != null)
        {
            result.Use(root);
        }

        for (var i = chain.Count - 2; i >= 0; i--)
        {
            chain[i].Use(result);
            result = chain[i];
        }

        result.Seal();
        return result;
    }

    /// <summary>The predefined CMap of <paramref name="name"/>, null when there is none; each CMap of its
    /// <c>usecmap</c> chain is read once into <paramref name="known"/>, which keeps the names of none too.</summary>
    internal static PdfCMap Predefined(string name, Dictionary<string, PdfCMap> known)
    {
        var chain = new List<(string Name, PdfCMap Map)>();
        PdfCMap root = null;
        for (var current = name; current != null && chain.Count < MaxChain;)
        {
            if (known.TryGetValue(current, out root))
            {
                break;
            }

            if (current is PdfEncodingNames.IdentityH or PdfEncodingNames.IdentityV)
            {
                root = PdfCMap.Identity(current == PdfEncodingNames.IdentityV);
                known[current] = root;
                break;
            }

            var data = PdfPredefinedCMaps.Data(current);
            if (data.IsEmpty)
            {
                break;
            }

            chain.Add((current, Parse(data, out var used)));
            current = used;
        }

        if (chain.Count == 0)
        {
            known.TryAdd(name, root);
            return root;
        }

        var result = root;
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var map = chain[i].Map;
            if (result != null)
            {
                map.Use(result);
            }

            map.Seal();
            known[chain[i].Name] = map;
            result = map;
        }

        return result;
    }

    private static PdfCMap Parse(ReadOnlyMemory<byte> data, out string usedName)
    {
        usedName = null;
        var map = new PdfCMap(null);
        var lexer = new PdfLexer(data);
        string lastName = null;
        while (true)
        {
            var token = lexer.Next();
            switch (token.Kind)
            {
                case PdfTokenKind.EndOfData:
                    return map;
                case PdfTokenKind.Name:
                    if (token.Text == PdfFontKeys.CMapName && lexer.Peek().Kind == PdfTokenKind.Name)
                    {
                        map.Name = lexer.Next().Text;
                    }
                    else if (token.Text == CMapKeywords.WMode && lexer.Peek().Kind == PdfTokenKind.Integer)
                    {
                        map.IsVertical = lexer.Next().Integer == 1;
                    }
                    else if (token.Text == PdfFontKeys.Registry && lexer.Peek().Kind == PdfTokenKind.LiteralString)
                    {
                        map.Registry = Encoding.Latin1.GetString(lexer.Next().Bytes);
                    }
                    else if (token.Text == PdfFontKeys.Ordering && lexer.Peek().Kind == PdfTokenKind.LiteralString)
                    {
                        map.Ordering = Encoding.Latin1.GetString(lexer.Next().Bytes);
                    }
                    else
                    {
                        lastName = token.Text;
                    }

                    continue;
                case PdfTokenKind.Keyword:
                    break;
                default:
                    continue;
            }

            switch (token.Text)
            {
                case CMapKeywords.BeginCodeSpaceRange:
                    ReadCodeSpaces(lexer, map);
                    break;
                case CMapKeywords.BeginCidRange:
                    ReadRanges(lexer, map, CMapKeywords.EndCidRange, PdfCMapMapping.Cids);
                    break;
                case CMapKeywords.BeginCidChar:
                    ReadChars(lexer, map, CMapKeywords.EndCidChar, PdfCMapMapping.Cids);
                    break;
                case CMapKeywords.BeginNotDefRange:
                    ReadRanges(lexer, map, CMapKeywords.EndNotDefRange, PdfCMapMapping.NotDef);
                    break;
                case CMapKeywords.BeginNotDefChar:
                    ReadChars(lexer, map, CMapKeywords.EndNotDefChar, PdfCMapMapping.NotDef);
                    break;
                case CMapKeywords.BeginBfRange:
                    ReadRanges(lexer, map, CMapKeywords.EndBfRange, PdfCMapMapping.Texts);
                    break;
                case CMapKeywords.BeginBfChar:
                    ReadChars(lexer, map, CMapKeywords.EndBfChar, PdfCMapMapping.Texts);
                    break;
                case CMapKeywords.UseCMap:
                    usedName = lastName;
                    break;
            }
        }
    }

    private static void ReadCodeSpaces(PdfLexer lexer, PdfCMap map)
    {
        while (true)
        {
            var low = lexer.Next();
            if (low.Kind != PdfTokenKind.HexString)
            {
                return;
            }

            var high = lexer.Next();
            if (high.Kind != PdfTokenKind.HexString)
            {
                return;
            }

            if (Code(low, out var from, out var length) && Code(high, out var to, out var highLength) &&
                length == highLength)
            {
                map.AddCodeSpace(new PdfCodeSpaceRange(length, from, to));
            }
        }
    }

    private static void ReadRanges(PdfLexer lexer, PdfCMap map, string end, PdfCMapMapping mapping)
    {
        while (true)
        {
            var low = lexer.Next();
            if (low.Kind != PdfTokenKind.HexString)
            {
                return;
            }

            var high = lexer.Next();
            var target = lexer.Next();
            if (high.Kind != PdfTokenKind.HexString || !Code(low, out var from, out var length) ||
                !Code(high, out var to, out var highLength) || length != highLength || to < from)
            {
                if (target.IsKeyword(end))
                {
                    return;
                }

                if (target.Kind == PdfTokenKind.ArrayStart)
                {
                    ReadTexts(lexer, 0);
                }

                continue;
            }

            if (mapping != PdfCMapMapping.Texts)
            {
                if (IsCid(target))
                {
                    map.Add(mapping, new PdfCodeRange(length, from, to, (uint)target.Integer));
                }
            }
            else if (target.Kind == PdfTokenKind.HexString)
            {
                map.Add(mapping, new PdfCodeRange(length, from, to, 0, Text(target.Bytes)));
            }
            else if (target.Kind == PdfTokenKind.ArrayStart)
            {
                var texts = ReadTexts(lexer, (long)to - from + 1);
                if (texts.Length > 0)
                {
                    map.Add(mapping, new PdfCodeRange(length, from, from + (uint)texts.Length - 1, 0, Texts: texts));
                }
            }
        }
    }

    private static string[] ReadTexts(PdfLexer lexer, long most)
    {
        var texts = new List<string>();
        while (true)
        {
            var item = lexer.Next();
            if (item.Kind is PdfTokenKind.ArrayEnd or PdfTokenKind.EndOfData)
            {
                return texts.ToArray();
            }

            if (texts.Count < most)
            {
                texts.Add(item.Kind == PdfTokenKind.HexString ? Text(item.Bytes) : null);
            }
        }
    }

    private static void ReadChars(PdfLexer lexer, PdfCMap map, string end, PdfCMapMapping mapping)
    {
        while (true)
        {
            var source = lexer.Next();
            if (source.Kind != PdfTokenKind.HexString)
            {
                return;
            }

            var target = lexer.Next();
            if (!Code(source, out var code, out var length))
            {
                if (target.IsKeyword(end))
                {
                    return;
                }

                continue;
            }

            if (mapping != PdfCMapMapping.Texts)
            {
                if (IsCid(target))
                {
                    map.Add(mapping, new PdfCodeRange(length, code, code, (uint)target.Integer));
                }
            }
            else if (target.Kind == PdfTokenKind.HexString)
            {
                map.Add(mapping, new PdfCodeRange(length, code, code, 0, Text(target.Bytes)));
            }
            else if (target.Kind == PdfTokenKind.Name)
            {
                map.Add(mapping, new PdfCodeRange(length, code, code, 0, PdfSimpleEncodings.TextOf(target.Text)));
            }
        }
    }

    private static bool IsCid(PdfToken token) =>
        token.Kind == PdfTokenKind.Integer && token.Integer >= 0 && token.Integer <= uint.MaxValue;

    private static bool Code(PdfToken token, out uint code, out int length)
    {
        code = 0;
        var bytes = token.Bytes ?? [];
        length = bytes.Length;
        if (length is < 1 or > 4)
        {
            return false;
        }

        foreach (var b in bytes)
        {
            code = (code << 8) | b;
        }

        return true;
    }

    private static string Text(byte[] bytes) =>
        bytes is { Length: > 0 } ? Encoding.BigEndianUnicode.GetString(bytes) : string.Empty;
}
