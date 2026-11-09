namespace Adamantium.Pdf.Fonts;

/// <summary>A character code read out of a string shown with a font, and how many bytes it took.</summary>
public readonly record struct PdfCharacter(uint Code, int Length)
{
    /// <summary>Whether word spacing applies to it: a single-byte code 32 (ISO 32000-2, 9.3.3).</summary>
    public bool IsWordSpace => Length == 1 && Code == 32;
}
