using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Adamantium.FontTests.Cff;

/// <summary>Writes a small bare CFF font as the Compact Font Format lays one out: header, Name, Top DICT, String and
/// Global Subr INDEXes, CharStrings and a Private DICT.</summary>
internal sealed class CffFontBuilder
{
    public List<byte[]> CharStrings { get; } = [[14], [14]];

    public List<byte[]> GlobalSubrs { get; } = [];

    public List<string> Strings { get; } = [];

    public byte[] PrivateDict { get; set; } = [];

    /// <summary>The Top DICT charset operand: a predefined id (0 to 2), or null to leave it out.</summary>
    public int? Charset { get; set; }

    /// <summary>The Top DICT Encoding operand: a predefined id (0 or 1), or null to leave it out.</summary>
    public int? Encoding { get; set; }

    public byte[] Build()
    {
        var name = Index([System.Text.Encoding.ASCII.GetBytes("Test")]);
        var strings = Index(Strings.Select(s => System.Text.Encoding.ASCII.GetBytes(s)).ToList());
        var globalSubrs = Index(GlobalSubrs);
        var charStrings = Index(CharStrings);

        var topSize = TopDict(0, 0).Length;
        var topIndexSize = Index([new byte[topSize]]).Length;
        var charStringsOffset = 4 + name.Length + topIndexSize + strings.Length + globalSubrs.Length;
        var privateOffset = charStringsOffset + charStrings.Length;
        var top = Index([TopDict(charStringsOffset, privateOffset)]);

        var output = new MemoryStream();
        output.Write([1, 0, 4, 4]);
        output.Write(name);
        output.Write(top);
        output.Write(strings);
        output.Write(globalSubrs);
        output.Write(charStrings);
        output.Write(PrivateDict);
        return output.ToArray();
    }

    private byte[] TopDict(int charStringsOffset, int privateOffset)
    {
        var dict = new List<byte>();
        if (Charset is { } charset)
        {
            dict.AddRange(Integer(charset));
            dict.Add(15);
        }

        if (Encoding is { } encoding)
        {
            dict.AddRange(Integer(encoding));
            dict.Add(16);
        }

        dict.AddRange(Integer(charStringsOffset));
        dict.Add(17);
        dict.AddRange(Integer(PrivateDict.Length));
        dict.AddRange(Integer(privateOffset));
        dict.Add(18);
        return dict.ToArray();
    }

    private static byte[] Integer(int value) => [29, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    /// <summary>An INDEX with four-byte offsets.</summary>
    public static byte[] Index(IReadOnlyList<byte[]> items)
    {
        var output = new MemoryStream();
        output.Write([(byte)(items.Count >> 8), (byte)items.Count]);
        if (items.Count == 0)
        {
            return output.ToArray();
        }

        output.WriteByte(4);
        var offset = 1;
        foreach (var item in items.Prepend(null))
        {
            offset += item?.Length ?? 0;
            output.Write([(byte)(offset >> 24), (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset]);
        }

        foreach (var item in items)
        {
            output.Write(item);
        }

        return output.ToArray();
    }
}
