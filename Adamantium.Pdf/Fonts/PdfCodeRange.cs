namespace Adamantium.Pdf.Fonts;

/// <summary>The codes from <see cref="Low"/> to <see cref="High"/>, of <see cref="Length"/> bytes, mapped in order
/// from the code <see cref="Origin"/>: to CIDs from <see cref="Start"/>, or to texts whose last code unit counts up
/// from <see cref="Text"/>'s, or to the <see cref="Texts"/> one by one (ISO 32000-2, 9.7.5.4 and 9.10.3).</summary>
internal readonly record struct PdfCodeRange(int Length, uint Low, uint High, uint Start, string Text = null,
    string[] Texts = null)
{
    /// <summary>The code the mapping counts from: <see cref="Low"/>, unless this is a piece cut from a wider range.
    /// </summary>
    public uint Origin { get; init; } = Low;

    public bool Contains(uint code, int length) => length == Length && code >= Low && code <= High;

    public PdfCodeRange Piece(uint low, uint high) => this with { Low = low, High = high };
}
