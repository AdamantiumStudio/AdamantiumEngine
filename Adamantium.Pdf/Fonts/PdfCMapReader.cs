using System;
using System.Collections.Generic;
using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Parsing;

namespace Adamantium.Pdf.Fonts;

/// <summary>Reads CMaps: Identity-H and Identity-V by name, embedded ones from their streams (ISO 32000-2, 9.7.5),
/// ToUnicode ones too (9.10.3). A chain of <c>usecmap</c> is followed no deeper than <see cref="MaxChain"/>.</summary>
public static class PdfCMapReader
{
    public const int MaxChain = 8;

    /// <summary>The CMap a font names or embeds; null for a predefined CMap other than the Identity ones, or one that
    /// cannot be read.</summary>
    public static PdfCMap Read(PdfObject value, PdfDocument document)
    {
        var chain = new List<PdfCMap>();
        var current = document.Resolve(value);
        while (current != null && chain.Count < MaxChain)
        {
            if (current is PdfName name)
            {
                var named = Predefined(name.Value);
                if (named == null)
                {
                    return chain.Count == 0 ? null : Assemble(chain, null);
                }

                return Assemble(chain, named);
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
            root?.Seal();
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

    private static PdfCMap Predefined(string name) => name switch
    {
        PdfEncodingNames.IdentityH => PdfCMap.Identity(false),
        PdfEncodingNames.IdentityV => PdfCMap.Identity(true),
        _ => null,
    };

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
                    ReadRanges(lexer, map, CMapKeywords.EndCidRange, cids: true);
                    break;
                case CMapKeywords.BeginCidChar:
                    ReadChars(lexer, map, CMapKeywords.EndCidChar, cids: true);
                    break;
                case CMapKeywords.BeginBfRange:
                    ReadRanges(lexer, map, CMapKeywords.EndBfRange, cids: false);
                    break;
                case CMapKeywords.BeginBfChar:
                    ReadChars(lexer, map, CMapKeywords.EndBfChar, cids: false);
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

    private static void ReadRanges(PdfLexer lexer, PdfCMap map, string end, bool cids)
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

            if (cids)
            {
                if (target.Kind == PdfTokenKind.Integer && target.Integer >= 0 && target.Integer <= uint.MaxValue)
                {
                    map.AddCids(new PdfCodeRange(length, from, to, (uint)target.Integer));
                }
            }
            else if (target.Kind == PdfTokenKind.HexString)
            {
                map.AddTexts(new PdfCodeRange(length, from, to, 0, Text(target.Bytes)));
            }
            else if (target.Kind == PdfTokenKind.ArrayStart)
            {
                var texts = ReadTexts(lexer, (long)to - from + 1);
                if (texts.Length > 0)
                {
                    map.AddTexts(new PdfCodeRange(length, from, from + (uint)texts.Length - 1, 0, Texts: texts));
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

    private static void ReadChars(PdfLexer lexer, PdfCMap map, string end, bool cids)
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

            if (cids && target.Kind == PdfTokenKind.Integer && target.Integer >= 0 && target.Integer <= uint.MaxValue)
            {
                map.AddCids(new PdfCodeRange(length, code, code, (uint)target.Integer));
            }
            else if (!cids && target.Kind == PdfTokenKind.HexString)
            {
                map.AddTexts(new PdfCodeRange(length, code, code, 0, Text(target.Bytes)));
            }
            else if (!cids && target.Kind == PdfTokenKind.Name)
            {
                map.AddTexts(new PdfCodeRange(length, code, code, 0, PdfSimpleEncodings.TextOf(target.Text)));
            }
        }
    }

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
