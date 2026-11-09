using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Encodings;

namespace Adamantium.Fonts.Parsers.Type1;

/// <summary>Reads a Type 1 font program (Adobe Type 1 Font Format): PFB segments, PFA hex, or the clear text and binary
/// encrypted part as PDF embeds them.</summary>
internal sealed class Type1Parser : IFontParser
{
    private const int MaxSubrs = 65536;
    private const string NotDef = ".notdef";

    private readonly byte[] _data;

    public Type1Parser(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        Typeface = new Typeface { Parser = this };
    }

    public Typeface Typeface { get; }

    public void Parse()
    {
        var program = Read(_data);
        var font = new Font(Typeface);
        Typeface.AddFont(font);
        Typeface.SetDefaultFont();
        var matrix = program.FontMatrix;
        font.UnitsPerEm = matrix is { Length: > 0 } && matrix[0] > 0 && 1 / matrix[0] <= ushort.MaxValue
            ? (ushort)Math.Round(1 / matrix[0])
            : (ushort)1000;
        font.FullName = program.FontName;

        var names = new List<string>(program.GlyphNames.Count);
        if (program.CharStrings.ContainsKey(NotDef))
        {
            names.Add(NotDef);
        }

        foreach (var name in program.GlyphNames)
        {
            if (name != NotDef)
            {
                names.Add(name);
            }
        }

        var source = new Type1GlyphOutlineSource(program.Subrs, program.CharStrings, _data.Length);
        var glyphs = new Glyph[names.Count];
        var indexOf = new Dictionary<string, int>(names.Count, StringComparer.Ordinal);
        var widths = new Type1CharStringInterpreter(program.Subrs, source.CharStringOf, source.Budget);
        for (var i = 0; i < names.Count; i++)
        {
            var glyph = Glyph.Create((uint)i, OutlineType.CompactFontFormat);
            glyph.Name = names[i];
            glyph.SetOutlineSource(source);
            try
            {
                widths.Run(program.CharStrings[names[i]], widthOnly: true);
                glyph.AdvanceWidth = (ushort)Math.Clamp(Math.Round(widths.Width), 0, ushort.MaxValue);
                glyph.LeftSideBearing = (short)Math.Clamp(Math.Round(widths.SideBearingX), short.MinValue,
                    short.MaxValue);
            }
            catch (InvalidDataException)
            {
                glyph.IsInvalid = true;
            }

            glyphs[i] = glyph;
            indexOf[names[i]] = i;
        }

        var encoding = new int[256];
        for (var code = 0; code < encoding.Length; code++)
        {
            var name = program.Encoding != null ? program.Encoding[code] : StandardEncoding.NameOf(code);
            encoding[code] = name != null && indexOf.TryGetValue(name, out var index) ? index : -1;
        }

        Typeface.SetGlyphs(glyphs);
        Typeface.OutlineSource = source;
        Typeface.BuiltInEncoding = encoding;
    }

    public void ReadFontName()
    {
    }

    public byte[] GetFontBytes() => _data;

    private static Type1FontProgram Read(byte[] data)
    {
        var (clear, encrypted) = Split(data);
        var program = new Type1FontProgram();
        ReadClearText(clear, program);
        var privatePart = Type1Encryption.Decrypt(encrypted, Type1Encryption.EexecKey, 4);
        ReadPrivate(privatePart, program);
        if (program.CharStrings.Count == 0)
        {
            throw new InvalidDataException("The Type 1 font has no CharStrings.");
        }

        var skip = program.LenIV;
        foreach (var key in new List<int>(program.Subrs.Keys))
        {
            program.Subrs[key] = Type1Encryption.Decrypt(program.Subrs[key], Type1Encryption.CharStringKey, skip);
        }

        foreach (var name in program.GlyphNames)
        {
            program.CharStrings[name] =
                Type1Encryption.Decrypt(program.CharStrings[name], Type1Encryption.CharStringKey, skip);
        }

        return program;
    }

