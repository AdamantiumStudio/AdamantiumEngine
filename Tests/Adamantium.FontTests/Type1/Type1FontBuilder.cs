using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Adamantium.FontTests.Type1;

/// <summary>Writes a small Type 1 font program the way the Type 1 format lays one out, in each of its file forms.</summary>
internal sealed class Type1FontBuilder
{
    private static readonly Dictionary<string, byte[]> Operators = new()
    {
        ["hstem"] = [1], ["vstem"] = [3], ["vmoveto"] = [4], ["rlineto"] = [5], ["hlineto"] = [6],
        ["vlineto"] = [7], ["rrcurveto"] = [8], ["closepath"] = [9], ["callsubr"] = [10], ["return"] = [11],
        ["hsbw"] = [13], ["endchar"] = [14], ["rmoveto"] = [21], ["hmoveto"] = [22], ["vhcurveto"] = [30],
        ["hvcurveto"] = [31], ["dotsection"] = [12, 0], ["seac"] = [12, 6], ["sbw"] = [12, 7], ["div"] = [12, 12],
        ["callothersubr"] = [12, 16], ["pop"] = [12, 17], ["setcurrentpoint"] = [12, 33],
    };

    public string FontName { get; set; } = "TestType1";

    public string FontMatrix { get; set; } = "[0.001 0 0 0.001 0 0]";

    /// <summary>The PostScript after <c>/Encoding</c>, up to and including its <c>def</c>.</summary>
    public string Encoding { get; set; } = "StandardEncoding def";

    public int LenIV { get; set; } = 4;

    public List<byte[]> Subrs { get; } = [];

    public List<(string Name, byte[] Code)> Glyphs { get; } = [];

    /// <summary>A charstring from numbers and operator names.</summary>
    public static byte[] CharString(params object[] items)
    {
        var bytes = new List<byte>();
        foreach (var item in items)
        {
            if (item is string name)
            {
                bytes.AddRange(Operators[name]);
                continue;
            }

            var v = Convert.ToInt32(item);
            if (v is >= -107 and <= 107)
            {
                bytes.Add((byte)(v + 139));
            }
            else if (v is >= 108 and <= 1131)
            {
                v -= 108;
                bytes.Add((byte)((v >> 8) + 247));
                bytes.Add((byte)v);
            }
            else if (v is >= -1131 and <= -108)
            {
                v = -v - 108;
                bytes.Add((byte)((v >> 8) + 251));
                bytes.Add((byte)v);
            }
            else
            {
                bytes.Add(255);
                bytes.Add((byte)(v >> 24));
                bytes.Add((byte)(v >> 16));
                bytes.Add((byte)(v >> 8));
                bytes.Add((byte)v);
            }
        }

        return bytes.ToArray();
    }

    /// <summary>The standard flex and hint replacement subroutines 0 to 4.</summary>
    public Type1FontBuilder WithStandardSubrs()
    {
        Subrs.Add(CharString(3, 0, "callothersubr", "pop", "pop", "setcurrentpoint", "return"));
        Subrs.Add(CharString(0, 1, "callothersubr", "return"));
        Subrs.Add(CharString(0, 2, "callothersubr", "return"));
        Subrs.Add(CharString("return"));
        Subrs.Add(CharString(1, 3, "callothersubr", "pop", "callsubr", "return"));
        return this;
    }

    /// <summary>The cipher of the Type 1 format: c = p xor (r >> 8), r = (c + r) * 52845 + 22719.</summary>
    public static byte[] Encrypt(byte[] plain, ushort key, int prefix)
    {
        if (prefix < 0)
        {
            return plain;
        }

        var input = new byte[prefix + plain.Length];
        Buffer.BlockCopy(plain, 0, input, prefix, plain.Length);
        var output = new byte[input.Length];
        var r = key;
        for (var i = 0; i < input.Length; i++)
        {
            var c = (byte)(input[i] ^ (r >> 8));
            r = (ushort)((c + r) * 52845 + 22719);
            output[i] = c;
        }

        return output;
    }

