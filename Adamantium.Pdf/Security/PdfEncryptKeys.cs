namespace Adamantium.Pdf.Security;

/// <summary>The keys of the encryption dictionary and its crypt filters (ISO 32000-2, tables 20, 21 and 25).</summary>
internal static class PdfEncryptKeys
{
    /// <summary>The security handler.</summary>
    public const string Filter = "Filter";

    /// <summary>The algorithm version.</summary>
    public const string V = "V";

    /// <summary>The standard security handler's revision.</summary>
    public const string R = "R";

    /// <summary>The owner password hash.</summary>
    public const string O = "O";

    /// <summary>The user password hash.</summary>
    public const string U = "U";

    /// <summary>The file key encrypted with the owner password (revision 6).</summary>
    public const string OE = "OE";

    /// <summary>The file key encrypted with the user password (revision 6).</summary>
    public const string UE = "UE";

    /// <summary>The permission flags.</summary>
    public const string P = "P";

    /// <summary>The file key length in bits.</summary>
    public const string Length = "Length";

    public const string EncryptMetadata = "EncryptMetadata";

    /// <summary>The crypt filters by name.</summary>
    public const string CF = "CF";

    /// <summary>The crypt filter for streams.</summary>
    public const string StmF = "StmF";

    /// <summary>The crypt filter for strings.</summary>
    public const string StrF = "StrF";

    /// <summary>A crypt filter's method.</summary>
    public const string CFM = "CFM";
}