    private static int EexecOf(byte[] data)
    {
        var first = -1;
        for (var from = 0; from < data.Length;)
        {
            var found = data.AsSpan(from).IndexOf("eexec"u8);
            if (found < 0)
            {
                break;
            }

            var at = from + found;
            first = first < 0 ? at : first;
            var before = at;
            while (before > 0 && Type1Tokenizer.IsWhitespace(data[before - 1]))
            {
                before--;
            }

            if (before < at && data.AsSpan(0, before).EndsWith("currentfile"u8))
            {
                return at;
            }

            from = at + 5;
        }

        return first;
    }

    private static (byte[] Clear, byte[] Encrypted) Split(byte[] data)
    {
        if (data.Length > 0 && data[0] == 0x80)
        {
            return SplitSegments(data);
        }

        var eexec = EexecOf(data);
        if (eexec < 0)
        {
            throw new InvalidDataException("The data is not a Type 1 font: it has no eexec part.");
        }

        var start = eexec + 5;
        if (start < data.Length && data[start] == '\r')
        {
            start++;
            if (start < data.Length && data[start] == '\n')
            {
                start++;
            }
        }
        else if (start < data.Length && data[start] is (byte)'\n' or (byte)' ' or (byte)'\t')
        {
            start++;
        }

        var clear = data.AsSpan(0, eexec).ToArray();
        var rest = data.AsSpan(start);
        return (clear, IsHex(rest) ? FromHex(rest) : rest.ToArray());
    }

    private static (byte[] Clear, byte[] Encrypted) SplitSegments(byte[] data)
    {
        var clear = new MemoryStream();
        var encrypted = new MemoryStream();
        var position = 0;
        while (position + 2 <= data.Length && data[position] == 0x80)
        {
            var type = data[position + 1];
            if (type == 3)
            {
                break;
            }

            if (position + 6 > data.Length)
            {
                throw new InvalidDataException("A PFB segment header is cut off.");
            }

            long length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 2, 4));
            position += 6;
            if (length > data.Length - position)
            {
                throw new InvalidDataException("A PFB segment runs past the end of the file.");
            }