    public byte[] ClearText() => Ascii(
        "%!PS-AdobeFont-1.0: " + FontName + " 001\n" +
        "% a comment with /Encoding and eexe in it\n" +
        "12 dict begin\n" +
        "/FontInfo 2 dict dup begin /FullName (Test (Type) 1) readonly def end readonly def\n" +
        "/FontName /" + FontName + " def\n" +
        "/PaintType 0 def\n" +
        "/FontMatrix " + FontMatrix + " readonly def\n" +
        "/FontBBox {0 -200 1000 900} readonly def\n" +
        "/Encoding " + Encoding + "\n" +
        "currentfile eexec\n");

    /// <summary>The encrypted private part, as binary.</summary>
    public byte[] EncryptedPart()
    {
        var plain = new MemoryStream();
        Write(plain, "dup /Private 8 dict dup begin\n/RD{string currentfile exch readstring pop}executeonly def\n" +
                     "/ND{noaccess def}executeonly def\n/NP{noaccess put}executeonly def\n" +
                     "/BlueValues [-20 0 700 720] def\n");
        Write(plain, "/lenIV " + LenIV + " def\n");
        Write(plain, "/Subrs " + Subrs.Count + " array\n");
        for (var i = 0; i < Subrs.Count; i++)
        {
            var code = Encrypt(Subrs[i], 4330, LenIV);
            Write(plain, "dup " + i + " " + code.Length + " RD ");
            plain.Write(code);
            Write(plain, " NP\n");
        }

        Write(plain, "ND\n2 index /CharStrings " + Glyphs.Count + " dict dup begin\n");
        foreach (var (name, glyphCode) in Glyphs)
        {
            var code = Encrypt(glyphCode, 4330, LenIV);
            Write(plain, "/" + name + " " + code.Length + " -| ");
            plain.Write(code);
            Write(plain, " |-\n");
        }

        Write(plain, "end\nend\nreadonly put\nnoaccess put\ndup /FontName get exch definefont pop\n" +
                     "mark currentfile closefile\n");
        return Encrypt(plain.ToArray(), 55665, 4);
    }

    /// <summary>The form PDF embeds as <c>FontFile</c>: clear text, binary, and the trailer of zeros.</summary>
    public byte[] BuildPdfFontFile()
    {
        var output = new MemoryStream();
        output.Write(ClearText());
        output.Write(EncryptedPart());
        output.Write(Trailer());
        return output.ToArray();
    }

    public byte[] BuildPfa()
    {
        var output = new MemoryStream();
        output.Write(ClearText());
        var encrypted = EncryptedPart();
        var hex = new StringBuilder();
        for (var i = 0; i < encrypted.Length; i++)
        {
            hex.Append(encrypted[i].ToString("x2"));
            if (i % 32 == 31)
            {
                hex.Append('\n');
            }
        }

        hex.Append('\n');
        output.Write(Ascii(hex.ToString()));
        output.Write(Trailer());
        return output.ToArray();
    }

    public byte[] BuildPfb()
    {
        var output = new MemoryStream();
        Segment(output, 1, ClearText());
        Segment(output, 2, EncryptedPart());
        Segment(output, 1, Trailer());
        output.Write([0x80, 3]);
        return output.ToArray();
    }

    private static void Segment(MemoryStream output, byte type, byte[] data)
    {
        output.Write([0x80, type]);
        output.Write(BitConverter.GetBytes(data.Length));
        output.Write(data);
    }

    private static byte[] Trailer()
    {
        var zeros = new StringBuilder();
        for (var i = 0; i < 8; i++)
        {
            zeros.Append('0', 64).Append('\n');
        }

        return Ascii(zeros + "cleartomark\n");
    }

    private static void Write(MemoryStream stream, string text) => stream.Write(Ascii(text));

    private static byte[] Ascii(string text) => System.Text.Encoding.ASCII.GetBytes(text);
}
