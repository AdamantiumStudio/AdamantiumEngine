using System;

namespace Adamantium.Pdf;

/// <summary>A file that cannot be read as PDF, or a part of it that cannot.</summary>
public class PdfException : Exception
{
    public PdfException(string message) : base(message)
    {
    }

    public PdfException(string message, Exception inner) : base(message, inner)
    {
    }
}