            (type == 2 ? encrypted : clear).Write(data, position, (int)length);
            position += (int)length;
        }

        if (encrypted.Length == 0)
        {
            throw new InvalidDataException("The PFB file has no encrypted part.");
        }

        var clearBytes = clear.ToArray();
        var eexec = EexecOf(clearBytes);
        return (eexec < 0 ? clearBytes : clearBytes.AsSpan(0, eexec).ToArray(), encrypted.ToArray());
    }

    private static void ReadClearText(byte[] clear, Type1FontProgram program)
    {
        var tokens = new Type1Tokenizer(clear);
        while (true)
        {
            var token = tokens.Next();
            if (token.Kind == Type1TokenKind.End)
            {
                return;
            }

            if (token.Kind != Type1TokenKind.Name)
            {
                continue;
            }

            switch (token.Text)
            {
                case "FontName":
                    var name = tokens.Next();
                    if (name.Kind == Type1TokenKind.Name && program.FontName == null)
                    {
                        program.FontName = name.Text;
                    }

                    break;
                case "FontMatrix":
                    program.FontMatrix ??= ReadNumbers(tokens);
                    break;
                case "FontBBox":
                    program.FontBBox ??= ReadNumbers(tokens);
                    break;
                case "Encoding":
                    if (program.Encoding == null)
                    {
                        program.Encoding = ReadEncoding(tokens);
                    }

                    break;
            }
        }
    }

    private static string[] ReadEncoding(Type1Tokenizer tokens)
    {
        var token = tokens.Next();
        if (token.Kind != Type1TokenKind.Number)
        {
            return null;
        }

        var names = new string[256];
        var window = new Type1Token[4];
        while (true)
        {
            token = tokens.Next();
            if (token.Kind == Type1TokenKind.End || token.Is("def") || token.Is("readonly"))
            {
                return names;
            }

            window[0] = window[1];
            window[1] = window[2];
            window[2] = window[3];
            window[3] = token;
            if (window[0].Is("dup") && window[1].Kind == Type1TokenKind.Number &&
                window[2].Kind == Type1TokenKind.Name && window[3].Is("put"))
            {
                var code = window[1].Number;
                if (code is >= 0 and < 256)
                {
                    names[(int)code] = window[2].Text;
                }
            }
        }
    }

    private static double[] ReadNumbers(Type1Tokenizer tokens)
    {
        var open = tokens.Next();
        if (open.Kind != Type1TokenKind.Open)
        {
            return null;
        }

        var numbers = new List<double>();
        while (true)
        {
            var token = tokens.Next();
            if (token.Kind != Type1TokenKind.Number)
            {
                return token.Kind == Type1TokenKind.Close ? numbers.ToArray() : null;
            }

            numbers.Add(token.Number);
        }
    }

    private static void ReadPrivate(byte[] data, Type1FontProgram program)
    {
        var tokens = new Type1Tokenizer(data);
        var pending = (Type1Token?)null;
        while (true)
        {
            var token = pending ?? tokens.Next();
            pending = null;
            if (token.Kind == Type1TokenKind.End)
            {
                return;
            }

            if (token.Kind != Type1TokenKind.Name)
            {
                continue;
            }

            switch (token.Text)
            {
                case "lenIV":
                    var lenIV = tokens.Next();
                    if (lenIV.Kind == Type1TokenKind.Number && lenIV.Number is >= -1 and <= 255)
                    {
                        program.LenIV = (int)lenIV.Number;
                    }

                    break;
                case "Subrs":
                    pending = ReadSubrs(tokens, program);
                    break;
                case "CharStrings":
                    ReadCharStrings(tokens, program);
                    if (program.CharStrings.Count > 0)
                    {
                        return;
                    }

                    break;
            }
        }
    }

    private static Type1Token? ReadSubrs(Type1Tokenizer tokens, Type1FontProgram program)
    {
        var count = tokens.Next();
        if (count.Kind != Type1TokenKind.Number)
        {
            return count;
        }

        while (true)
        {
            var token = tokens.Next();
            if (token.Is("array") || token.Is("NP") || token.Is("|") || token.Is("put") || token.Is("noaccess") ||
                token.Is("readonly") || token.Is("executeonly"))
            {
                continue;
            }

            if (!token.Is("dup"))
            {
                return token;
            }

            var index = tokens.Next();
            var length = tokens.Next();
            if (index.Kind != Type1TokenKind.Number || length.Kind != Type1TokenKind.Number)
            {
                return length;
            }

            tokens.Next();
            var code = tokens.ReadBinary(length.Number);
            if (index.Number is >= 0 and < MaxSubrs)
            {
                program.Subrs[(int)index.Number] = code;
            }
        }
    }

    private static void ReadCharStrings(Type1Tokenizer tokens, Type1FontProgram program)
    {
        while (true)
        {
            var token = tokens.Next();
            if (token.Kind == Type1TokenKind.End)
            {
                return;
            }

            if (token.Is("begin"))
            {
                break;
            }
        }

        while (true)
        {
            var token = tokens.Next();
            if (token.Kind == Type1TokenKind.Keyword && !token.Is("end"))
            {
                continue;
            }

            if (token.Kind != Type1TokenKind.Name)
            {
                return;
            }

            var length = tokens.Next();
            if (length.Kind != Type1TokenKind.Number)
            {
                return;
            }

            tokens.Next();
            var code = tokens.ReadBinary(length.Number);
            if (program.CharStrings.TryAdd(token.Text, code))
            {
                program.GlyphNames.Add(token.Text);
            }
        }
    }

    private static bool IsHex(ReadOnlySpan<byte> data)
    {
        var digits = 0;
        foreach (var b in data)
        {
            if (Type1Tokenizer.IsWhitespace(b))
            {
                continue;
            }

            if (HexValue(b) < 0)
            {
                return false;
            }

            if (++digits == 4)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] FromHex(ReadOnlySpan<byte> data)
    {
        var bytes = new List<byte>(data.Length / 2);
        var high = -1;
        foreach (var b in data)
        {
            if (Type1Tokenizer.IsWhitespace(b))
            {
                continue;
            }

            var value = HexValue(b);
            if (value < 0)
            {
                break;
            }

            if (high < 0)
            {
                high = value;
            }
            else
            {
                bytes.Add((byte)(high << 4 | value));
                high = -1;
            }
        }

        return bytes.ToArray();
    }

    private static int HexValue(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => -1,
    };
}
