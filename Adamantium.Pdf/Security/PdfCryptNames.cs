namespace Adamantium.Pdf.Security;

/// <summary>The security handler and crypt filter names (ISO 32000-2, 7.6).</summary>
internal static class PdfCryptNames
{
    /// <summary>The standard security handler.</summary>
    public const string Standard = "Standard";

    /// <summary>The crypt filter that leaves data as it is.</summary>
    public const string Identity = "Identity";

    /// <summary>RC4.</summary>
    public const string V2 = "V2";

    /// <summary>AES with a 128-bit key.</summary>
    public const string AESV2 = "AESV2";

    /// <summary>AES with a 256-bit key.</summary>
    public const string AESV3 = "AESV3";
}
