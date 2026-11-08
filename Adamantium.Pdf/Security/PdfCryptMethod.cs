namespace Adamantium.Pdf.Security;

/// <summary>How a crypt filter encrypts (ISO 32000-2, 7.6.5, <c>/CFM</c>).</summary>
public enum PdfCryptMethod
{
    /// <summary><c>/None</c> or <c>/Identity</c>: not encrypted.</summary>
    None,

    /// <summary><c>/V2</c>: RC4 with a key per object.</summary>
    Rc4,

    /// <summary><c>/AESV2</c>: AES-128 in CBC mode with a key per object.</summary>
    Aes128,

    /// <summary><c>/AESV3</c>: AES-256 in CBC mode with the file key.</summary>
    Aes256,
}
