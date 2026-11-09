using System;
using System.IO;

namespace Adamantium.Pdf.Fonts;

/// <summary>The predefined CMaps of ISO 32000-1, table 118, and those they use, from Adobe's CMap resources the
/// assembly carries: Adobe-Japan1, Adobe-GB1, Adobe-CNS1 and Adobe-Korea1.</summary>
internal static class PdfPredefinedCMaps
{
    public const string Japan1 = "Japan1";
    public const string GB1 = "GB1";
    public const string CNS1 = "CNS1";
    public const string Korea1 = "Korea1";

    private const string Prefix = "Adamantium.Pdf.CMaps.";

    /// <summary>The text of the CMap of <paramref name="name"/>; empty when there is no such CMap.</summary>
    public static ReadOnlyMemory<byte> Data(string name)
    {
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(['/', '\\', '.']) >= 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        using var stream = typeof(PdfPredefinedCMaps).Assembly.GetManifestResourceStream(Prefix + name);
        if (stream == null)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return data;
    }

    /// <summary>The Unicode CMaps of a character collection - their codes UTF-16, their CIDs the collection's - which,
    /// read backwards, give each CID its text, the first to give one winning: the UTF-16 ones, then the older UCS-2
    /// ones, and Japan1's half-width one, which maps Latin to the half-width CIDs; each vertical first, so a vertical
    /// form reads as the character it stands for. Empty for a collection the engine does not carry.</summary>
    public static string[] UnicodeCMapsOf(string ordering) => ordering switch
    {
        Japan1 =>
        [
            "UniJIS-UTF16-V", "UniJIS-UTF16-H", "UniJIS-UCS2-V", "UniJIS-UCS2-H", "UniJIS-UCS2-HW-V",
            "UniJIS-UCS2-HW-H",
        ],
        GB1 => ["UniGB-UTF16-V", "UniGB-UTF16-H", "UniGB-UCS2-V", "UniGB-UCS2-H"],
        CNS1 => ["UniCNS-UTF16-V", "UniCNS-UTF16-H", "UniCNS-UCS2-V", "UniCNS-UCS2-H"],
        Korea1 => ["UniKS-UTF16-V", "UniKS-UTF16-H", "UniKS-UCS2-V", "UniKS-UCS2-H"],
        _ => [],
    };

    /// <summary>Whether a predefined CMap's codes are UTF-16 (or UCS-2) code units, so a code is its own text.
    /// </summary>
    public static bool IsUnicode(string name) =>
        name != null && name.StartsWith("Uni", StringComparison.Ordinal) &&
        (name.Contains("-UCS2-", StringComparison.Ordinal) || name.Contains("-UTF16-", StringComparison.Ordinal));
}
