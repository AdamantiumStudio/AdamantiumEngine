namespace Adamantium.Pdf.Images;

/// <summary>An image decoded for drawing: rows top to bottom, 8-bit RGBA with straight alpha. A stencil mask carries
/// only its alpha - where to paint - and takes its color from the fill.</summary>
public sealed class PdfImageBitmap
{
    public PdfImageBitmap(int width, int height, byte[] rgba, bool isStencil)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
        IsStencil = isStencil;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Rgba { get; }

    /// <summary>Whether this is an <c>/ImageMask</c>: paint the fill color where alpha is set.</summary>
    public bool IsStencil { get; }
}
