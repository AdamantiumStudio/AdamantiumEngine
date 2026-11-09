namespace Adamantium.Pdf.Fonts;

/// <summary>A codespace range of a CMap: the codes of <see cref="Length"/> bytes whose every byte lies between the
/// matching bytes of <see cref="Low"/> and <see cref="High"/>.</summary>
internal readonly record struct PdfCodeSpaceRange(int Length, uint Low, uint High)
{
    public bool Contains(uint code, int length)
    {
        if (length != Length)
        {
            return false;
        }

        for (var shift = 0; shift < 8 * length; shift += 8)
        {
            var value = (code >> shift) & 0xFF;
            if (value < ((Low >> shift) & 0xFF) || value > ((High >> shift) & 0xFF))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether a code's first byte lies within the range's first byte.</summary>
    public bool StartsWith(byte first)
    {
        var shift = 8 * (Length - 1);
        return first >= ((Low >> shift) & 0xFF) && first <= ((High >> shift) & 0xFF);
    }
}
