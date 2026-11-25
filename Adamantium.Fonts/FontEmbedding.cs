namespace Adamantium.Fonts;

/// <summary>What a font's license lets a document carry of it ('OS/2' fsType).</summary>
/// <param name="IsAllowed">Whether a document may carry the font's outlines as a font.</param>
/// <param name="MaySubset">Whether it may carry some glyphs without the rest.</param>
public readonly record struct FontEmbedding(bool IsAllowed, bool MaySubset)
{
    private const ushort Restricted = 0x0002;
    private const ushort PreviewAndPrint = 0x0004;
    private const ushort Editable = 0x0008;
    private const ushort NoSubsetting = 0x0100;
    private const ushort BitmapsOnly = 0x0200;

    /// <summary>A font that says nothing against it.</summary>
    public static FontEmbedding Free { get; } = new(true, true);

    /// <summary>As <paramref name="flags"/> say: where several licenses are named the least restrictive one holds.
    /// </summary>
    internal static FontEmbedding From(ushort flags)
    {
        var restricted = (flags & (Restricted | PreviewAndPrint | Editable)) == Restricted;
        return new FontEmbedding(!restricted && (flags & BitmapsOnly) == 0, (flags & NoSubsetting) == 0);
    }
}
