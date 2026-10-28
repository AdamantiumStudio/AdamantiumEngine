using Adamantium.Mathematics;

namespace Adamantium.Fonts;

/// <summary>
/// A step of a color glyph's paint program ('COLR' version 1), the paint graph walked in drawing order: clips by glyph
/// outlines, groups composited with a mode, and fills. Transforms are folded into the steps, in font units with y up.
/// </summary>
public sealed class ColorPaintOperation
{
    private ColorPaintOperation(ColorPaintOperationKind kind)
    {
        Kind = kind;
    }

    /// <summary>What the step does.</summary>
    public ColorPaintOperationKind Kind { get; }

    /// <summary>The glyph whose outline a <see cref="ColorPaintOperationKind.PushClip"/> cuts to.</summary>
    public uint GlyphIndex { get; private set; }

    /// <summary>From the clipping outline's space into the color glyph's.</summary>
    public Matrix3x2<double> Transform { get; private set; }

    /// <summary>What a <see cref="ColorPaintOperationKind.Fill"/> paints; its transform maps into the color glyph's
    /// space too.</summary>
    public ColorFill Fill { get; private set; }

    /// <summary>How a <see cref="ColorPaintOperationKind.PopGroup"/> composites its group.</summary>
    public ColorCompositeMode Mode { get; private set; }

    /// <summary>A step that cuts what follows to <paramref name="glyphIndex"/>'s outline, placed by
    /// <paramref name="transform"/>.</summary>
    public static ColorPaintOperation PushClip(uint glyphIndex, Matrix3x2<double> transform) =>
        new(ColorPaintOperationKind.PushClip) { GlyphIndex = glyphIndex, Transform = transform };

    /// <summary>A step that takes the last clip away.</summary>
    public static ColorPaintOperation PopClip() => new(ColorPaintOperationKind.PopClip);

    /// <summary>A step that starts a group.</summary>
    public static ColorPaintOperation PushGroup() => new(ColorPaintOperationKind.PushGroup);

    /// <summary>A step that ends a group and composites it with <paramref name="mode"/>.</summary>
    public static ColorPaintOperation PopGroup(ColorCompositeMode mode) =>
        new(ColorPaintOperationKind.PopGroup) { Mode = mode };

    /// <summary>A step that fills the clips in force with <paramref name="fill"/>.</summary>
    public static ColorPaintOperation FillWith(ColorFill fill) => new(ColorPaintOperationKind.Fill) { Fill = fill };
}
