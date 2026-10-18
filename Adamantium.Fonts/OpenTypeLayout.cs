using System.Collections.Concurrent;
using Adamantium.Fonts.Shaping;
using Adamantium.Fonts.Tables;
using Adamantium.Fonts.Tables.GPOS;
using Adamantium.Fonts.Tables.GSUB;

namespace Adamantium.Fonts;

internal sealed class OpenTypeLayout
{
    public GlyphSubstitutionTable Gsub { get; set; }

    public GlyphPositioningTable Gpos { get; set; }

    public GlyphDefinitionTable Gdef { get; set; }

    public KerningSubtable[] KernSubtables { get; set; }

    public bool HasKernTable => KernSubtables is { Length: > 0 };

    public bool HasCrossKerning { get; set; }

    // A subtable of format 1, a state machine: HarfBuzz then leaves marks their width.
    public bool HasMachineKerning { get; set; }

    public ConcurrentDictionary<string, ShapePlan> Plans { get; } = new();

    public bool HasGlyphClasses => Gdef?.GlyphClassDefTable != null;

    public ushort GetGlyphProps(uint glyph)
    {
        switch (Gdef.GlyphClassDefTable.GetClassValue((ushort)glyph))
        {
            case 1:
                return GlyphProps.BaseGlyph;
            case 2:
                return GlyphProps.Ligature;
            case 3:
                var attachClass = Gdef.MarkAttachClassDefTable?.GetClassValue((ushort)glyph) ?? 0;
                return (ushort)(GlyphProps.Mark | (attachClass << 8));
            default:
                return 0;
        }
    }

    public bool IsInMarkSet(int setIndex, uint glyph)
    {
        var sets = Gdef?.MarkGlyphSetsTable?.CoverageTables;
        if (sets == null || setIndex >= sets.Length)
        {
            return false;
        }

        return sets[setIndex].FindPosition((ushort)glyph) >= 0;
    }
}
